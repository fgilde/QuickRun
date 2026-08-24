using System.Text;

namespace QuickRun.Core.Process;

/// <summary>
/// Splits a byte stream into lines on CR <em>and</em> LF.
/// <para>
/// Necessary because git writes progress as carriage-return overwrites on one line: a reader that
/// only breaks on LF sees nothing until a stage finishes, which is exactly when the progress has
/// stopped being interesting.
/// </para>
/// </summary>
public sealed class LineSplitter
{
    private readonly StringBuilder _pending = new();

    public void Push(string chunk, Action<string> onLine)
    {
        foreach (var c in chunk)
        {
            if (c is '\r' or '\n')
            {
                // CRLF must not produce a spurious empty line.
                if (_pending.Length > 0) Emit(onLine);
                continue;
            }
            _pending.Append(c);
        }
    }

    /// <summary>Emits whatever is left when the stream ends without a terminator.</summary>
    public void Flush(Action<string> onLine)
    {
        if (_pending.Length > 0) Emit(onLine);
    }

    private void Emit(Action<string> onLine)
    {
        var line = _pending.ToString();
        _pending.Clear();
        onLine(line);
    }
}
