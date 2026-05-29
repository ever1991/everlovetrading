# everlovetrading

Repositorio personal de trading: bots, indicadores, scripts y documentación de la operativa de **Everardo (`ever1991`)** en NQ/MNQ.

## Estructura

```
everlovetrading/
├── nt8/
│   ├── strategies/        # NinjaScript C# (.cs) — bots de ejecución automática
│   │   └── ScalpingNQMorning.cs   # ⏳ en construcción (port del Pine v4)
│   └── indicators/        # Indicadores custom NT8 si hacen falta
├── pine/
│   └── scalping-nq-morning-v4.pine   # Referencia: Pine Script original (TradingView)
├── docs/
│   ├── setup-nt8.md       # Cómo instalar el bot en NinjaTrader 8
│   ├── apex-rules.md      # Reglas de Apex que afectan al bot
│   └── trading-stack.md   # Mapa: bot + gestor emocional + bot Bruno Meza
└── README.md
```

## Estado

- 🟡 **Estrategia base**: scalping NQ sesión mañana CDMX (8:30–12:00) sobre rompimientos de PDH/PDL/Opening Range, con filtros EMA8/SMA20 + VWAP + volumen + delta orderflow ≥ 50%
- 🟡 **Port a NinjaScript**: en progreso, salida = Strategy lista para correr en NT8 con bracket OCO automático
- ⏳ **Cuenta destino**: Apex (fondeada)
- ⏳ **Co-existencia**: gestor emocional + bot de réplica de Bruno Meza (a documentar en `docs/trading-stack.md`)
