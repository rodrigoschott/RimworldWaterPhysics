Add-Type -AssemblyName System.Drawing

# Get the current script directory
$scriptDir = $PSScriptRoot
Write-Host "Script directory: $scriptDir"

# Function to create a simple texture
function Create-Texture {
    param(
        [string]$FilePath,
        [int]$Width = 128,
        [int]$Height = 128,
        [System.Drawing.Color]$BackgroundColor,
        [System.Drawing.Color]$BorderColor,
        [string]$Symbol = $null,
        [System.Drawing.Color]$SymbolColor = [System.Drawing.Color]::White,
        [int]$BorderThickness = 3
    )

    # Create a new bitmap
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Fill the background
    $graphics.Clear($BackgroundColor)

    # Draw a border
    if ($BorderThickness -gt 0) {
        $pen = New-Object System.Drawing.Pen($BorderColor, $BorderThickness)
        $graphics.DrawRectangle($pen, $BorderThickness/2, $BorderThickness/2, $Width - $BorderThickness, $Height - $BorderThickness)
        $pen.Dispose()
    }

    # Add a symbol if provided
    if ($Symbol) {
        $font = New-Object System.Drawing.Font("Arial", 48, [System.Drawing.FontStyle]::Bold)
        $textBrush = New-Object System.Drawing.SolidBrush($SymbolColor)
        $stringFormat = New-Object System.Drawing.StringFormat
        $stringFormat.Alignment = [System.Drawing.StringAlignment]::Center
        $stringFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString($Symbol, $font, $textBrush, [System.Drawing.RectangleF]::new(0, 0, $Width, $Height), $stringFormat)
        $font.Dispose()
        $textBrush.Dispose()
        $stringFormat.Dispose()
    }

    # Save the bitmap as PNG
    try {
        $dirName = [System.IO.Path]::GetDirectoryName($FilePath)
        if (-not (Test-Path $dirName)) {
            New-Item -ItemType Directory -Path $dirName -Force | Out-Null
            Write-Host "Created directory: $dirName"
        }
        $bitmap.Save($FilePath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Created texture at $FilePath"
    }
    catch {
        Write-Host "Error saving texture: $_"
    }

    # Clean up
    $graphics.Dispose()
    $bitmap.Dispose()
}

# Function to create a water flowing texture with wave pattern
function Create-WaterTexture {
    param(
        [string]$FilePath,
        [int]$Width = 128,
        [int]$Height = 128,
        [System.Drawing.Color]$WaterColor
    )

    # Create a new bitmap
    $bitmap = New-Object System.Drawing.Bitmap($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Fill the background with base water color
    $graphics.Clear($WaterColor)

    # Create wave patterns
    $lighterColor = [System.Drawing.Color]::FromArgb(
        [Math]::Min(255, $WaterColor.R + 30),
        [Math]::Min(255, $WaterColor.G + 30),
        [Math]::Min(255, $WaterColor.B + 30)
    )
    $darkerColor = [System.Drawing.Color]::FromArgb(
        [Math]::Max(0, $WaterColor.R - 30),
        [Math]::Max(0, $WaterColor.G - 30),
        [Math]::Max(0, $WaterColor.B - 30)
    )

    $lighterBrush = New-Object System.Drawing.SolidBrush($lighterColor)
    $darkerBrush = New-Object System.Drawing.SolidBrush($darkerColor)

    # Draw some random wave patterns
    $rand = New-Object Random
    
    # Draw lighter waves
    for ($i = 0; $i -lt 5; $i++) {
        $x1 = $rand.Next(0, $Width)
        $y1 = $rand.Next(0, $Height)
        $x2 = $rand.Next(10, 30)
        $y2 = $rand.Next(3, 10)
        
        $graphics.FillEllipse($lighterBrush, $x1, $y1, $x2, $y2)
    }
    
    # Draw darker waves
    for ($i = 0; $i -lt 5; $i++) {
        $x1 = $rand.Next(0, $Width)
        $y1 = $rand.Next(0, $Height)
        $x2 = $rand.Next(10, 30)
        $y2 = $rand.Next(3, 10)
        
        $graphics.FillEllipse($darkerBrush, $x1, $y1, $x2, $y2)
    }

    # Draw a slightly transparent border
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(100, 255, 255, 255), 2)
    $graphics.DrawRectangle($borderPen, 1, 1, $Width-2, $Height-2)

    # Save the bitmap as PNG
    try {
        $dirName = [System.IO.Path]::GetDirectoryName($FilePath)
        if (-not (Test-Path $dirName)) {
            New-Item -ItemType Directory -Path $dirName -Force | Out-Null
            Write-Host "Created directory: $dirName"
        }
        $bitmap.Save($FilePath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Created water texture at $FilePath"
    }
    catch {
        Write-Host "Error saving texture: $_"
    }

    # Clean up
    $graphics.Dispose()
    $bitmap.Dispose()
    $lighterBrush.Dispose()
    $darkerBrush.Dispose()
    $borderPen.Dispose()
}

# Create directories if they don't exist
$buildingTexturePath = Join-Path -Path $scriptDir -ChildPath "Textures\Things\Building"
$itemTexturePath = Join-Path -Path $scriptDir -ChildPath "Textures\Things\Item"

if (-not (Test-Path $buildingTexturePath)) {
    New-Item -ItemType Directory -Path $buildingTexturePath -Force | Out-Null
    Write-Host "Created directory: $buildingTexturePath"
}

if (-not (Test-Path $itemTexturePath)) {
    New-Item -ItemType Directory -Path $itemTexturePath -Force | Out-Null
    Write-Host "Created directory: $itemTexturePath"
}

# Create Water Spring building texture (blue well-like structure)
$waterSpringTexturePath = Join-Path -Path $buildingTexturePath -ChildPath "WaterSpring.png"
$waterSpringColor = [System.Drawing.Color]::FromArgb(80, 120, 200)
$waterSpringBorderColor = [System.Drawing.Color]::FromArgb(50, 70, 120)
Create-Texture -FilePath $waterSpringTexturePath -BackgroundColor $waterSpringColor -BorderColor $waterSpringBorderColor -Symbol "⛲" -SymbolColor ([System.Drawing.Color]::White)

# Create Flowing Water item texture (wavy blue pattern)
$flowingWaterTexturePath = Join-Path -Path $itemTexturePath -ChildPath "FlowingWater.png"
$flowingWaterColor = [System.Drawing.Color]::FromArgb(64, 156, 255)
Create-WaterTexture -FilePath $flowingWaterTexturePath -WaterColor $flowingWaterColor

Write-Host "Texture generation complete!"
