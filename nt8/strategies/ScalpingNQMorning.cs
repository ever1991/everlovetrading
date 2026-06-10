#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

// ============================================================================
// ScalpingNQMorning  —  port a NinjaScript del Pine v4
//   "Scalping NQ Morning - Breakout Signals"
//
// Estrategia: rompimiento de PDH/PDL/Opening Range en sesión 8:30-12:00 CDMX
// sobre NQ/MNQ, con filtros combinados EMA8>SMA20 + lado de VWAP + volumen
// > promedio + delta orderflow ≥ deltaPctMin.
//
// REQUISITOS NT8:
//   1. NT8 8.1+ (para OrderFlowCumulativeDelta nativo)
//   2. Tick Replay habilitado en la connection (Tools → Options → Market data)
//   3. Subscription a data con Volumetric Bars / Order Flow (Apex/Rithmic ✓)
//
// Diseño defensivo (Apex 50K + Emotional Manager + Replicator):
//   • Si el Emotional Manager cierra la posición externamente, el bot
//     detecta Flat-inesperado y se session-lockea hasta el siguiente día.
//   • Daily loss interno $300 (más estricto que el cap $1k del gestor)
//   • Auto-disable al alcanzar el lifetime target (default $6000/cuenta;
//     señal para mover a PA). Es por cuenta: CumProfit de la instancia.
//   • Breakout ARMADO + retest (10-jun): un cruce de nivel arma la dirección
//     ArmBars velas (default 6 = 30m). Entra en cuanto EMA/VWAP se alinean
//     dentro de la ventana (no solo en la vela del cruce) y re-arma en el
//     retest. Resuelve el "casi no entra": antes exigía cruce + filtros en
//     la MISMA vela, perdiendo la señal si el cruce era con gap o sin
//     alineación instantánea.
//   • OpenRiskMode (08-jun, default OFF): SL/TP por trade y topes diarios
//     ACTIVOS. Cada trade lleva su stop ($200) y target ($300) reales, y al
//     cerrarse libera la posición para re-entrar (más trades/día). El Gestor
//     Emocional externo queda como respaldo. Prender el switch solo para volver
//     al modo "aguanta hasta fin de sesión sin stop".
//
// Repo: https://github.com/ever1991/everlovetrading
// ============================================================================

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ScalpingNQMorning : Strategy
    {
        // ===================== INPUTS =====================
        #region Inputs

        // ---------- Sesión (en hora CDMX, conversión automática) ----------
        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "Inicio sesión (HHMM CDMX)", Description = "Ej: 830 = 8:30am CDMX", GroupName = "1. Sesión", Order = 0)]
        public int SessionStartCdmx { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "Fin sesión (HHMM CDMX)", Description = "Ej: 1200 = 12:00pm CDMX", GroupName = "1. Sesión", Order = 1)]
        public int SessionEndCdmx { get; set; }

        [Range(1, 120), NinjaScriptProperty]
        [Display(Name = "Opening Range (min)", Description = "Minutos desde apertura para fijar OR High/Low", GroupName = "1. Sesión", Order = 2)]
        public int OpeningRangeMins { get; set; }

        // ---------- Niveles del día previo ----------
        [NinjaScriptProperty]
        [Display(Name = "Operar rompimiento PDH/PDL", GroupName = "2. Niveles", Order = 0)]
        public bool UsePdhPdl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Operar rompimiento OR High/Low", GroupName = "2. Niveles", Order = 1)]
        public bool UseOpeningRange { get; set; }

        [Range(1, 60), NinjaScriptProperty]
        [Display(Name = "Ventana de armado (velas)", Description = "Tras romper un nivel, el setup queda ARMADO esta cantidad de velas. Entra en cuanto EMA/VWAP se alineen dentro de la ventana (no solo en la vela del cruce), y re-arma si hace pullback y vuelve a romper. Default 6 = 30 min en 5m. Subir = más entradas.", GroupName = "2. Niveles", Order = 2)]
        public int ArmBars { get; set; }

        // ---------- Filtros técnicos ----------
        [NinjaScriptProperty]
        [Display(Name = "Filtro EMA8 vs SMA20 (tendencia)", GroupName = "3. Filtros", Order = 0)]
        public bool UseEma { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro VWAP (lado correcto)", GroupName = "3. Filtros", Order = 1)]
        public bool UseVwap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filtro Volumen > promedio", GroupName = "3. Filtros", Order = 2)]
        public bool UseVol { get; set; }

        [Range(1, 200), NinjaScriptProperty]
        [Display(Name = "Volumen — periodo promedio", GroupName = "3. Filtros", Order = 3)]
        public int VolLen { get; set; }

        // ---------- Delta orderflow ----------
        [NinjaScriptProperty]
        [Display(Name = "Filtro Delta (orderflow)", GroupName = "4. Delta", Order = 0)]
        public bool UseDelta { get; set; }

        [Range(0, 100), NinjaScriptProperty]
        [Display(Name = "Delta % mínimo de la barra", Description = "|delta/volumen| × 100. Default 50 (era 60 en Pine v4, bajado tras 2 sesiones sin señales).", GroupName = "4. Delta", Order = 1)]
        public double DeltaPctMin { get; set; }

        // ---------- Riesgo / size ----------
        [Range(1, 200), NinjaScriptProperty]
        [Display(Name = "ATR length", GroupName = "5. Riesgo", Order = 0)]
        public int AtrLen { get; set; }

        [Range(0.1, 5.0), NinjaScriptProperty]
        [Display(Name = "Stop Loss (× ATR)", GroupName = "5. Riesgo", Order = 1)]
        public double SlAtr { get; set; }

        [Range(0.1, 10.0), NinjaScriptProperty]
        [Display(Name = "Risk:Reward (TP = SL × RR)", GroupName = "5. Riesgo", Order = 2)]
        public double RrRatio { get; set; }

        [Range(1, 60), NinjaScriptProperty]
        [Display(Name = "Contratos por trade (MNQ)", Description = "Apex 50K: máx 60 MNQ. Default 5 alineado a la spec.", GroupName = "5. Riesgo", Order = 3)]
        public int ContractsQty { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TP/SL fijos en USD (en vez de ATR)", Description = "ON: usa Take Profit USD / Stop Loss USD. OFF: usa ATR×SlAtr y RR.", GroupName = "5. Riesgo", Order = 4)]
        public bool UseFixedDollarRisk { get; set; }

        [Range(1, 100000), NinjaScriptProperty]
        [Display(Name = "Take Profit USD", Description = "Ganancia objetivo por trade (posición completa). Solo si TP/SL fijos = ON.", GroupName = "5. Riesgo", Order = 5)]
        public double TpUsd { get; set; }

        [Range(1, 100000), NinjaScriptProperty]
        [Display(Name = "Stop Loss USD", Description = "Pérdida máxima por trade (posición completa). Solo si TP/SL fijos = ON.", GroupName = "5. Riesgo", Order = 6)]
        public double SlUsd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "⚠️ Modo RIESGO ABIERTO (sin SL/TP ni topes)", Description = "ON: NO coloca Stop Loss ni Take Profit por trade y desactiva los topes diarios (loss/profit cap). La posición solo cierra al fin de sesión. Pensado para correr con un Gestor Emocional externo. OFF: respeta SL/TP y topes normales.", GroupName = "5. Riesgo", Order = 7)]
        public bool OpenRiskMode { get; set; }

        // ---------- Guardrails (Apex + defensa en profundidad) ----------
        [Range(1, 50), NinjaScriptProperty]
        [Display(Name = "Max trades por día", GroupName = "6. Guardrails", Order = 0)]
        public int MaxTradesPerDay { get; set; }

        [Range(0, 5000), NinjaScriptProperty]
        [Display(Name = "Daily loss cap USD (apaga día)", Description = "$300 por defecto, más estricto que el $1k del Emotional Manager.", GroupName = "6. Guardrails", Order = 1)]
        public double DailyLossCapUsd { get; set; }

        [Range(0, 5000), NinjaScriptProperty]
        [Display(Name = "Daily profit cap USD (apaga día)", Description = "Apaga al alcanzarlo para no devolver ganancias.", GroupName = "6. Guardrails", Order = 2)]
        public double DailyProfitCapUsd { get; set; }

        [Range(0, 1000000), NinjaScriptProperty]
        [Display(Name = "Lifetime profit target USD", Description = "Auto-disable al alcanzarlo (señal para mover a PA). Es por CUENTA: CumProfit de esa instancia. Ajústalo por cuenta a tu target real. 0 = desactivado.", GroupName = "6. Guardrails", Order = 3)]
        public double LifetimeProfitTargetUsd { get; set; }

        #endregion

        // ===================== ESTADO INTERNO =====================
        #region State

        // Indicadores
        private EMA ema8;
        private SMA sma20;
        private SMA volSma;
        private ATR atr;
        private OrderFlowCumulativeDelta cumDelta;

        // VWAP calculado manualmente (reset diario, evita depender del indicador VWAP
        // nativo que no existe con ese nombre en todas las versiones de NT8)
        private double vwapNumerator = 0;
        private double vwapDenominator = 0;
        private DateTime vwapResetDate = DateTime.MinValue;
        private double currentVwap = double.NaN;

        // Niveles del día previo (calculados desde DataSeries diaria, BarsArray[1])
        private double pdh = double.NaN, pdl = double.NaN, pdc = double.NaN;

        // Opening Range del día actual
        private double orHigh = double.NaN, orLow = double.NaN;
        private bool orFrozen = false;
        private DateTime orFreezeAt = DateTime.MinValue;

        // Armado de rompimiento (breakout armado + retest):
        // al cruzar un nivel se "arma" la dirección por ArmBars velas. Mientras
        // siga armado y el precio se mantenga del lado roto, entra en cuanto los
        // filtros (EMA/VWAP) se alineen — no solo en la vela del cruce.
        private int    longArmedBars  = 0;
        private int    shortArmedBars = 0;
        private double longArmLevel   = double.NaN;
        private double shortArmLevel  = double.NaN;

        // Sesión / día
        private DateTime currentSessionDate = DateTime.MinValue;
        private bool sessionLocked = false;   // tras cierre externo o cap alcanzado
        private MarketPosition lastObservedPosition = MarketPosition.Flat;

        // Conteos diarios
        private int tradesToday = 0;
        private double realizedPnLToday = 0;
        private double lifetimeRealizedPnL = 0;
        // CumProfit snapshot al inicio del día CDMX. realizedPnLToday se deriva
        // como (CumProfit actual − este snapshot), robusto ante exits que se
        // llenan en varios fills: esos crean múltiples Trade records y antes,
        // al sumar solo AllTrades[last], se subcontaba la pérdida (bug 01-jun:
        // stop real -$125 reportado como -$50, riesgo para el daily loss cap).
        private double sessionStartCumProfit = 0;

        // Conversion CDMX (México NO hace DST desde 2022 — UTC-6 todo el año)
        private TimeZoneInfo cdmxTz;
        private TimeZoneInfo chicagoTz;

        #endregion

        // ===================== OVERRIDES =====================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ScalpingNQMorning — port del Pine v4. Rompimiento PDH/PDL/OR en sesión mañana CDMX con filtros EMA/VWAP/Vol/Delta. Diseñado para Apex 50K + Emotional Manager + Replicator.";
                Name        = "ScalpingNQMorning";

                Calculate                                   = Calculate.OnBarClose;
                EntriesPerDirection                         = 1;
                EntryHandling                               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy                = true;
                ExitOnSessionCloseSeconds                   = 30;
                IsFillLimitOnTouch                          = false;
                MaximumBarsLookBack                         = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                         = OrderFillResolution.Standard;
                Slippage                                    = 0;
                StartBehavior                               = StartBehavior.WaitUntilFlat;
                TimeInForce                                 = TimeInForce.Gtc;
                TraceOrders                                 = false;
                RealtimeErrorHandling                       = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                          = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                         = 30;
                IsInstantiatedOnEachOptimizationIteration   = true;

                // Defaults alineados al Pine v4 + ajuste de delta a 50 (pendiente del 18-may)
                SessionStartCdmx        = 830;
                SessionEndCdmx          = 1200;
                OpeningRangeMins        = 15;
                UsePdhPdl               = true;
                UseOpeningRange         = true;
                ArmBars                 = 6;      // 10-jun: breakout armado + retest — tras romper, 6 velas (30m) para que EMA/VWAP se alineen y entre. Sube las entradas.
                UseEma                  = true;
                UseVwap                 = true;
                UseVol                  = false;   // override actual del usuario
                VolLen                  = 20;
                UseDelta                = false;  // requiere subscription "Order Flow+" en NT; default OFF
                DeltaPctMin             = 50.0;
                AtrLen                  = 14;
                SlAtr                   = 1.0;
                RrRatio                 = 2.0;
                ContractsQty            = 5;
                UseFixedDollarRisk      = true;   // 02-jun: usuario pidió TP/SL fijos en USD
                TpUsd                   = 300;    // 08-jun: TP $300 con 5 MNQ = 30 pts (1:1.5)
                SlUsd                   = 200;    // 08-jun: SL $200 con 5 MNQ = 20 pts
                OpenRiskMode            = false;  // 08-jun: SL/TP por trade REACTIVADOS + topes diarios; al cerrar un trade permite re-entrada (más trades/día). Gestor Emocional = respaldo externo
                MaxTradesPerDay         = 3;      // 08-jun: máx 3 entradas/día
                DailyLossCapUsd         = 400;    // 08-jun: 2 stops ($200 c/u) y se apaga el día
                DailyProfitCapUsd       = 600;   // 08-jun: ~2 ganadores = día verde hecho; ajustable en el diálogo de la estrategia
                LifetimeProfitTargetUsd = 6000;  // 10-jun: subido de 3000 → 6000 (por cuenta). Ajústalo por cuenta a tu target real; 0 = desactivado.
            }
            else if (State == State.Configure)
            {
                // BarsArray[1] = serie diaria del MISMO instrumento → da
                // O/H/L/C del día previo cerrado para PDH/PDL/PDC.
                AddDataSeries(Instrument.FullName, BarsPeriodType.Day, 1);

                // BarsArray[2] = serie de Tick para alimentar a
                // OrderFlowCumulativeDelta SOLO si el filtro Delta está activo.
                // Si UseDelta=false (default) NO se carga — ahorra recursos y
                // evita warnings de subscription Order Flow+ ausente.
                if (UseDelta)
                {
                    AddDataSeries(Instrument.FullName, BarsPeriodType.Tick, 1);
                }
            }
            else if (State == State.DataLoaded)
            {
                ema8     = EMA(BarsArray[0], 8);
                sma20    = SMA(BarsArray[0], 20);
                volSma   = SMA(Volumes[0], VolLen);
                atr      = ATR(BarsArray[0], AtrLen);

                // OrderFlowCumulativeDelta requiere subscription "Order Flow+"
                // en NinjaTrader. Si el usuario no la tiene activa, el filtro
                // Delta se desactiva silenciosamente (deltaOk = true siempre)
                // y el bot opera con los demás filtros (PDH/PDL/OR + EMA/VWAP/Vol).
                if (UseDelta)
                {
                    try
                    {
                        cumDelta = OrderFlowCumulativeDelta(
                                      BarsArray[0],
                                      CumulativeDeltaType.BidAsk,
                                      CumulativeDeltaPeriod.Bar,
                                      0);
                    }
                    catch (Exception ex)
                    {
                        cumDelta = null;
                        Print($"WARN: OrderFlowCumulativeDelta no disponible — el filtro Delta se omite. Detalle: {ex.Message}");
                    }
                }

                cdmxTz    = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
                chicagoTz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            }
        }

        // ----------------------------------------------------------
        // OnBarUpdate — la lógica core, corre por cada barra cerrada
        // ----------------------------------------------------------
        protected override void OnBarUpdate()
        {
            // Sólo procesamos la serie primaria (5m); la daily (BarsArray[1])
            // se usa para leer PDH/PDL/PDC.
            if (BarsInProgress != 0) return;
            if (CurrentBars[0] < BarsRequiredToTrade) return;
            if (CurrentBars[1] < 2) return;

            // ---- 1. Hora del exchange (Chicago) → hora CDMX
            DateTime tNow = Time[0];                                  // exchange time (Chicago)
            DateTime tCdmx = ConvertChicagoToCdmx(tNow);
            int hhmmNow = tCdmx.Hour * 100 + tCdmx.Minute;
            DateTime today = tCdmx.Date;

            // ---- 2. Detectar nuevo día CDMX → resetear estado
            if (today != currentSessionDate)
            {
                currentSessionDate    = today;
                tradesToday           = 0;
                sessionStartCumProfit = CumProfit();
                realizedPnLToday      = 0;
                sessionLocked         = false;
                orHigh                = double.NaN;
                orLow                 = double.NaN;
                orFrozen              = false;
                longArmedBars         = 0;
                shortArmedBars        = 0;
                longArmLevel          = double.NaN;
                shortArmLevel         = double.NaN;

                // Lee niveles del día previo desde la serie diaria
                pdh = Highs[1][1];      // [1][1] = bar 1 de la serie diaria = ayer cerrado
                pdl = Lows[1][1];
                pdc = Closes[1][1];

                Print($"[{tCdmx:yyyy-MM-dd}] Nuevo día — PDH={pdh:F2} PDL={pdl:F2} PDC={pdc:F2}");
            }

            // ---- 2b. VWAP intradía calculado manualmente (reset diario CDMX)
            //         Fórmula estándar: Σ(typicalPrice × volume) / Σ(volume).
            //         Más portable que el indicador VWAP nativo, que no existe
            //         con ese nombre en todas las versiones de NT8.
            if (today != vwapResetDate)
            {
                vwapNumerator   = 0;
                vwapDenominator = 0;
                vwapResetDate   = today;
            }
            double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            vwapNumerator   += typicalPrice * Volume[0];
            vwapDenominator += Volume[0];
            currentVwap      = vwapDenominator > 0
                               ? vwapNumerator / vwapDenominator
                               : Close[0];

            // ---- 3. Sesión activa?
            bool inSession = hhmmNow >= SessionStartCdmx && hhmmNow < SessionEndCdmx;

            // ---- 4. Construcción del Opening Range
            if (inSession && !orFrozen)
            {
                if (double.IsNaN(orHigh))
                {
                    orHigh = High[0];
                    orLow  = Low[0];
                    orFreezeAt = tCdmx.AddMinutes(OpeningRangeMins);
                }
                else
                {
                    if (High[0] > orHigh) orHigh = High[0];
                    if (Low[0]  < orLow)  orLow  = Low[0];
                }

                if (tCdmx >= orFreezeAt)
                {
                    orFrozen = true;
                    Print($"[{tCdmx:HH:mm}] OR fijado — High={orHigh:F2} Low={orLow:F2}");
                }
            }

            // ---- 5. Detección de cierre externo MOVIDA a OnExecutionUpdate.
            //         Comparar lastObservedPosition vs Position.MarketPosition
            //         aquí produce falsos positivos porque el bracket OCO del bot
            //         (SetStopLoss/SetProfitTarget) también lleva la posición a
            //         Flat tras un fill, indistinguible de un cierre externo.
            //         Ahora la detección se hace en OnExecutionUpdate filtrando
            //         por execution.Order.FromEntrySignal: si trae nuestro signal
            //         name ("Long_break"/"Short_break") = cierre interno; si no =
            //         externo y session-lock.
            lastObservedPosition = Position.MarketPosition;

            // ---- 6. Guardrails
            if (!inSession || sessionLocked) return;

            // Recalcular P&L del día desde la fuente autoritativa (CumProfit) en
            // CADA barra → el daily loss cap siempre evalúa el número real,
            // aunque un OnExecutionUpdate se pierda o un exit se llene en partes.
            realizedPnLToday    = CumProfit() - sessionStartCumProfit;
            lifetimeRealizedPnL = CumProfit();

            if (tradesToday >= MaxTradesPerDay) { LockDay($"Max trades/día {MaxTradesPerDay} alcanzado."); return; }
            // OpenRiskMode: sin topes diarios de P&L. El Gestor Emocional externo
            // gobierna cuándo detener el día.
            if (!OpenRiskMode && realizedPnLToday <= -DailyLossCapUsd) { LockDay($"Daily loss cap -${DailyLossCapUsd} alcanzado."); return; }
            if (!OpenRiskMode && realizedPnLToday >=  DailyProfitCapUsd) { LockDay($"Daily profit cap +${DailyProfitCapUsd} alcanzado."); return; }
            if (lifetimeRealizedPnL >= LifetimeProfitTargetUsd) { LockDay($"Lifetime target +${LifetimeProfitTargetUsd} alcanzado — mover a PA."); return; }

            // ---- 7. Filtros
            bool emaBull = !UseEma  || ema8[0] > sma20[0];
            bool emaBear = !UseEma  || ema8[0] < sma20[0];

            bool vwapBull  = !UseVwap || Close[0] > currentVwap;
            bool vwapBear  = !UseVwap || Close[0] < currentVwap;

            bool volOk     = !UseVol  || Volume[0] > volSma[0];

            // Filtro Delta: solo se aplica si UseDelta=true Y cumDelta se pudo
            // instanciar (requiere subscription "Order Flow+"). Si cumDelta es
            // null, el filtro se omite gracefully y deltaBull/deltaBear = true.
            double deltaPct = 0;
            bool deltaBull = true, deltaBear = true;
            if (UseDelta && cumDelta != null)
            {
                double deltaBar = cumDelta.DeltaClose[0];
                if (Volume[0] > 0)
                    deltaPct = Math.Abs(deltaBar) / Volume[0] * 100.0;
                deltaBull = deltaBar > 0 && deltaPct >= DeltaPctMin;
                deltaBear = deltaBar < 0 && deltaPct >= DeltaPctMin;
            }

            // ---- 8. Detección de rompimientos (cruce desde la barra previa)
            bool breakPdh = UsePdhPdl       && !double.IsNaN(pdh) && Close[1] <= pdh && Close[0] > pdh;
            bool breakPdl = UsePdhPdl       && !double.IsNaN(pdl) && Close[1] >= pdl && Close[0] < pdl;
            bool breakOrH = UseOpeningRange && orFrozen && Close[1] <= orHigh && Close[0] > orHigh;
            bool breakOrL = UseOpeningRange && orFrozen && Close[1] >= orLow  && Close[0] < orLow;

            // ---- 8b. ARMADO: un cruce fresco arma la dirección por ArmBars velas.
            //          Guarda el nivel roto; un nuevo cruce re-arma (cubre el retest:
            //          si hace pullback bajo el nivel y vuelve a romper, se re-arma).
            if (breakPdh || breakOrH)
            {
                longArmedBars = ArmBars;
                longArmLevel  = breakPdh ? pdh : orHigh;   // PDH tiene prioridad si coinciden
            }
            if (breakPdl || breakOrL)
            {
                shortArmedBars = ArmBars;
                shortArmLevel  = breakPdl ? pdl : orLow;
            }

            // ---- 9. Señal LONG — armada, precio aún sobre el nivel roto y filtros OK.
            //          Permite entrar en la vela del cruce O en las siguientes ArmBars
            //          velas cuando EMA/VWAP por fin se alinean.
            if (longArmedBars > 0 && Close[0] > longArmLevel
                && emaBull && vwapBull && volOk && deltaBull
                && Position.MarketPosition == MarketPosition.Flat)
            {
                EnterBracket("Long_break", true, deltaPct);
                longArmedBars = 0;   // consumir el armado tras entrar
                return;
            }

            // ---- 10. Señal SHORT
            if (shortArmedBars > 0 && Close[0] < shortArmLevel
                && emaBear && vwapBear && volOk && deltaBear
                && Position.MarketPosition == MarketPosition.Flat)
            {
                EnterBracket("Short_break", false, deltaPct);
                shortArmedBars = 0;
                return;
            }

            // ---- 11. Caducar el armado (1 vela menos). Va al final para que la
            //          vela del cruce todavía pueda entrar arriba.
            if (longArmedBars  > 0) longArmedBars--;
            if (shortArmedBars > 0) shortArmedBars--;
        }

        // ----------------------------------------------------------
        // OnExecutionUpdate — trackeo de P&L diario / lifetime
        // ----------------------------------------------------------
        protected override void OnExecutionUpdate(Execution execution, string executionId,
                                                  double price, int quantity,
                                                  MarketPosition marketPosition,
                                                  string orderId, DateTime time)
        {
            if (execution.Order == null) return;
            if (execution.Order.OrderState != OrderState.Filled) return;

            // Cuando la posición vuelve a Flat = trade cerrado (interno o externo)
            if (Position.MarketPosition == MarketPosition.Flat
                && SystemPerformance.AllTrades.Count > 0)
            {
                // P&L del trade = delta del CumProfit desde el último cierre.
                // Captura el trade COMPLETO aunque el exit se haya llenado en
                // varios fills (que generan múltiples Trade records). Antes
                // leíamos solo AllTrades[last] y subcontábamos la pérdida.
                double cumNow   = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                double tradePnL = cumNow - lifetimeRealizedPnL;
                lifetimeRealizedPnL = cumNow;
                realizedPnLToday    = cumNow - sessionStartCumProfit;

                Print($"[{time:HH:mm}] Trade cerrado P&L=${tradePnL:F2}  día=${realizedPnLToday:F2}  total=${lifetimeRealizedPnL:F2}");

                // Detección robusta de cierre externo:
                // - Si la orden ejecutada tiene FromEntrySignal == Long_break / Short_break
                //   → es nuestro SL o TP del bracket OCO. Cierre normal.
                // - Si no, o si es una market order sin nuestro signal name (cierre
                //   manual del usuario, Emotional Manager, otra Strategy) → externo.
                // - PERO si el cierre ocurre FUERA de nuestra ventana de operación
                //   (antes de 8:30 o después de 12:00 CDMX), casi seguro es el
                //   IsExitOnSessionCloseStrategy del propio bot (cierra al session
                //   close del exchange ~15:00 CDMX) o un cierre manual fuera de
                //   horas. En esos casos NO marcamos session-lock porque ya no
                //   íbamos a operar más hoy de todos modos.
                string fromSignal = execution.Order.FromEntrySignal ?? string.Empty;
                bool isOurExit = fromSignal == "Long_break" || fromSignal == "Short_break";

                if (!isOurExit && !sessionLocked)
                {
                    DateTime tCdmxExec = ConvertChicagoToCdmx(time);
                    int hhmmExec = tCdmxExec.Hour * 100 + tCdmxExec.Minute;
                    bool insideSession = hhmmExec >= SessionStartCdmx && hhmmExec < SessionEndCdmx;

                    if (insideSession)
                    {
                        sessionLocked = true;
                        Print($"[{tCdmxExec:HH:mm}] Cierre EXTERNO detectado dentro de sesión (FromEntrySignal='{fromSignal}', OrderType={execution.Order.OrderType}) — bot SESSION-LOCKED hasta mañana.");
                    }
                    else
                    {
                        Print($"[{tCdmxExec:HH:mm}] Cierre fuera de ventana (FromEntrySignal='{fromSignal}', OrderType={execution.Order.OrderType}) — probable ExitOnSessionClose del exchange, no se aplica session-lock.");
                    }
                }
            }
        }

        // ===================== HELPERS =====================

        private void EnterBracket(string signalName, bool isLong, double deltaPct)
        {
            double entry  = Close[0];
            double slPts, tpPts;

            if (UseFixedDollarRisk)
            {
                // Convierte $ → puntos según el valor del punto del instrumento y
                // el nº de contratos. Para MNQ: PointValue=$2, 5 contratos → $10/pt.
                // SlUsd=300 → 30 pts ; TpUsd=500 → 50 pts. Riesgo por trade BLINDADO
                // al monto exacto, inmune al ATR (resuelve el stop gigante en días
                // de alta volatilidad).
                double dollarPerPoint = Instrument.MasterInstrument.PointValue * ContractsQty;
                slPts = SlUsd / dollarPerPoint;
                tpPts = TpUsd / dollarPerPoint;
            }
            else
            {
                slPts = atr[0] * SlAtr;
                tpPts = slPts * RrRatio;
            }

            double stop   = isLong ? entry - slPts : entry + slPts;
            double target = isLong ? entry + tpPts : entry - tpPts;

            // OpenRiskMode: sin SL ni TP por trade. La posición solo se cierra por
            // IsExitOnSessionCloseStrategy (fin de sesión) o por el Gestor Emocional
            // externo. Si está OFF, se colocan el stop y el target normales.
            if (!OpenRiskMode)
            {
                SetStopLoss(signalName, CalculationMode.Price, stop, false);
                SetProfitTarget(signalName, CalculationMode.Price, target);
            }

            if (isLong) EnterLong(ContractsQty, signalName);
            else        EnterShort(ContractsQty, signalName);

            tradesToday++;

            double riskUsd   = slPts * Instrument.MasterInstrument.PointValue * ContractsQty;
            double rewardUsd = tpPts * Instrument.MasterInstrument.PointValue * ContractsQty;

            DateTime tCdmx = ConvertChicagoToCdmx(Time[0]);
            string riskTxt = OpenRiskMode
                ? "SL=ABIERTO  TP=ABIERTO (cierra al fin de sesión)"
                : $"SL={stop:F2} (-${riskUsd:F0})  TP={target:F2} (+${rewardUsd:F0})";
            Print($"[{tCdmx:HH:mm}] {(isLong ? "LONG" : "SHORT")} @ {entry:F2}  {riskTxt}  ΔPct={deltaPct:F1}  trades hoy={tradesToday}");
        }

        private void LockDay(string reason)
        {
            if (sessionLocked) return;
            sessionLocked = true;
            DateTime tCdmx = ConvertChicagoToCdmx(Time[0]);
            Print($"[{tCdmx:HH:mm}] Día bloqueado: {reason}");
        }

        // P&L realizado acumulado (lifetime) según SystemPerformance. Devuelve 0
        // si aún no hay trades, para evitar excepciones al inicio de sesión.
        private double CumProfit()
        {
            return SystemPerformance.AllTrades.Count > 0
                   ? SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit
                   : 0.0;
        }

        private DateTime ConvertChicagoToCdmx(DateTime chicago)
        {
            // Time[0] viene en hora del exchange (Chicago).
            // En invierno CT == CDMX (ambos UTC-6).
            // En verano CT = UTC-5, CDMX = UTC-6 (México no hace DST desde 2022).
            // Conversión robusta vía UTC.
            try
            {
                DateTime utc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(chicago, DateTimeKind.Unspecified),
                    chicagoTz);
                return TimeZoneInfo.ConvertTimeFromUtc(utc, cdmxTz);
            }
            catch
            {
                // Fallback: si las TZ no existen en este Windows, usar Chicago tal cual
                return chicago;
            }
        }
    }
}
