using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QuickRun.App.Daemon;

/// <summary>
/// Issues the token the browser extension sends with every request.
/// <para>
/// A token is handed out only while a pairing window is open, and the window can only be opened
/// from this machine. That is what stops any web page from silently obtaining one, without asking
/// the user to copy and paste a secret.
/// </para>
/// <para>
/// The window lives in a file rather than in memory, because <c>quickrun pair</c> runs in a
/// different process from the daemon it is pairing.
/// </para>
/// </summary>
public sealed class Pairing
{
    public static readonly TimeSpan WindowLength = TimeSpan.FromSeconds(60);

    private const string TokenFileName = "pair-token";
    private const string WindowFileName = "pair-window";

    private readonly string _tokenPath;
    private readonly string _windowPath;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    public Pairing(string configDirectory, Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _tokenPath = Path.Combine(configDirectory, TokenFileName);
        _windowPath = Path.Combine(configDirectory, WindowFileName);
    }

    public bool WindowOpen => ReadWindow() is { } until && _now() < until;

    public void OpenWindow() => WriteWindow(_now() + WindowLength);

    /// <summary>Returns the token if a window is open, otherwise null. Closes the window on success.</summary>
    public string? Claim()
    {
        lock (_gate)
        {
            if (!WindowOpen) return null;
            CloseWindow();

            var token = Load() ?? Create();
            Save(token);
            return token;
        }
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        var known = Load();
        if (known is null) return false;

        // Constant-time: this is a secret comparison.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(known), Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Invalidates the current token, so a leaked one can be revoked.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            TryDelete(_tokenPath);
            CloseWindow();
        }
    }

    private static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private string? Load()
    {
        try
        {
            var value = File.Exists(_tokenPath) ? File.ReadAllText(_tokenPath).Trim() : null;
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Save(string token) => Write(_tokenPath, token);

    private DateTimeOffset? ReadWindow()
    {
        try
        {
            if (!File.Exists(_windowPath)) return null;
            var text = File.ReadAllText(_windowPath).Trim();
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var until) ? until : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteWindow(DateTimeOffset until) => Write(_windowPath, until.ToString("O", CultureInfo.InvariantCulture));

    private void CloseWindow() => TryDelete(_windowPath);

    private static void Write(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        catch (IOException)
        {
            // Nothing to do: pairing simply cannot be completed on a read-only config directory.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* already gone, or locked */ }
    }
}
