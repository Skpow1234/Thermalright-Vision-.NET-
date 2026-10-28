# Thermalright Vision (.NET)

Cliente **nativo** (WinForms + WinUSB) para la pantalla del AIO Wonder Vision 360.  
Sin Electron, sin TRCC, sin Chromium.

Doble clic: `publish\ThermalrightVision.exe`

## Peso y objetivo

| | Aprox. |
|--|--|
| `.exe` (framework-dependent) | **~190 KB** |
| RAM en uso (media GIF) | **~20–70 MB** privados |
| Antes (GIF precargado + self-contained) | ~330 MB+ |

**Objetivo del proyecto:** mantenerlo lo más liviano posible. Cada cambio debe medirse en RAM/CPU. Preferir decode al vuelo, buffers reutilizados y runtime compartido de .NET 8 Desktop.

Requiere [**.NET 8 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/8.0).

Cierra **TRCC** antes de abrir esta app (usan el mismo USB).

---

## Cómo modificar la config

1. Abre la carpeta `publish\` (o clic derecho en la bandeja → **Abrir carpeta de config**).
2. Edita `config.json` con el Bloc de notas, VS Code, etc.
3. Guarda el archivo.
4. Si `watchConfig` es `true`, la app recarga sola en ~0,3 s. Si no, bandeja → **Recargar config**.

Reglas del JSON:

- Usa comillas dobles en claves y textos: `"gif": "foto.gif"`.
- Booleanos: `true` / `false` (sin comillas).
- Números sin comillas: `"fps": 30`.
- No dejes una coma después del último campo.
- Rutas relativas parten de la carpeta del `.exe` (`publish\`).

Ejemplo mínimo fluido (GIF continuo):

```json
{
  "mode": "auto",
  "fps": 30,
  "fpsIdle": 30,
  "jpegQuality": 70,
  "gif": "yay-riven.gif",
  "rotate": 180,
  "fit": "contain",
  "zoom": 1.0
}
```

---

## Todos los parámetros

### Modo y contenido

| Parámetro | Tipo | Default | Qué hace |
|-----------|------|---------|----------|
| `mode` | texto | `"auto"` | `auto` = sin archivo de media → estadísticas; con GIF/imagen → solo media. `stats` = siempre estadísticas. `media` = siempre media (si el archivo existe). |
| `gif` | texto | `""` | Ruta a GIF o imagen (PNG/JPG). Relativa al `.exe` o absoluta. Vacío = no hay media. |
| `media` | texto | `""` | Alias de `gif`. Se usa si `gif` está vacío. |

### Rendimiento / fluidez

| Parámetro | Tipo | Default | Qué hace |
|-----------|------|---------|----------|
| `fps` | número | `15` | Tope de fotogramas por segundo cuando el contenido cambia. Para fluido: `30` (o `60` si el USB aguanta). |
| `fpsIdle` | número | `2` | FPS cuando el fotograma del GIF **no** ha cambiado. Si está bajo, la LCD se ve a trompicones. Para fluido: ponlo **igual que `fps`** (ej. `30`). `0` = no reenviar hasta el siguiente frame del GIF. |
| `jpegQuality` | número | `78` | Calidad JPEG 40–95. Más bajo = menos CPU/USB y más fluido; más alto = más nitidez. |

### Orientación y encaje

| Parámetro | Tipo | Default | Qué hace |
|-----------|------|---------|----------|
| `rotate` | número | `0` | Rotación: `0`, `90`, `180`, `270`. Si el GIF sale al revés, usa `180`. |
| `flipHorizontal` | bool | `false` | Espejo horizontal. |
| `flipVertical` | bool | `false` | Espejo vertical. |
| `fit` | texto | `"contain"` | `contain` = cabe entero (puede dejar bordes). `cover` = llena la pantalla y recorta (se ve más “ampliado”). |
| `zoom` | número | `1.0` | Multiplicador 0.1–5.0. `0.85` = más pequeño; `1.2` = más cerca. |
| `letterboxColor` | texto | `"#000000"` | Color de los bordes en `contain`. Formato `#RRGGBB` o `r,g,b` (ej. `"16,20,32"`). |

### Estadísticas (solo en modo stats / auto sin media)

| Parámetro | Tipo | Default | Qué hace |
|-----------|------|---------|----------|
| `showClock` | bool | `true` | Muestra el reloj. |
| `showCpu` | bool | `true` | Tarjeta CPU (carga %, GHz; °C si HWiNFO Shared Memory está activo). |
| `showGpu` | bool | `true` | Tarjeta GPU (temp borde / hotspot / uso). |
| `showRam` | bool | `true` | Tarjeta RAM. |

Temp CPU: abre **HWiNFO** → Settings → activa **Shared Memory Support**.

### Arranque y panel

| Parámetro | Tipo | Default | Qué hace |
|-----------|------|---------|----------|
| `startHidden` | bool | `false` | Reserva; la app ya corre en bandeja. |
| `runAtLogin` | bool | `false` | Si `true` al cargar config, registra inicio con Windows. También se cambia desde la bandeja. |
| `watchConfig` | bool | `true` | Recarga automática al guardar `config.json`. |
| `width` | número | *(omitir)* | Fuerza ancho del framebuffer. Sin esto usa el del handshake (esta unidad: 1600). |
| `height` | número | *(omitir)* | Fuerza alto (esta unidad: 720). Solo si sabes lo que haces. |

---

## Bandeja del sistema

- **Estado** — PM/SUB y resolución detectada  
- **Recargar config** — aplica `config.json` sin reiniciar el USB  
- **Abrir carpeta de config** — abre `publish\`  
- **Iniciar / no iniciar con Windows**  
- **Salir**

---

## Publicar de nuevo

```bat
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Tras publicar, vuelve a copiar o revisar tu `publish\config.json` (el publish puede sobrescribirlo con el del proyecto).
