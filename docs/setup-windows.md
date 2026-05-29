# Setup Windows desde cero (compu de trading)

Guía para una PC Windows con casi nada instalado, que solo va a correr NinjaTrader 8 + Git + GitHub Desktop. Sin software extra que coma latencia.

## Antivirus — recomendación directa

**Quédate con Windows Defender** (incluido en Windows 10/11).

**No instales antivirus de terceros** (Norton/McAfee/Avast/Kaspersky) en una compu de trading. Razones:

1. Comen RAM y CPU constantemente → latencia añadida a NT8
2. Escanean archivos abiertos en tiempo real → leen los tick logs que NT8 escribe y meten micro-pausas
3. Algunos bloquean conexiones SSL que NT8 necesita para Rithmic / Apex Trader Funding

Windows Defender es suficiente. Lo configuramos para excluir las carpetas de NT8 y Git, eso baja latencia y evita falsos positivos.

---

## Setup paso a paso

Cada paso es de 2 a 5 minutos. En orden de dependencia.

### Paso 1 — Actualizar Windows y reiniciar

- Barra de búsqueda → **"Windows Update"** → **"Buscar actualizaciones"** → aplicar todo → reiniciar.

Esto resuelve la mayoría de problemas raros antes de empezar.

### Paso 2 — Plan de energía para trading

1. Barra de búsqueda → **"Editar plan de energía"**
2. **"Opciones de energía"** → elegir **"Alto rendimiento"** (si no aparece, click "Mostrar planes adicionales")
3. Si tu equipo tiene Windows Pro/Enterprise puedes activar **Ultimate Performance**:
   - PowerShell como admin → `powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61` → seleccionar "Ultimate Performance" en el panel

Esto evita que el CPU baje de frecuencia justo cuando entra un fill.

### Paso 3 — Configurar exclusiones de Windows Defender

PowerShell como administrador (clic derecho en Inicio → **"Terminal (admin)"** o **"Windows PowerShell (admin)"**):

```powershell
Add-MpPreference -ExclusionPath "$env:USERPROFILE\Documents\NinjaTrader 8"
Add-MpPreference -ExclusionPath "$env:USERPROFILE\Documents\GitHub"
Add-MpPreference -ExclusionPath "C:\Program Files (x86)\NinjaTrader 8"
Add-MpPreference -ExclusionProcess "NinjaTrader.exe"
```

Verificar:
```powershell
Get-MpPreference | Select-Object -ExpandProperty ExclusionPath
```

### Paso 4 — Instalar Git para Windows

1. Descarga: <https://git-scm.com/download/win>
2. Doble click al `.exe` → siguiente, siguiente con todos los defaults
3. Verifica abriendo **PowerShell** (no necesita ser admin):
   ```powershell
   git --version
   ```
   Debe responder algo tipo `git version 2.45.x`.

### Paso 5 — Instalar GitHub Desktop

1. Descarga: <https://desktop.github.com/>
2. Doble click al `.exe` → se instala solo
3. Al abrir: **"Sign in to GitHub.com"**
4. Te lleva al navegador → entra con tu cuenta `ever1991` + 2FA → autoriza
5. Cuando vuelva a Desktop, configura:
   - **Name**: ever1991
   - **Email**: everardopalmero@gmail.com (mismo que tu cuenta)

### Paso 6 — Clonar el repo

En GitHub Desktop:

1. **File → Clone repository**
2. Pestaña **"URL"** → pega: `https://github.com/ever1991/everlovetrading.git`
3. **Local path**: déjalo en `C:\Users\<tuusuario>\Documents\GitHub\everlovetrading`
4. **Clone**

Listo, tienes todo el repo local.

### Paso 7 — Symlink para que NT8 lea el bot desde el repo

Clave para que cada `pull` actualice automáticamente lo que NT8 compila. Sin symlink tendrías que copiar el `.cs` a mano cada vez que cambiemos algo.

PowerShell como administrador:

```powershell
$source = "$env:USERPROFILE\Documents\GitHub\everlovetrading\nt8\strategies\ScalpingNQMorning.cs"
$target = "$env:USERPROFILE\Documents\NinjaTrader 8\bin\Custom\Strategies\ScalpingNQMorning.cs"

# Si ya existe algo ahí, borrarlo primero
if (Test-Path $target) { Remove-Item $target }

# Crear el enlace simbólico
New-Item -ItemType SymbolicLink -Path $target -Target $source
```

Verifica:
```powershell
Get-Item $target | Select-Object Name,LinkType,Target
```

Debe mostrar `LinkType = SymbolicLink` y el `Target` apuntando al repo.

### Paso 8 — Compilar en NT8

1. Abre NinjaTrader 8
2. **New → NinjaScript Editor**
3. En el panel izquierdo expandes **Strategies** → ves `ScalpingNQMorning.cs`
4. Click sobre él para abrirlo
5. **F5** o botón **Compile** arriba
6. Mira el panel inferior **Errors**:
   - Si dice `Compile successful` → todo OK
   - Si tiene errores → copia el texto del primer error y pásamelo en la próxima sesión

### Paso 9 — Activar Tick Replay (necesario para el delta)

1. **Tools → Options → Market data**
2. Marca **"Show realtime updates as: Tick replay"** (o equivalente según versión)
3. Si tu connection (Rithmic / Apex) tiene un toggle **"Use bar magnifier on historical replay"** → actívalo

### Paso 10 — Probar en Sim101

1. Abre un chart NQ (front month, actualmente `NQ 03-26`) en 5 min
2. Click derecho → **Strategies...**
3. **Add** → busca `ScalpingNQMorning` → OK
4. En el panel del Strategy:
   - **Account**: `Sim101`
   - **Start behavior**: `Wait until flat`
   - **Calculate**: `On bar close`
5. **Enable** ✓

Abre **Tools → Output** para ver los logs. Cuando entre la hora 8:30 CDMX debe loguear:
```
[YYYY-MM-DD] Nuevo día — PDH=... PDL=... PDC=...
```

Y conforme avancen las barras, cada cierre dentro de la ventana 8:30-12:00 CDMX puede disparar señales o no según los filtros.

---

## Checklist final

- [ ] Windows actualizado y reiniciado
- [ ] Plan de energía en Alto rendimiento (o Ultimate Performance)
- [ ] Defender con exclusiones para NT8 y GitHub
- [ ] Git for Windows instalado (`git --version` responde)
- [ ] GitHub Desktop instalado y vinculado a `ever1991`
- [ ] Repo `everlovetrading` clonado a `Documents\GitHub\everlovetrading`
- [ ] Symlink creado: NT8 ve el `.cs` del repo
- [ ] F5 compila sin errores
- [ ] Tick Replay activado
- [ ] Strategy agregada al chart NQ 5m con Sim101

---

## Loop de iteración después del setup

Cuando algo no funcione bien en NT8:

1. Le pegas el error / screenshot / log raro a Claude en el chat
2. Claude corrige en la Mac, hace commit + push al repo
3. Tú haces **"Fetch origin" → "Pull"** en GitHub Desktop
4. Si tienes symlink (paso 7): no copies nada, NT8 ya ve el archivo actualizado
5. En NinjaScript Editor → **F5** para recompilar
6. Pruebas otra vez en Sim101
