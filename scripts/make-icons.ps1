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

# ---- the wordmark for a dark background ------------------------------------------------------
# The logo's "Quick" is near-black, which disappears on a dark page. Only those pixels are lifted;
# the violet-to-blue "Run" and the mark keep their colours. Done in C# over the raw bytes, because
# 1.5 million GetPixel calls from PowerShell is not a thing anyone should wait for.

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Wordmark
{
    /// <summary>Lifts every near-black pixel to the given colour, leaving everything else alone.</summary>
    public static void Lighten(string input, string output, byte r, byte g, byte b, double threshold)
    {
        using (var source = new Bitmap(input))
        using (var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        {
            var rectangle = new Rectangle(0, 0, source.Width, source.Height);

            var from = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var to = target.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            var bytes = Math.Abs(from.Stride) * source.Height;
            var pixels = new byte[bytes];
            Marshal.Copy(from.Scan0, pixels, 0, bytes);

            for (var at = 0; at < bytes; at += 4)
            {
                // BGRA in memory.
                double luma = (0.114 * pixels[at]) + (0.587 * pixels[at + 1]) + (0.299 * pixels[at + 2]);

                // Only the near-neutral dark of the type, never the mark: the hexagon's shadow face
                // is a saturated violet that is just as dark and must keep its colour.
                var high = Math.Max(pixels[at], Math.Max(pixels[at + 1], pixels[at + 2]));
                var low = Math.Min(pixels[at], Math.Min(pixels[at + 1], pixels[at + 2]));
                var neutral = high - low < 34;

                if (pixels[at + 3] > 8 && luma < threshold && neutral)
                {
                    pixels[at] = b;
                    pixels[at + 1] = g;
                    pixels[at + 2] = r;
                }
            }

            Marshal.Copy(pixels, 0, to.Scan0, bytes);
            source.UnlockBits(from);
            target.UnlockBits(to);
            target.Save(output, ImageFormat.Png);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core

$darkPath = Join-Path $OutDir 'logo-dark.png'
[Wordmark]::Lighten((Resolve-Path "$PSScriptRoot/../assets/logo.png").Path, $darkPath, 231, 224, 240, 90)
"wrote logo-dark.png"
