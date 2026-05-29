# Setup en NinjaTrader 8 — paso a paso

Cómo instalar y configurar `ScalpingNQMorning.cs` en la PC Windows donde corre NT8.

## Prerrequisitos

- **NinjaTrader 8.1+** (necesario para `OrderFlowCumulativeDelta` nativo)
- **Subscription a Order Flow / Volumetric Bars** (incluida en Apex con feed Rithmic)
- **Tick Replay habilitado** (lo activamos abajo)
- **GitHub Desktop** instalado y vinculado a la cuenta `ever1991`
- Visual Studio Code u otro editor (opcional, sólo si vas a tocar el `.cs`)

---

## Paso 1 — Clonar el repo en la PC

1. Abre **GitHub Desktop**
2. File → Clone repository
3. URL: `https://github.com/ever1991/everlovetrading.git`
4. Local path sugerido: `C:\Users\<tu_usuario>\Documents\GitHub\everlovetrading`

Esto te trae el código a tu PC. Cuando yo (Claude) actualice algo desde la Mac y haga push, tú solo haces **"Fetch origin" → "Pull"** en GitHub Desktop para tenerlo al día.

## Paso 2 — Copiar la Strategy a la carpeta de NinjaTrader

NT8 lee Strategies desde una carpeta fija. Hay dos formas:

### Forma A (recomendada — symlink, copia automática)

Abre **PowerShell como administrador** y corre:
```powershell
New-Item -ItemType SymbolicLink `
  -Path "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\ScalpingNQMorning.cs" `
  -Target "$env:USERPROFILE\Documents\GitHub\everlovetrading\nt8\strategies\ScalpingNQMorning.cs"
```

Con esto, cada vez que pulleas cambios desde GitHub Desktop, NT8 ve el archivo actualizado automáticamente. No tienes que copiar nada a mano.

### Forma B (copia manual cada vez)

Si no quieres usar symlink, simplemente copia el archivo:
- Desde: `Documents\GitHub\everlovetrading\nt8\strategies\ScalpingNQMorning.cs`
- A:     `Documents\NinjaTrader 8\bin\Custom\Strategies\ScalpingNQMorning.cs`

Cada vez que actualicemos el `.cs`, repites la copia.

## Paso 3 — Compilar en NT8

1. En NT8: **New** → **NinjaScript Editor**
2. En el panel izquierdo, navega a **Strategies** → verás `ScalpingNQMorning.cs`
3. Presiona **F5** (o el botón Compile)
4. Si compila ✓: aparece `Compile successful` en la barra de status
5. Si hay errores: aparecen en el panel inferior **Errors**

**Si falla**, copia el texto completo del primer error y me lo pegas en el chat. Yo corrijo el código, hago push, y tú pulleas + recompilas.

## Paso 4 — Activar Tick Replay en la connection

1. **Tools → Options → Market data**
2. Marca **"Show realtime updates as: Tick replay"** (o equivalente según versión)
3. En tu connection (Rithmic/Apex), edita propiedades y activa **"Use bar magnifier on historical replay"** y **"Tick Replay"**

Sin esto, el `OrderFlowCumulativeDelta` no calculará el delta histórico correctamente en backtest.

## Paso 5 — Agregar la Strategy a un chart

1. Abre un chart de **NQ 03-26** (o **MNQ 03-26**), timeframe **5 min**
2. Click derecho en el chart → **Strategies...**
3. En el panel, click **Add** → busca `ScalpingNQMorning` → **OK**
4. Configura los inputs (los defaults ya están alineados al Pine v4):
   - **Account**: `Sim101` (para sim) o tu cuenta Apex maestra
   - **Start behavior**: `Wait until flat`
   - **Calculate**: `On bar close`
   - **Order quantity**: dejar en `Strategy` (el bot decide el size desde su input)
5. Click **Enable** → la Strategy queda corriendo

## Paso 6 — Validar en simulador antes de cuenta real

**Importantísimo:** antes de cargar el bot en cuenta Apex real, hazlo:

1. Cambia **Account** a `Sim101` (o `Playback`)
2. Deja correr una sesión completa (8:30 – 12:00 CDMX)
3. Revisa en el **Output** (Tools → Output) los logs:
   - `[HH:mm] Nuevo día — PDH=... PDL=... PDC=...`
   - `[HH:mm] OR fijado — High=... Low=...`
   - `[HH:mm] LONG @ ...` o `[HH:mm] SHORT @ ...` cuando dispare una señal
4. Comprueba que las órdenes se ven en el **Trades** panel y los brackets están bien (SL y TP correctos)

Si todo OK durante 1 semana de sim, pasamos a cuenta real (maestra Apex 50K) por 1 semana más, y después activamos la replicación.

---

## Loop de iteración (entre tú y yo)

Cuando algo no funcione bien:

1. Tú me pegas el error / screenshot / log raro en este chat
2. Yo (Claude) corrijo en la Mac, commit + push al repo
3. Tú haces **Fetch origin → Pull** en GitHub Desktop
4. Si usas symlink (Forma A del paso 2): no copies nada, NT8 ya ve el archivo
5. Si usas copia manual (Forma B): vuelves a copiar el `.cs`
6. En NinjaScript Editor → **F5** para recompilar
7. Vuelves a probar

---

## Checklist rápido

- [ ] GitHub Desktop instalado y vinculado a `ever1991`
- [ ] Repo `everlovetrading` clonado a `Documents\GitHub\everlovetrading`
- [ ] `ScalpingNQMorning.cs` puesto en `Documents\NinjaTrader 8\bin\Custom\Strategies\` (symlink o copia)
- [ ] **F5** compila sin errores
- [ ] Tick Replay habilitado en la connection
- [ ] Strategy agregada al chart NQ/MNQ 5m con cuenta `Sim101`
- [ ] Logs aparecen en Output al cierre de barras dentro de la ventana 8:30-12:00 CDMX
- [ ] Al menos 3 sesiones de sim ejecutadas correctamente antes de pasar a real
