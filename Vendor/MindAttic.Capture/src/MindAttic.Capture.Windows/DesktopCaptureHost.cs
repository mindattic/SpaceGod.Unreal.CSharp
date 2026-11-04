using MindAttic.Capture.Hosting;
using MindAttic.Capture.Windows.Audio;
using MindAttic.Capture.Windows.Screenshots;
using MindAttic.Capture.Windows.Video;
#if !MINDATTIC_NO_WGC
using MindAttic.Capture.Windows.Wgc;
#endif

namespace MindAttic.Capture.Windows;

/// <summary>Desktop host options.</summary>
public sealed class DesktopHostOptions
{
    /// <summary>Include the mouse pointer in video.</summary>
    public bool DrawMouse { get; set; } = true;
    /// <summary>Use Windows Graphics Capture for window targets (follows moves, works while covered).</summary>
    public bool PreferWgcForWindows { get; set; } = true;
    /// <summary>Skip Desktop Duplication and use GDI for monitor/region video (e.g. inside RDP).</summary>
    public bool ForceGdiGrab { get; set; }
}

/// <summary>
/// Captures a monitor, region, window or process on the Windows desktop. Used directly by the agent
/// and by apps without a richer host adapter (WinForms, consoles, browsers).
/// </summary>
public sealed class DesktopCaptureHost(CaptureTarget target, DesktopHostOptions? options = null) : ICaptureHost
{
    private readonly DesktopHostOptions _options = options ?? new DesktopHostOptions();

    /// <summary>What this host captures.</summary>
    public CaptureTarget Target { get; } = target;

    /// <inheritdoc/>
    public string Name => "desktop";

    /// <inheritdoc/>
    public HostCapabilities Capabilities => HostCapabilities.None;

    /// <inheritdoc/>
    public ValueTask<CapturedImage> CaptureStillAsync(ScreenshotOptions options, CancellationToken ct) =>
        new(Task.Run(() => GdiScreenshotter.Capture(TargetResolver.Resolve(Target)), ct));

    /// <inheritdoc/>
    public VideoSource CreateVideoSource(int fps)
    {
        var resolved = TargetResolver.Resolve(Target);
#if !MINDATTIC_NO_WGC
        if (resolved.Hwnd != 0 && _options.PreferWgcForWindows && WgcWindowSource.IsSupported)
            return new WgcWindowSource(resolved.Hwnd, fps, _options.DrawMouse);
#endif // SpaceGod.Unreal.CSharp: built as net10.0 for UnrealSharp, so no WinRT; windows are captured as their screen rectangle (US-T72)
        var rect = TargetResolver.ClipToDesktop(resolved.Bounds, Desktop.GetMonitors());
        var outputs = _options.ForceGdiGrab ? [] : DxgiOutputs.GetAdapter0();
        return new NativeVideoSource(rect.Width, rect.Height, DesktopInputs.Candidates(rect, outputs, fps, _options.DrawMouse, _options.ForceGdiGrab));
    }

    /// <inheritdoc/>
    public IReadOnlyList<IAudioTrackCapture> CreateAudioTracks(AudioMode mode) => CreateWasapiTracks(mode);

    /// <summary>System loopback, plus the default microphone for <see cref="AudioMode.AppAndMic"/>.</summary>
    public static IReadOnlyList<IAudioTrackCapture> CreateWasapiTracks(AudioMode mode)
    {
        var tracks = new List<IAudioTrackCapture>();
        if (mode == AudioMode.None) return tracks;
        tracks.Add(WasapiTrack.Loopback());
        if (mode == AudioMode.AppAndMic)
        {
            try
            {
                tracks.Add(WasapiTrack.Microphone());
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // No microphone; continue with app audio only.
            }
        }
        return tracks;
    }
}
