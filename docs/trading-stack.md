# Trading Stack — bot + Emotional Manager + Replicator

Cómo conviven los 3 componentes dentro de NinjaTrader 8.

## Diagrama

```
   ┌─────────────────────────────────────────┐
   │   ScalpingNQMorning.cs   (este repo)    │ ← Strategy NinjaScript
   │   Decide cuándo entrar/salir            │   Cargada en chart NQ1! 5m
   └───────────────┬─────────────────────────┘
                   │ EnterLong / EnterShort + bracket OCO
                   ▼
   ┌─────────────────────────────────────────┐
   │   Emotional Manager (Bruno Meza)        │ ← Pre-trade filter + risk
   │   Valida daily loss $1,000/cuenta       │   AddOn / Strategy NT8
   │   Cierra y bloquea bot si lo alcanza    │
   └───────────────┬─────────────────────────┘
                   │ Approve or block
                   ▼
   ┌─────────────────────────────────────────┐
   │   Cuenta MAESTRA Apex 50K (#1)          │ ← Connection: Rithmic/Apex feed
   └───────────────┬─────────────────────────┘
                   │ Fill confirmation
                   ▼
   ┌─────────────────────────────────────────┐
   │   NT8-to-NT8 Replicator (Bruno Meza)    │ ← Copia órdenes a cuentas slave
   └──┬──────┬──────┬──────┬──────┬──────────┘
      ▼      ▼      ▼      ▼      ▼
   Cuenta Cuenta Cuenta Cuenta Cuenta
   Apex#2 Apex#3 Apex#4 Apex#5 Apex#N
   50K    50K    50K    50K    50K
```

## División de responsabilidades

| Componente | Owner | Qué hace | Qué NO hace |
|---|---|---|---|
| **ScalpingNQMorning.cs** | Este repo (Claude + Ever) | Decide entrada/salida según Pine v4: PDH/PDL/OR + EMA8/SMA20 + VWAP + vol + delta% ≥ 50; emite orden bracket en la cuenta maestra | NO gestiona daily loss (delega a EM); NO replica a otras cuentas (delega a Replicator); NO opera fuera de 8:30-12:00 CDMX |
| **Emotional Manager** | Bruno Meza (comprado) | Trackea P&L diaria, cierra todo al llegar al cap (\$1,000), bloquea Strategies tras cierre, valida pre-trade | NO genera señales propias |
| **NT8-to-NT8 Replicator** | Bruno Meza (comprado) | Lee fills en cuenta maestra → emite la misma orden en cuentas slave configuradas | NO modifica órdenes; NO arbitra; NO compensa por slippage |

## Configuración recomendada en NT8

### En la cuenta maestra (donde corre el bot)
1. Chart **NQ 03-26** (front month) o `MNQ 03-26`, timeframe **5 min** o **15 min**
2. Activar **Tick Replay** en la connection settings (necesario para que el delta histórico se calcule en backtest correctamente)
3. Cargar `ScalpingNQMorning` Strategy en el chart (`Strategies` panel → Add → ScalpingNQMorning)
4. En el panel del Strategy:
   - **Account**: cuenta maestra Apex
   - **Start behavior**: `Wait until flat`
   - **Calculate**: `On bar close`
   - **Inputs**: ver `nt8/strategies/ScalpingNQMorning.cs` para valores recomendados
5. **Enable** la Strategy. Queda corriendo hasta apagado manual o por Emotional Manager.

### Emotional Manager
- Debe estar configurado **ANTES** que la Strategy del bot
- Daily loss cap por cuenta: $1,000 (ya está, según user)
- Disable bot al alcanzar cap: ✅ (ya está)
- Aplicar a la cuenta maestra **y** a las cuentas replicadas (cada una con su propio cap, no agregado)

### NT8-to-NT8 Replicator
- Master: cuenta Apex #1
- Slaves: cuentas Apex #2–#N
- **Recomendación de arranque:** copiar solo a 1 cuenta slave los primeros 5–10 días. Si la curva es positiva consistente, escalar a 2-3. Cuando lleves 2-3 semanas verde, ya activa todas.

## Qué pasa si el Emotional Manager apaga el bot a mitad de sesión

El bot está diseñado defensivamente:

1. **Detección de cierre externo**: el bot revisa `Position.MarketPosition` cada barra. Si esperaba estar en Long/Short y aparece `Flat`, asume cierre externo.
2. **Session lock**: una vez detectado el cierre externo, el bot entra en `sessionLocked = true` y **no genera más señales hasta el día siguiente**.
3. **Estado persistente**: al apagar/encender NT8 a media sesión, el bot revisa fecha del último cierre forzado y respeta el lock si sigue dentro del mismo día.

No hay forma de que el bot "ignore" al Emotional Manager y reentre.

## Qué pasa con la replicación cuando el bot está apagado

Si el bot está apagado en la maestra, no hay órdenes que replicar. El Replicator
queda inactivo. No replica posiciones manuales que abras tú a mano, salvo que
así esté configurado.

## Próximos pasos

Una vez compilado y validado el bot en sim:

1. Activar bot en cuenta maestra (sim) por 1 semana
2. Si OK → activar en cuenta maestra (real Apex)
3. Si OK → activar Replicator a 1 cuenta slave
4. Si OK → escalar a más cuentas slave gradualmente
