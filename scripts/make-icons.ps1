# Builds the platform icon files from assets/icon.png.
#
# Windows wants a multi-size .ico for the taskbar, Explorer, Alt-Tab and the tray; macOS wants an
# .icns in the app bundle; the website wants a light version of the wordmark. All three are written
# here and committed, because the release runners have no image tooling to rely on.
#
# The container writing happens in C# rather than PowerShell for a reason that cost a release: a
# PowerShell function returning a byte array has it unrolled into the pipeline, so `$images[$i]`
# became a single byte, every size and offset in the icon directory was written as 1, and the file
# was malformed. Windows Explorer tolerated it; Avalonia did not, and a right-click on the tray icon
# took the whole application down with "Unable to load bitmap from provided data".
#
#   pwsh scripts/make-icons.ps1
[CmdletBinding()]
param(
  [string] $Source = "$PSScriptRoot/../assets/icon.png",
  [string] $OutDir = "$PSScriptRoot/../assets"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class Icons
{
    /// <summary>One square copy of the source, drawn with the good resampler.</summary>
    private static Bitmap Scale(Image source, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, 0, 0, size, size);
        }

        return bitmap;
    }

    private static byte[] Png(Image source, int size)
    {
        using (var bitmap = Scale(source, size))
        using (var buffer = new MemoryStream())
        {
            bitmap.Save(buffer, ImageFormat.Png);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// One icon image as a device-independent bitmap: a BITMAPINFOHEADER whose height is doubled,
    /// the pixels bottom-up, then the AND mask. PNG-compressed frames are legal since Vista and are
    /// what most tools write, but Avalonia's Windows backend hands the frame to Skia, which refuses
    /// them - and that refusal is an unhandled exception on the UI thread.
    /// </summary>
    private static byte[] Dib(Image source, int size)
    {
        using (var bitmap = Scale(source, size))
        using (var buffer = new MemoryStream())
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write(40);                 // biSize
            writer.Write(size);               // biWidth
            writer.Write(size * 2);           // biHeight: colour data plus mask
            writer.Write((short)1);           // biPlanes
            writer.Write((short)32);          // biBitCount
            writer.Write(0);                  // biCompression: BI_RGB
            writer.Write(size * size * 4);    // biSizeImage
            writer.Write(0);                  // biXPelsPerMeter
            writer.Write(0);                  // biYPelsPerMeter
            writer.Write(0);                  // biClrUsed
            writer.Write(0);                  // biClrImportant

            var pixels = Pixels(bitmap);
            var stride = size * 4;

            // Bottom-up, as a DIB is stored.
            for (var y = size - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);

            // The AND mask. Fully transparent pixels are masked out, so that tools which ignore the
            // alpha channel still get the shape right. Rows are padded to four bytes.
            var maskStride = ((size + 31) / 32) * 4;

            for (var y = size - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];

                for (var x = 0; x < size; x++)
                {
                    var alpha = pixels[(y * stride) + (x * 4) + 3];
                    if (alpha == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                }

                writer.Write(row);
            }

            writer.Flush();
            return buffer.ToArray();
        }
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var locked = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = Math.Abs(locked.Stride) * bitmap.Height;
            var pixels = new byte[bytes];
            Marshal.Copy(locked.Scan0, pixels, 0, bytes);
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }

    /// <summary>The .ico: a directory of entries, then the images, with sizes and offsets that add up.</summary>
    public static string WriteIco(string input, string output, int[] sizes)
    {
        using (var source = Image.FromFile(input))
        {
            var frames = new byte[sizes.Length][];
            for (var i = 0; i < sizes.Length; i++) frames[i] = Dib(source, sizes[i]);

            using (var file = File.Create(output))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write((short)0);              // reserved
                writer.Write((short)1);              // type: icon
                writer.Write((short)sizes.Length);

                var offset = 6 + (16 * sizes.Length);

                for (var i = 0; i < sizes.Length; i++)
                {
                    var size = sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);           // palette entries
                    writer.Write((byte)0);           // reserved
                    writer.Write((short)1);          // colour planes
                    writer.Write((short)32);         // bits per pixel
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset += frames[i].Length;
                }

                foreach (var frame in frames) writer.Write(frame);
            }

            return output;
        }
    }

    /// <summary>
    /// The .icns: 'icns', the total length, then one type/length/data chunk per image, big-endian.
    /// PNG payloads here, which is what macOS reads and what every icns writer emits.
    /// </summary>
    public static string WriteIcns(string input, string output, string[] types, int[] sizes)
    {
        using (var source = Image.FromFile(input))
        using (var chunks = new MemoryStream())
        {
            for (var i = 0; i < types.Length; i++)
            {
                var data = Png(source, sizes[i]);
                var header = Encoding.ASCII.GetBytes(types[i]);
                chunks.Write(header, 0, 4);
                chunks.Write(BigEndian(data.Length + 8), 0, 4);
                chunks.Write(data, 0, data.Length);
            }

            using (var file = File.Create(output))
            {
                var body = chunks.ToArray();
                var magic = Encoding.ASCII.GetBytes("icns");
                file.Write(magic, 0, 4);
                file.Write(BigEndian(body.Length + 8), 0, 4);
                file.Write(body, 0, body.Length);
            }

            return output;
        }
    }

    /// <summary>Lifts every near-neutral dark pixel, for a wordmark on a dark background.</summary>
    public static string Lighten(string input, string output, byte r, byte g, byte b, double threshold)
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

                if (pixels[at + 3] > 8 && luma < threshold && high - low < 34)
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
            return output;
        }
    }

    private static byte[] BigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes;
    }
}
'@ -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core

