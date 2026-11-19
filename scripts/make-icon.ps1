<#
  Tạo app.ico (16..256 px) và logo.png từ logo vector trong Themes/Logo.xaml.
  Chạy bằng Windows PowerShell 5.1 (dùng WPF có sẵn trong .NET Framework).
  powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
#>
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$xamlPath = Join-Path $Root 'src\CaoMinhComputer.UI\Themes\Logo.xaml'
$assets   = Join-Path $Root 'src\CaoMinhComputer.UI\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$dict  = [System.Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText($xamlPath))
$image = $dict['LogoMark']

function Render-Png([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()
    $pad = [Math]::Max(0, [int]($size * 0.02))
    $ctx.DrawImage($image, (New-Object System.Windows.Rect($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))))
    $ctx.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = Render-Png $s }

# ICO chứa các ảnh PNG (hỗ trợ từ Windows Vista).
$ico = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($ico)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$s].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $ico.ToArray())
[IO.File]::WriteAllBytes((Join-Path $assets 'logo-512.png'), (Render-Png 512))
Write-Host "Đã tạo app.ico và logo-512.png trong $assets"
