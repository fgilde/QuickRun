using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using QuickRun.App.Commands;
using Microsoft.Web.WebView2.Core;

namespace QuickRun.App.Ui;

/// <summary>
/// Shows the local web UI inside the desktop window, where the system has a WebView to show it with.
/// <para>
/// The point is one interface instead of two: the page served on 127.0.0.1 is where new features go,
/// and the window renders that page rather than a second implementation of it. Nothing is bundled -
/// this is the browser engine the operating system already ships (WebView2 on Windows). Where there
/// is none, the window keeps its native view, which is why this returns null rather than throwing.
/// </para>
/// </summary>
public static class EmbeddedBrowser
{
    /// <summary>Set to opt out and get the native window back.</summary>
    private const string OptOut = "QUICKRUN_NO_WEBVIEW";

    /// <summary>Whether the window could host the web UI on this machine.</summary>
    public static bool Available()
    {
        if (Environment.GetEnvironmentVariable(OptOut) is { Length: > 0 }) return false;
        if (!OperatingSystem.IsWindows()) return false;

        return WebView2Available();
    }

    /// <summary>The control, or null when the machine cannot host one.</summary>
    public static Control? TryCreate(string url, Action<string> onFailure)
    {
        if (!Available()) return null;

        return OperatingSystem.IsWindows() ? Windows(url, onFailure) : null;
    }

    [SupportedOSPlatform("windows")]
    private static Control? Windows(string url, Action<string> onFailure)
    {
        try { return new WebView2Host(url, onFailure); }
        catch (Exception e) when (e is DllNotFoundException or TypeInitializationException
                                     or PlatformNotSupportedException)
        {
            onFailure(e.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool WebView2Available()
    {
        try
        {
            // Present as a runtime, an installed browser or nothing at all. Asking is cheap and
            // avoids putting an empty control in the window.
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (Exception e) when (e is WebView2RuntimeNotFoundException or DllNotFoundException
                                     or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// A WebView2 parented into the child window Avalonia hands out.
/// <para>
/// WebView2 does not lay itself out: it fills the rectangle it is told to, so the bounds are pushed
/// again on every resize. Everything happens after the control exists, which is why a failure here
/// reports itself instead of throwing into a layout pass.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WebView2Host : NativeControlHost
{
    private readonly string _url;
    private readonly Action<string> _onFailure;

    private CoreWebView2Controller? _controller;
    private nint _child;

    public WebView2Host(string url, Action<string> onFailure)
    {
        _url = url;
        _onFailure = onFailure;

        // The control's size in device pixels is what WebView2 wants, and Avalonia reports it in
        // layout units - so the client rectangle of the child window is the honest source.
        LayoutUpdated += (_, _) => Resize();
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var child = base.CreateNativeControlCore(parent);
        _child = child.Handle;
        _ = AttachAsync(child.Handle);
        return child;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller?.Close();
        _controller = null;
        _child = 0;
        base.DestroyNativeControlCore(control);
    }

    private async Task AttachAsync(nint parent)
    {
        try
        {
            // Its own profile directory: QuickRun's cookies and local storage are not the user's
            // browsing profile, and a shared one would need write access to wherever the exe sits.
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickRun", "webview");
            Directory.CreateDirectory(profile);

            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            var controller = await environment.CreateCoreWebView2ControllerAsync(parent);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_child == 0)
                {
                    // The window closed while the engine was starting.
                    controller.Close();
                    return;
                }

                _controller = controller;

                var settings = controller.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = true;
                settings.AreDevToolsEnabled = true;
                settings.IsStatusBarEnabled = false;

                // A link to somewhere else belongs in the real browser, not in a tool window with
                // no address bar to see where it went.
                controller.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    UiCommand.Launch(e.Uri);
                };

                Resize();
                controller.CoreWebView2.Navigate(_url);
            });
        }
        catch (Exception e)
        {
            // Any failure here means the window has an empty rectangle in it, so the caller has to
            // hear about it - narrowing this would only hide the reason.
            _onFailure($"{e.GetType().Name}: {e.Message}");
        }
    }

    private void Resize()
    {
        if (_controller is null || _child == 0) return;
        if (!GetClientRect(_child, out var rect)) return;

        var bounds = new System.Drawing.Rectangle(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (bounds is { Width: <= 0 } or { Height: <= 0 }) return;

        _controller.Bounds = bounds;
        _controller.IsVisible = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);
}
