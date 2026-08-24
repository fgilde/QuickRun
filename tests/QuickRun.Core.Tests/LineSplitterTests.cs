using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class LineSplitterTests
{
    private static List<string> Split(params string[] chunks)
    {
        var lines = new List<string>();
        var splitter = new LineSplitter();
        foreach (var chunk in chunks) splitter.Push(chunk, lines.Add);
        splitter.Flush(lines.Add);
        return lines;
    }

    [Fact]
    public void Splits_on_newline()
        => Assert.Equal(new[] { "one", "two" }, Split("one\ntwo\n"));

    [Fact]
    public void Splits_on_carriage_return()
        => Assert.Equal(new[] { "one", "two" }, Split("one\rtwo\r"));

    [Fact]
    public void Crlf_does_not_produce_an_empty_line()
        => Assert.Equal(new[] { "one", "two" }, Split("one\r\ntwo\r\n"));

    [Fact]
    public void A_trailing_fragment_is_flushed()
        => Assert.Equal(new[] { "one", "partial" }, Split("one\npartial"));

    [Fact]
    public void Lines_split_across_chunks_are_joined()
        => Assert.Equal(new[] { "hello world" }, Split("hello ", "wor", "ld\n"));

    [Fact]
    public void Empty_input_yields_nothing()
        => Assert.Empty(Split(""));

    [Fact]
    public void Repeated_terminators_are_collapsed()
        => Assert.Equal(new[] { "a", "b" }, Split("a\n\n\r\nb\n"));

    /// <summary>
    /// The case this class exists for: git overwrites one line with carriage returns, so every
    /// update must surface, not just the last one.
    /// </summary>
    [Fact]
    public void Git_style_progress_overwrites_surface_individually()
    {
        var lines = Split("Receiving objects:  10% (1/10)\rReceiving objects:  50% (5/10)\r"
                          + "Receiving objects: 100% (10/10), done.\n");

        Assert.Equal(3, lines.Count);
        Assert.Contains("10%", lines[0]);
        Assert.Contains("50%", lines[1]);
        Assert.Contains("100%", lines[2]);
    }
}
