# Builds the platform icon files from assets/icon.png.
#
# Windows wants a multi-size .ico for the taskbar, Explorer and the tray; macOS wants an .icns in
# the app bundle. Both are containers around PNGs, so both are built here rather than by hand, and
# the results are committed - the release runners have no image tooling to rely on.
#
#   pwsh scripts/make-icons.ps1
[CmdletBinding()]
param(
  [string] $Source = "$PSScriptRoot/../assets/icon.png",
  [string] $OutDir = "$PSScriptRoot/../assets"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$original = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)

function Png([int] $size) {
  $bitmap = New-Object System.Drawing.Bitmap $size, $size
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.InterpolationMode = 'HighQualityBicubic'
  $graphics.PixelOffsetMode = 'HighQuality'
  $graphics.SmoothingMode = 'HighQuality'
  $graphics.Clear([System.Drawing.Color]::Transparent)
  $graphics.DrawImage($original, 0, 0, $size, $size)
  $graphics.Dispose()

  $stream = New-Object System.IO.MemoryStream
  $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
  return $stream.ToArray()
}

# ---- .ico ----------------------------------------------------------------------------------
# ICONDIR + one ICONDIRENTRY per image, each image a whole PNG file. Vista and later read that.

$sizes = 16, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { Png $_ }

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16] 0)              # reserved
$writer.Write([uint16] 1)              # type: icon
$writer.Write([uint16] $sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $size = $sizes[$i]
  $writer.Write([byte] ($(if ($size -ge 256) { 0 } else { $size })))   # 0 means 256
  $writer.Write([byte] ($(if ($size -ge 256) { 0 } else { $size })))
  $writer.Write([byte] 0)              # palette
  $writer.Write([byte] 0)              # reserved
  $writer.Write([uint16] 1)            # colour planes
  $writer.Write([uint16] 32)           # bits per pixel
  $writer.Write([uint32] $images[$i].Length)
  $writer.Write([uint32] $offset)
  $offset += $images[$i].Length
}

foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'quickrun.ico'), $ico.ToArray())
"wrote quickrun.ico ($($ico.Length) bytes, $($sizes -join '/') px)"

# ---- .icns ---------------------------------------------------------------------------------
# 'icns' + total length, then one type/length/data chunk per image, big-endian throughout.

$types = @{ 'ic07' = 128; 'ic08' = 256; 'ic09' = 512; 'ic11' = 32; 'ic12' = 64; 'ic14' = 512 }

$chunks = New-Object System.IO.MemoryStream
foreach ($type in $types.Keys) {
  $data = Png $types[$type]
  $chunks.Write([System.Text.Encoding]::ASCII.GetBytes($type), 0, 4)

  $length = [System.BitConverter]::GetBytes([uint32] ($data.Length + 8))
  if ([System.BitConverter]::IsLittleEndian) { [array]::Reverse($length) }
  $chunks.Write($length, 0, 4)
  $chunks.Write($data, 0, $data.Length)
}

$icns = New-Object System.IO.MemoryStream
$icns.Write([System.Text.Encoding]::ASCII.GetBytes('icns'), 0, 4)
$total = [System.BitConverter]::GetBytes([uint32] ($chunks.Length + 8))
if ([System.BitConverter]::IsLittleEndian) { [array]::Reverse($total) }
$icns.Write($total, 0, 4)
$icns.Write($chunks.ToArray(), 0, $chunks.Length)

[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'quickrun.icns'), $icns.ToArray())
"wrote quickrun.icns ($($icns.Length) bytes)"

$original.Dispose()
