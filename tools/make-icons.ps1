# Gera os icones do SdFake (PC e Android) desenhando com System.Drawing. Nao baixa nada.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Rounded([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Desenha o cartao + lupa numa grade de 100x100, centrado em (cx, cy) com a escala dada (em pixels por unidade).
function Draw-Card($g, [single]$u, [single]$ox, [single]$oy) {
    $state = $g.Save()
    $g.TranslateTransform($ox, $oy)
    $g.ScaleTransform($u, $u)

    # corpo do cartao: canto superior direito chanfrado
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddArc(20, 6, 12, 12, 180, 90)
    $body.AddLine(26, 6, 60, 6)
    $body.AddLine(60, 6, 80, 26)
    $body.AddLine(80, 26, 80, 88)
    $body.AddArc(68, 82, 12, 12, 0, 90)
    $body.AddArc(20, 82, 12, 12, 90, 90)
    $body.CloseFigure()
    $rect = New-Object System.Drawing.RectangleF(20, 6, 60, 88)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
        [System.Drawing.Color]::FromArgb(255, 59, 130, 246), [System.Drawing.Color]::FromArgb(255, 29, 78, 216), 90.0)
    $g.FillPath($grad, $body)

    # contatos dourados
    $gold = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 251, 191, 36))
    for ($i = 0; $i -lt 5; $i++) {
        $c = Rounded (27 + $i * 7.2) 13 4.6 15 1.6
        $g.FillPath($gold, $c)
    }

    # lupa
    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 6.5)
    $white.StartCap = 'Round'; $white.EndCap = 'Round'
    $glass = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 255, 255, 255))
    $g.FillEllipse($glass, 33, 42, 28, 28)
    $g.DrawEllipse($white, 33, 42, 28, 28)
    $handle = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 8)
    $handle.StartCap = 'Round'; $handle.EndCap = 'Round'
    $g.DrawLine($handle, 58, 67, 70, 80)

    $g.Restore($state)
}

function Render([int]$size, [string]$mode) {
    $c = New-Canvas $size
    $bmp = $c[0]; $g = $c[1]
    if ($mode -eq 'card') {
        Draw-Card $g ($size / 100.0) 0 0
    } elseif ($mode -eq 'tile') {
        # icone legado do Android: quadrado arredondado escuro com o cartao dentro
        $bg = Rounded 0 0 $size $size ($size * 0.2)
        $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 15, 23, 42))), $bg)
        $u = $size * 0.78 / 100.0
        Draw-Card $g $u ($size * 0.11) ($size * 0.11)
    } elseif ($mode -eq 'foreground') {
        # camada da frente do icone adaptativo: conteudo dentro da zona segura central (66/108)
        $u = $size * 0.56 / 100.0
        Draw-Card $g $u ($size * 0.22) ($size * 0.22)
    }
    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, [string]$path) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

# Entrada de icone em formato DIB 32 bits (compatibilidade maxima para tamanhos pequenos).
function Get-DibBytes($bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $w.Write([byte]$p.B); $w.Write([byte]$p.G); $w.Write([byte]$p.R); $w.Write([byte]$p.A)
        }
    }
    $maskRow = [int]([math]::Ceiling($n / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $n)))
    $w.Flush()
    return ,$ms.ToArray()
}

function Save-Ico([string]$path, [int[]]$sizes) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $path) | Out-Null
    $images = @()
    foreach ($s in $sizes) {
        $bmp = Render $s 'card'
        if ($s -ge 256) { $images += ,(Get-PngBytes $bmp) } else { $images += ,(Get-DibBytes $bmp) }
        $bmp.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter($fs)
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = [byte]$(if ($s -ge 256) { 0 } else { $s })
        $w.Write($dim); $w.Write($dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32)
        $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $w.Write($img) }
    $w.Flush(); $fs.Close()
}

# ---- PC ----
Save-Ico (Join-Path $root 'pc\assets\sdfake.ico') @(16, 20, 24, 32, 40, 48, 64, 256)
$logo = Render 160 'card'; Save-Png $logo (Join-Path $root 'pc\assets\logo.png'); $logo.Dispose()

# ---- Android ----
$res = Join-Path $root 'android\res'
$dens = @{ 'mdpi' = 1.0; 'hdpi' = 1.5; 'xhdpi' = 2.0; 'xxhdpi' = 3.0; 'xxxhdpi' = 4.0 }
foreach ($k in $dens.Keys) {
    $legacy = Render ([int](48 * $dens[$k])) 'tile'
    Save-Png $legacy (Join-Path $res "mipmap-$k\ic_launcher.png"); $legacy.Dispose()
    $fg = Render ([int](108 * $dens[$k])) 'foreground'
    Save-Png $fg (Join-Path $res "mipmap-$k\ic_launcher_foreground.png"); $fg.Dispose()
}
$store = Render 512 'tile'; Save-Png $store (Join-Path $root 'android\loja\icone-512.png'); $store.Dispose()
$notif = Render 96 'card'; Save-Png $notif (Join-Path $res 'drawable-nodpi\logo.png'); $notif.Dispose()

Write-Host 'Icones gerados.'
