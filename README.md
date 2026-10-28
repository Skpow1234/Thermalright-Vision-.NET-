# Thermalright Vision (.NET)

Cliente **nativo** (WinForms + WinUSB) para la pantalla del AIO Wonder Vision 360.  
Sin Electron, sin TRCC, sin Chromium.

Doble clic: `publish\ThermalrightVision.exe`

## Peso y objetivo

| | Aprox. |
|--|--|
| `.exe` (framework-dependent) | **~190 KB** |
| RAM en uso (media GIF) | **~30–70 MB** privados |
| Antes (GIF precargado + self-contained) | ~330 MB+ |

**Objetivo:** mantenerlo lo más liviano posible. Features nuevas deben justificarse en RAM/CPU.

Requiere [**.NET 8 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/8.0).

## Modos

- `mode: auto` — sin media → estadísticas; con GIF/imagen → solo media
- `mode: stats` / `mode: media`

## Config (`config.json`)

```json
{
  "mode": "auto",
  "fps": 60,
  "fpsIdle": 2,
  "jpegQuality": 78,
  "gif": "yay-riven.gif",
  "rotate": 180,
  "fit": "contain",
  "zoom": 1.0,
  "letterboxColor": "#000000",
  "showClock": true,
  "showCpu": true,
  "showGpu": true,
  "showRam": true,
  "watchConfig": true,
  "runAtLogin": false
}
```

- `fpsIdle`: FPS cuando el fotograma del GIF no cambia (`0` = esperar al siguiente frame)
- `letterboxColor`: bordes en `contain` (`#RRGGBB` o `r,g,b`)
- `watchConfig`: recarga al guardar el JSON
- Temp CPU: si HWiNFO está abierto con **Shared Memory Support**, aparece en la tarjeta CPU

Cierra **TRCC** antes de usar esta app.

## Bandeja

- Recargar config / Abrir carpeta / Inicio con Windows / Salir

## Publicar

```bat
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```
