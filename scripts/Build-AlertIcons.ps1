Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot '../src/AIResetRadar.App/Resources'
$brand = [Drawing.Image]::FromFile((Join-Path $root 'Images/token_baron.png'))
foreach ($mode in @('alert','tray')) {
foreach ($entry in @{ info='#58A6FF'; warning='#F2C94C'; critical='#FF5D68' }.GetEnumerator()) {
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    $sizes = @(16,20,24,32,40,48,64)
    $frames = @()
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.SmoothingMode = 'AntiAlias'
        if ($mode -eq 'tray') {
            $graphics.InterpolationMode = 'HighQualityBicubic'
            $graphics.DrawImage($brand,0,0,$size,$size)
            $graphics.TranslateTransform($size*.65,$size*.65)
            $graphics.ScaleTransform(.35,.35)
        }
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($entry.Value))
        $border = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#1D1E25'))
        $graphics.FillEllipse($border, $size*.24,$size*.24,$size*.72,$size*.72)
        $graphics.FillEllipse($brush, $size*.32,$size*.32,$size*.56,$size*.56)
        $png = [IO.MemoryStream]::new(); $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
        $frames += ,$png.ToArray()
        $png.Dispose(); $brush.Dispose(); $border.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $writer.Write([byte]$sizes[$i]); $writer.Write([byte]$sizes[$i]); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach($frame in $frames) { $writer.Write([byte[]]$frame) }
    [IO.File]::WriteAllBytes((Join-Path $root "$mode-$($entry.Key).ico"),$stream.ToArray())
    $writer.Dispose(); $stream.Dispose()
}

}
$brand.Dispose()

