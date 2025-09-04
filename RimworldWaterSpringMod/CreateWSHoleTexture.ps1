param(
  [int]$Size = 64
)

$ErrorActionPreference = 'Stop'
$dir = 'f:\.Net\Mods\WaterPhysics\RimworldWaterSpringMod\Textures\Things\Building'
if (-not (Test-Path -LiteralPath $dir)) {
  New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
$pngPath = Join-Path $dir 'WS_Hole.png'

# Generate a fully transparent PNG so the underlying tile shows through.
Add-Type -AssemblyName System.Drawing
# Use constructor without PixelFormat to avoid enum conversion issues; defaults to 32bpp ARGB on Windows
$bitmap = New-Object System.Drawing.Bitmap ($Size, $Size)
$gfx = [System.Drawing.Graphics]::FromImage($bitmap)
$gfx.Clear([System.Drawing.Color]::Transparent)
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose()
$bitmap.Dispose()

Write-Host "Wrote transparent texture:" $pngPath
Get-Item -LiteralPath $pngPath | Format-List -Property FullName,Length,LastWriteTime
