using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Pluto;

/// <summary>
/// A window background of nothing at all: the window is see-through wherever Pluto draws nothing, so the notch can have
/// its own shape (curved shoulders, rounded bottom) and change it on the compositor instead of resizing the window.
/// </summary>
internal sealed partial class ClearBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;
    private static nint s_queue;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
    }

    /// <summary>The system compositor the backdrop brush comes from; it needs a system dispatcher queue on this thread.</summary>
    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (s_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2 /* current */, ApartmentType = 0 };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_queue));
            }

            s_compositor = new Windows.UI.Composition.Compositor();
        }

        return s_compositor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
