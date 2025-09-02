# PowerShell script to create a blue water texture

$dir = "Textures\Things\Item"

# Create directory if it doesn't exist
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force
    Write-Host "Created directory: $dir"
}

# Create a blue water texture 
$texturePath = "$dir\FlowingWater.png"
Write-Host "Creating blue water texture: $texturePath"

# Using .NET to create a bitmap
Add-Type -AssemblyName System.Drawing

# Create a 64x64 bitmap with blue color
$width = 64
$height = 64
$bitmap = New-Object System.Drawing.Bitmap $width, $height

# Fill with blue gradient (to show water better)
for ($y = 0; $y -lt $height; $y++) {
    for ($x = 0; $x -lt $width; $x++) {
        # Calculate distance from center (0-1 range)
        $centerX = $width / 2
        $centerY = $height / 2
        $dx = ($x - $centerX) / $centerX
        $dy = ($y - $centerY) / $centerY
        $distanceFromCenter = [Math]::Sqrt($dx * $dx + $dy * $dy)
        
        # Create a radial gradient effect
        $intensity = [Math]::Max(0, 1 - $distanceFromCenter)
        
        # Blue water with white center
        $red = [int]([Math]::Min(255, 40 + (215 * $intensity * $intensity)))
        $green = [int]([Math]::Min(255, 120 + (135 * $intensity * $intensity)))
        $blue = [int]([Math]::Min(255, 200 + (55 * $intensity)))
        
        $color = [System.Drawing.Color]::FromArgb(255, $red, $green, $blue)
        $bitmap.SetPixel($x, $y, $color)
    }
}

# Save the bitmap
$bitmap.Save($texturePath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Host "Texture created successfully."
