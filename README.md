# Thermalright Vision (.NET)

Ejecutable nativo (WinForms + WinUSB). Doble clic en `publish\ThermalrightVision.exe`.

## Modos

- `mode: auto` — sin media → estadísticas; con GIF/imagen → solo media
- `mode: stats` — siempre estadísticas
- `mode: media` — siempre media (si el archivo existe)

## Config (`config.json`)

```json
{
  "mode": "auto",
  "fps": 60,
  "jpegQuality": 78,
  "gif": "yay-riven.gif",
  "rotate": 180,
  "flipHorizontal": false,
  "flipVertical": false,
  "fit": "contain",
  "zoom": 1.0,
  "showClock": true,
  "showCpu": true,
  "showGpu": true,
  "showRam": true,
  "startHidden": false,
  "runAtLogin": false
}
```

- `fit`: `contain` (entero) o `cover` (llena y recorta)
- `zoom`: 0.1–5.0
- `rotate`: 0 / 90 / 180 / 270

## Bandeja

- Recargar config (sin reiniciar el USB)
- Abrir carpeta de config
- Iniciar / no iniciar con Windows
- Salir

Cierra TRCC antes de usar esta app.

## Publicar

```bat
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Requiere .NET 8 Desktop Runtime.
