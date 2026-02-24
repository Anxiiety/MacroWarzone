using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MacroWarzone.Vision;

/// <summary>
/// Screen capture usando GDI+ (più compatibile di Desktop Duplication)
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private bool _isInitialized;

    public bool Initialize()
    {
        _isInitialized = true;
        System.Diagnostics.Debug.WriteLine("[SCREEN CAPTURE] ✓ Initialized (GDI+ mode)");
        return true;
    }

    public Bitmap? CaptureFrame()
    {
        if (!_isInitialized)
            return null;

        try
        {
            // Cattura schermo primario con GDI+
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCREEN CAPTURE] Capture error: {ex.Message}");
            return null;
        }
    }

    public Bitmap? CaptureRegion(int x, int y, int width, int height)
    {
        using var fullFrame = CaptureFrame();
        if (fullFrame == null)
            return null;

        x = Math.Max(0, Math.Min(x, fullFrame.Width - width));
        y = Math.Max(0, Math.Min(y, fullFrame.Height - height));
        width = Math.Min(width, fullFrame.Width - x);
        height = Math.Min(height, fullFrame.Height - y);

        var rect = new Rectangle(x, y, width, height);
        return fullFrame.Clone(rect, fullFrame.PixelFormat);
    }

    public void Dispose()
    {
        _isInitialized = false;
    }
}