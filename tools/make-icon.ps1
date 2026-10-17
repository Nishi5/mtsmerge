# PNG 画像からマルチサイズの Windows アイコン (.ico) を生成する
#   使い方: powershell -File tools/make-icon.ps1 assets/icon.png src/MtsMerge/app.ico
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Destination
)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Resize([Drawing.Image] $img, [int] $size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $attr = New-Object Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)  # 縁のにじみ防止
    $g.DrawImage($img, (New-Object Drawing.Rectangle 0, 0, $size, $size), 0, 0, $img.Width, $img.Height, [Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose()
    return $bmp
}

# 32bpp BMP (DIB) 形式のアイコンデータ
function ToDib([Drawing.Bitmap] $bmp) {
    $size = $bmp.Width
    $ms = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter $ms
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($size * $size * 4 + $maskStride * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    $rect = New-Object Drawing.Rectangle 0, 0, $size, $size
    $data = $bmp.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($size * 4)
    for ($y = $size - 1; $y -ge 0; $y--) {  # ボトムアップ
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $row.Length)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $w.Write((New-Object byte[] ($maskStride * $size)))  # AND マスク（アルファを使うので全 0）
    $w.Flush()
    return , $ms.ToArray()
}

function ToPng([Drawing.Bitmap] $bmp) {
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$img = [Drawing.Image]::FromFile((Resolve-Path $Source))
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) {
    $bmp = Resize $img $s
    # 256px は PNG 圧縮、それ以外は互換性の高い BMP 形式
    if ($s -ge 256) { $images.Add((ToPng $bmp)) } else { $images.Add((ToDib $bmp)) }
    $bmp.Dispose()
}
$img.Dispose()

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
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
foreach ($bytes in $images) { $w.Write([byte[]]$bytes) }
$w.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Destination), $out.ToArray())
Write-Output "Created $Destination ($($out.Length) bytes, sizes: $($sizes -join ', '))"
