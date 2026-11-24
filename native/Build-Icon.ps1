$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot 'icon'
[xml]$svg = Get-Content -LiteralPath (Join-Path $destination 'eye-care.svg') -Raw
if ($svg.svg.viewBox -ne '0 0 256 256' -or $svg.svg.path.d -ne 'M48 128C88 68 168 68 208 128C168 188 88 188 48 128Z') { throw 'Unsupported icon geometry' }
$sizes = @(16,20,24,32,48,64,128,256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $canvas = [Drawing.Bitmap]::new($size*4,$size*4,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($canvas)
    $background = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($svg.svg.rect.fill))
    $iris = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($svg.svg.circle.fill))
    $outline = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($svg.svg.path.stroke),14)
    $box = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $scaled = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $output = [Drawing.Graphics]::FromImage($scaled)
    $data = [IO.MemoryStream]::new()
    try {
        $g.Clear([Drawing.Color]::Transparent)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.ScaleTransform($size*4.0/256,$size*4.0/256)
        $box.AddArc(8,8,112,112,180,90); $box.AddArc(136,8,112,112,270,90)
        $box.AddArc(136,136,112,112,0,90); $box.AddArc(8,136,112,112,90,90)
        $box.CloseFigure(); $g.FillPath($background,$box)
        $shape.AddBezier(48,128,88,68,168,68,208,128)
        $shape.AddBezier(208,128,168,188,88,188,48,128); $shape.CloseFigure()
        $outline.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($outline,$shape); $g.FillEllipse($iris,104,104,48,48)
        $output.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $output.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $output.DrawImage($canvas,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$canvas.Width,$canvas.Height,[Drawing.GraphicsUnit]::Pixel)
        $scaled.Save($data,[Drawing.Imaging.ImageFormat]::Png); $frames.Add($data.ToArray())
        if ($size -eq 256) { $scaled.Save((Join-Path $destination 'eye-care.png'),[Drawing.Imaging.ImageFormat]::Png) }
    } finally { $data.Dispose(); $output.Dispose(); $scaled.Dispose(); $shape.Dispose(); $box.Dispose(); $outline.Dispose(); $iris.Dispose(); $background.Dispose(); $g.Dispose(); $canvas.Dispose() }
}
$file = [IO.File]::Create((Join-Path $destination 'eye-care.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + $sizes.Count*16
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([int]$frames[$i].Length); $writer.Write([int]$offset); $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
