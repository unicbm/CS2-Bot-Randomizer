param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\Panel\app-icon.png")
)

Add-Type -AssemblyName System.Drawing

$size = 1024
$bitmap = [System.Drawing.Bitmap]::new($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::Transparent)

$path = [System.Drawing.Drawing2D.GraphicsPath]::new()
$radius = 220
$diameter = $radius * 2
$path.AddArc(32, 32, $diameter, $diameter, 180, 90)
$path.AddArc($size - $diameter - 32, 32, $diameter, $diameter, 270, 90)
$path.AddArc($size - $diameter - 32, $size - $diameter - 32, $diameter, $diameter, 0, 90)
$path.AddArc(32, $size - $diameter - 32, $diameter, $diameter, 90, 90)
$path.CloseFigure()

$gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(180, 80),
    [System.Drawing.Point]::new(850, 940),
    [System.Drawing.Color]::FromArgb(255, 30, 147, 255),
    [System.Drawing.Color]::FromArgb(255, 1, 62, 125)
)
$graphics.FillPath($gradient, $path)

$innerPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(125, 190, 225, 255), 12)
$graphics.DrawPath($innerPen, $path)

$font = [System.Drawing.Font]::new("Segoe UI", 350, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$textBounds = [System.Drawing.RectangleF]::new(0, -12, $size, $size)
$shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(80, 0, 16, 34))
$graphics.DrawString("BR", $font, $shadowBrush, [System.Drawing.RectangleF]::new(10, 12, $size, $size), $format)
$textBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$graphics.DrawString("BR", $font, $textBrush, $textBounds, $format)

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)

$textBrush.Dispose()
$shadowBrush.Dispose()
$format.Dispose()
$font.Dispose()
$innerPen.Dispose()
$gradient.Dispose()
$path.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Write-Output "Wrote $OutputPath"
