# Produce native PNG and multi-size ICO assets using only Windows WPF.
# No third-party icon libraries, network downloads or native resource shims.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$icoPath = Join-Path $assets 'OpenTabletDisplay.ico'
$pngPath = Join-Path $assets 'OpenTabletDisplay.png'

function Brush([string]$c) {
    return [System.Windows.Media.SolidColorBrush]::new(
        [System.Windows.Media.ColorConverter]::ConvertFromString($c))
}
function Pen($b,[double]$w) {
    $p = [System.Windows.Media.Pen]::new($b,$w)
    $p.LineJoin = 'Round'
    $p.Freeze()
    return $p
}
function Gradient([string[]]$c) {
    $b = [System.Windows.Media.LinearGradientBrush]::new()
    $b.StartPoint = [System.Windows.Point]::new(0,0)
    $b.EndPoint = [System.Windows.Point]::new(1,1)
    $n = $c.Count - 1
    for($i=0;$i -lt $c.Count;$i++) {
        $b.GradientStops.Add([System.Windows.Media.GradientStop]::new(
            [System.Windows.Media.ColorConverter]::ConvertFromString($c[$i]),$i/[double]$n))
    }
    $b.Freeze()
    return $b
}
$visual = [System.Windows.Media.DrawingVisual]::new()
$dc = $visual.RenderOpen()
$bg = [System.Windows.Rect]::new(5,5,246,246)
$dc.PushClip([System.Windows.Media.RectangleGeometry]::new($bg,57,57))
$dc.DrawRoundedRectangle((Gradient @('#0977E7','#142278','#7F24AD')),(Pen (Brush '#94B5FF') 1.6),$bg,57,57)
$dc.DrawEllipse((Brush '#1840D5FF'),$null,[System.Windows.Point]::new(25,45),105,95)
$dc.DrawEllipse((Brush '#144C6FFF'),$null,[System.Windows.Point]::new(240,224),105,100)
# The cable ribbon is the recognizable bridge between the two screens.
$wave=[System.Windows.Media.Geometry]::Parse('M 10,180 C 58,216 95,162 135,171 C 159,171 180,190 206,214 L 199,231 C 166,203 149,190 133,191 C 88,190 48,236 10,207 Z')
$dc.DrawGeometry((Gradient @('#27D5FF','#477EF7','#C152FF')),$null,$wave)
$dc.DrawGeometry($null,(Pen (Brush '#8CE9FF') 3),[System.Windows.Media.Geometry]::Parse('M 11,176 C 56,204 93,164 134,166'))
# Rear desktop monitor, bezel, and blue panel.
$dc.DrawRoundedRectangle((Gradient @('#174DB3','#0C1C48')),
    (Pen (Gradient @('#E5FBFF','#7FC9FF','#B2A2FF')) 8),
    [System.Windows.Rect]::new(43,67,155,107),18,18)
$dc.DrawRoundedRectangle($null,(Pen (Brush '#3E80D8') 1.5),
    [System.Windows.Rect]::new(51,75,139,91),12,12)
# Foreground tablet, slightly rotated.
$dc.PushTransform([System.Windows.Media.RotateTransform]::new(7,177,165))
$dc.DrawRoundedRectangle((Gradient @('#256FC9','#151747','#37206B')),
    (Pen (Gradient @('#EAFBFF','#8AD9FF','#C5A3FF')) 8),
    [System.Windows.Rect]::new(143,109,83,114),17,17)
$dc.DrawEllipse((Brush '#C6E9FF'),$null,[System.Windows.Point]::new(185,211),3.8,3.8)
$dc.Pop()
$dc.Pop()
$dc.Close()
$bitmap=[System.Windows.Media.Imaging.RenderTargetBitmap]::new(256,256,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)

function Get-PNGBytes($source) {
    $enc=[System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($source))
    $m=[System.IO.MemoryStream]::new()
    $enc.Save($m)
    $b=$m.ToArray()
    $m.Dispose()
    return ,$b
}
[System.IO.File]::WriteAllBytes($pngPath, (Get-PNGBytes $bitmap))
$sizes=@(16,24,32,48,64,128,256)
$images=@()
foreach($size in $sizes) {
    $bmp = if ($size -eq 256) { $bitmap } else {
        [System.Windows.Media.Imaging.TransformedBitmap]::new(
            $bitmap,[System.Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
    }
    $images += [pscustomobject]@{ S=$size; B=[byte[]](Get-PNGBytes $bmp) }
}
$stream=[System.IO.File]::Create($icoPath)
try {
    $w=[System.IO.BinaryWriter]::new($stream)
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach($img in $images) {
        $w.Write([byte]($img.S % 256)); $w.Write([byte]($img.S % 256))
        $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$img.B.Length); $w.Write([uint32]$offset)
        $offset += $img.B.Length
    }
    foreach($img in $images) { $w.Write([byte[]]$img.B) }
    $w.Flush()
} finally { $stream.Dispose() }

Write-Host 'Generated native high-resolution PNG and multi-size ICO for the compiled WPF application.'
