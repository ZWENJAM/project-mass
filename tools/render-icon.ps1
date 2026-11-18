$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$webroot = Join-Path $root 'src/ProjectMass/wwwroot'
[xml]$source = Get-Content -LiteralPath (Join-Path $webroot 'icon.svg') -Raw
# This repository-owned SVG contains only rectangles and circles. Render those
# primitives with GDI+ on Windows; no downloaded renderer or runtime CDN is needed.
foreach ($size in @(192, 512, 32)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 512.0, $size / 512.0)
        foreach ($shape in $source.DocumentElement.ChildNodes) {
            $brush = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml($shape.fill))
            try {
                if ($shape.LocalName -eq 'rect') {
                    $graphics.FillRectangle($brush, [single]$shape.x, [single]$shape.y, [single]$shape.width, [single]$shape.height)
                } elseif ($shape.LocalName -eq 'circle') {
                    $x = [single]$shape.cx - [single]$shape.r
                    $y = [single]$shape.cy - [single]$shape.r
                    $diameter = 2 * [single]$shape.r
                    $graphics.FillEllipse($brush, $x, $y, $diameter, $diameter)
                    if ($shape.stroke) {
                        $pen = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml($shape.stroke), [single]$shape.'stroke-width')
                        try { $graphics.DrawEllipse($pen, $x, $y, $diameter, $diameter) }
                        finally { $pen.Dispose() }
                    }
                }
            } finally { $brush.Dispose() }
        }
        $name = if ($size -eq 32) { 'favicon.png' } else { "icon-$size.png" }
        $bitmap.Save((Join-Path $webroot $name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
