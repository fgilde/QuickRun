namespace QuickRun.App.Tests;

/// <summary>
/// The application icon, checked as a file.
/// <para>
/// This exists because a malformed icon is invisible until it is fatal. A generator bug wrote a
/// directory whose every entry claimed one byte of image data; Explorer shrugged and showed
/// something, and the first right-click on the tray icon took the whole application down inside
/// Avalonia with "Unable to load bitmap from provided data". Nothing else in the build looks at
/// this file, so nothing else would notice.
/// </para>
/// </summary>
public class IconTests
{
    private static readonly int[] Sizes = { 16, 32, 48, 64, 128, 256 };

    private static string IconPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "assets", "quickrun.ico");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("assets/quickrun.ico was not found above the test binary");
    }

    [Fact]
    public void The_application_icon_holds_every_size_as_a_readable_bitmap()
    {
        var bytes = File.ReadAllBytes(IconPath());

        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal(Sizes.Length, BitConverter.ToUInt16(bytes, 4));

        var found = new List<int>();

        for (var i = 0; i < Sizes.Length; i++)
        {
            var entry = 6 + (16 * i);
            var width = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = BitConverter.ToInt32(bytes, entry + 8);
            var offset = BitConverter.ToInt32(bytes, entry + 12);

            found.Add(width);

            // The bug that made this necessary wrote 1 here.
            Assert.True(length > 1000, $"the {width}px frame claims only {length} bytes");
            Assert.True(offset + length <= bytes.Length,
                $"the {width}px frame runs past the end of the file");

            // A PNG-compressed frame is legal in the format and fatal in Avalonia, which hands the
            // frame straight to Skia - and Skia refuses it.
            Assert.False(bytes[offset] == 0x89 && bytes[offset + 1] == 0x50,
                $"the {width}px frame is a PNG");

            // BITMAPINFOHEADER: 40 bytes, and the height is doubled by the AND mask below it.
            Assert.Equal(40, BitConverter.ToInt32(bytes, offset));
            Assert.Equal(width, BitConverter.ToInt32(bytes, offset + 4));
            Assert.Equal(width * 2, BitConverter.ToInt32(bytes, offset + 8));
        }

        Assert.Equal(Sizes, found.OrderBy(size => size).ToArray());
    }
}