$source = (Resolve-Path $Source).Path
$icoPath = Join-Path $OutDir 'quickrun.ico'
$icnsPath = Join-Path $OutDir 'quickrun.icns'
$darkPath = Join-Path $OutDir 'logo-dark.png'

[Icons]::WriteIco($source, $icoPath, [int[]] @(16, 32, 48, 64, 128, 256)) | Out-Null
"wrote quickrun.ico ($((Get-Item $icoPath).Length) bytes)"

[Icons]::WriteIcns($source, $icnsPath,
  [string[]] @('ic11', 'ic12', 'ic07', 'ic08', 'ic09', 'ic14'),
  [int[]] @(32, 64, 128, 256, 512, 512)) | Out-Null
"wrote quickrun.icns ($((Get-Item $icnsPath).Length) bytes)"

[Icons]::Lighten((Resolve-Path "$PSScriptRoot/../assets/logo.png").Path, $darkPath, 231, 224, 240, 90) | Out-Null
"wrote logo-dark.png ($((Get-Item $darkPath).Length) bytes)"

# ---- and then check it, because a malformed icon is invisible until something crashes ----------
#
# The directory is read back rather than trusted: the bug this replaces wrote entries claiming one
# byte per image, which Explorer shrugged off and Avalonia died on.

$bytes = [System.IO.File]::ReadAllBytes($icoPath)
if ([BitConverter]::ToUInt16($bytes, 2) -ne 1) { throw 'not an icon: type is not 1' }
$count = [BitConverter]::ToUInt16($bytes, 4)
if ($count -ne 6) { throw "expected 6 images, found $count" }

for ($i = 0; $i -lt $count; $i++) {
  $entry = 6 + (16 * $i)
  $width = if ($bytes[$entry] -eq 0) { 256 } else { [int] $bytes[$entry] }
  $length = [BitConverter]::ToInt32($bytes, $entry + 8)
  $offset = [BitConverter]::ToInt32($bytes, $entry + 12)

  if ($length -lt 1000) { throw "the $width px frame claims only $length bytes" }
  if ($offset + $length -gt $bytes.Length) { throw "the $width px frame runs past the end of the file" }

  # A PNG-compressed frame is legal in the format and fatal in Avalonia, which hands it to Skia.
  if ($bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50) { throw "the $width px frame is a PNG" }
  if ([BitConverter]::ToInt32($bytes, $offset) -ne 40) { throw "the $width px frame is not a DIB" }
}

"directory checks out: 6 DIB frames, offsets and sizes inside the file"

# And it decodes. Not 256: System.Drawing cannot select a 256 px frame at all - GDI+ predates them
# and hands back the next size down - so asking would fail on a perfectly good file.
foreach ($size in 16, 32, 48, 64, 128) {
  $frame = New-Object System.Drawing.Icon $icoPath, $size, $size
  if ($frame.Width -ne $size) { throw "the $size px frame came back as $($frame.Width) px" }
  $frame.Dispose()
}

"every frame decodes: 16/32/48/64/128"
