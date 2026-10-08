$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$stream = [IO.File]::Create((Join-Path $PSScriptRoot 'USBPal.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $frames = @()
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size)
        $g = [Drawing.Graphics]::FromImage($bitmap)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([Drawing.Color]::FromArgb(16,23,32))
        $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(86,225,195))
        $font = [Drawing.Font]::new('Segoe UI', [single]($size*0.49), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        $format = [Drawing.StringFormat]::new()
        $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
        $g.DrawString('UP',$font,$brush,[Drawing.RectangleF]::new(0,0,$size,$size*0.92),$format)
        $g.FillRectangle($brush,[single]($size*0.22),[single]($size*0.83),[single]($size*0.56),[single]($size*0.055))
        $ms=[IO.MemoryStream]::new(); $bitmap.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
        $frames += ,@($size,$ms.ToArray())
        $ms.Dispose(); $format.Dispose(); $font.Dispose(); $brush.Dispose(); $g.Dispose(); $bitmap.Dispose()
    }
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6+16*$frames.Count
    foreach ($frame in $frames) {
        $dim=if($frame[0] -eq 256){0}else{$frame[0]}
        $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame[1].Length); $writer.Write([uint32]$offset)
        $offset += $frame[1].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
} finally { $writer.Dispose(); $stream.Dispose() }
