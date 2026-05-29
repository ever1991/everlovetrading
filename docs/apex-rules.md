# Reglas Apex 50K (eval) — relevantes al bot

Esta hoja resume **solo** las reglas Apex que el bot necesita conocer para no
quemar la cuenta. No es la documentación oficial — siempre validar contra
[apextraderfunding.com](https://apextraderfunding.com) en caso de duda.

## Cuenta del usuario

- **Plan:** Apex 50K (evaluación, no PA)
- **Cuentas activas simultáneas:** 5+ (cuenta maestra + replicadas)
- **Replicación:** vía **NT8-to-NT8 Replicator de Bruno Meza** desde la maestra
  hacia las demás (esto significa que el riesgo agregado se multiplica por N).

## Métricas duras de la cuenta

| Métrica | Valor | Comentario |
|---|---|---|
| Profit Target | **$3,000** | Al llegar aquí, eval pasa a PA |
| Trailing Drawdown (TDD) | **$2,500** | Se mueve con el equity hasta lockear |
| Max contracts (NQ futuro) | **6** (regla 50% hasta lockear) | Después de lockear sube |
| Max contracts (MNQ micro) | **60** (regla 50%) | Después de lockear sube |
| Min trading days | 1 | Rápido en Apex |
| Daily loss limit Apex | ❌ no hay en eval | (pero el usuario tiene cap propio, ver abajo) |
| Consistency rule | ❌ no hay en eval | Empieza en PA |
| News trading | ✅ permitido en eval | |

## Reglas propias del usuario (guardrails externos)

| Guardrail | Valor | Quién lo aplica |
|---|---|---|
| Daily loss MAX por cuenta | **$1,000** | Emotional Manager de Bruno Meza |
| Cierre forzado al alcanzar daily loss | ✅ | Emotional Manager |
| Deshabilita el bot tras cierre forzado | ✅ | Emotional Manager |

## Lo que el bot debe respetar (defensa en profundidad)

El bot construido en este repo aplica guardrails **redundantes** al Emotional
Manager — si el gestor falla por cualquier motivo, el bot se autodetiene antes
de causar daño:

| Guardrail interno del bot | Valor | Razón |
|---|---|---|
| Position size | **5 MNQ** (= $10/pt) | Lejos del cap 60 MNQ, alineado a meta $150/día |
| Stop loss | **1 × ATR(14)** | Ajustado a volatilidad NQ |
| Take profit | **2 × ATR(14)** (RR = 2.0) | Asimetría positiva |
| Max trades por día | **5** | Después no toma más señales |
| Ventana de sesión | **8:30 – 12:00 CDMX** | Fuera de eso no opera ni reentra |
| Daily loss interno | **$300 / cuenta** | Más estricto que el $1,000 del gestor |
| Daily profit cap | **$300 / cuenta** | Apaga el día — evita devolver ganancias |
| Auto-disable al alcanzar profit target | **+$3,000 lifetime** | Señal para mover a PA |
| Re-entry tras cierre externo del gestor | **❌ bloqueado** ese día | Asume que el gestor sabe |

## Implicación de la replicación

Como el NT8-to-NT8 Replicator copia las órdenes de la cuenta maestra a las
otras N cuentas Apex, la P&L se **multiplica por N**:

- **+$150 en la maestra ⇒ +$150 × N en agregado** (esto sí escala muy bien)
- **–$300 en la maestra ⇒ –$300 × N en agregado** (el riesgo también escala)

Con 5 cuentas replicando, una racha de –$300 × 5 días = $7,500 quemado en agregado.
Por eso el bot tiene el daily loss interno en $300 y un cap de 5 trades/día — es
para no acumular pérdidas que la replicación amplifique.

**Recomendación operativa:** validar curva de equity en la cuenta maestra por 2-3
semanas antes de activar la replicación a las demás cuentas. Esto NO depende del
bot; depende de cómo configures el Replicator (puedes elegir cuántas cuentas
copia).
