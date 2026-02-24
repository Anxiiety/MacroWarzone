using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;

namespace MacroWarzone.Vision;

public sealed class OverlayRenderer : IDisposable
{
    private Form? _overlayForm;
    private bool _isVisible;
    private List<AIVisionService.Detection> _currentDetections = new();
    private readonly object _lock = new();
    private int _frameCount = 0;  // ← DEBUG COUNTER

    public bool IsVisible => _isVisible;

    public void Initialize()
    {
        if (_overlayForm != null)
        {
            Debug.WriteLine("[OVERLAY] Already initialized");
            return;
        }

        Debug.WriteLine("[OVERLAY] Creating overlay form...");

        try
        {
            _overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Color.Black,
                TransparencyKey = Color.Black,
                TopMost = true,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                Size = Screen.PrimaryScreen.Bounds.Size,
                Opacity = 1.0
            };

            Debug.WriteLine($"[OVERLAY] Form created: {_overlayForm.Size}");

            // Click-through
            int style = GetWindowLong(_overlayForm.Handle, -20);
            SetWindowLong(_overlayForm.Handle, -20, style | 0x80000 | 0x20);

            _overlayForm.Paint += OnPaint;
            _overlayForm.Show();
            _overlayForm.BringToFront();

            _isVisible = true;
            Debug.WriteLine("[OVERLAY] ✓ Form visible and TopMost");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OVERLAY] ❌ Init failed: {ex.Message}");
        }
    }

    public void UpdateDetections(List<AIVisionService.Detection> detections)
    {
        lock (_lock)
        {
            _currentDetections = new List<AIVisionService.Detection>(detections);
            _frameCount++;
        }

        // ✅ LOG OGNI UPDATE
        if (_frameCount % 30 == 0) // Ogni 30 frame (1 sec @ 30Hz)
        {
            Debug.WriteLine($"[OVERLAY] Update #{_frameCount}: {detections.Count} detections");
        }

        try
        {
            _overlayForm?.Invoke((MethodInvoker)delegate
            {
                _overlayForm?.Invalidate();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OVERLAY] Update error: {ex.Message}");
        }
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        List<AIVisionService.Detection> detections;
        lock (_lock)
        {
            detections = new List<AIVisionService.Detection>(_currentDetections);
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // ✅ SEMPRE DISEGNA QUALCOSA (DEBUG)
        // Disegna X rosso in alto a sinistra per verificare overlay funziona
        using (var debugPen = new Pen(Color.Red, 3))
        {
            g.DrawLine(debugPen, 10, 10, 50, 50);
            g.DrawLine(debugPen, 50, 10, 10, 50);
        }

        // ✅ DISEGNA COUNTER (DEBUG)
        using (var font = new Font("Arial", 16, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.Yellow))
        {
            g.DrawString($"Detections: {detections.Count} | Frame: {_frameCount}",
                         font, brush, 60, 20);
        }

        if (detections.Count == 0)
        {
            Debug.WriteLine("[OVERLAY] OnPaint called but 0 detections");
            return;
        }

        Debug.WriteLine($"[OVERLAY] Drawing {detections.Count} boxes");

        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
        int screenHeight = Screen.PrimaryScreen.Bounds.Height;

        foreach (var detection in detections)
        {
            float centerX = detection.X * screenWidth;
            float centerY = detection.Y * screenHeight;
            float width = detection.Width * screenWidth;
            float height = detection.Height * screenHeight;

            float x = centerX - width / 2;
            float y = centerY - height / 2;

            // Box rosso SPESSO
            using var pen = new Pen(Color.Red, 5);
            g.DrawRectangle(pen, x, y, width, height);

            // Confidence
            string confText = $"{detection.Confidence:P0}";
            using var textFont = new Font("Arial", 14, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.Red);
            using var bgBrush = new SolidBrush(Color.FromArgb(200, Color.Black));

            var textSize = g.MeasureString(confText, textFont);
            g.FillRectangle(bgBrush, x, y - textSize.Height - 2, textSize.Width + 4, textSize.Height + 2);
            g.DrawString(confText, textFont, textBrush, x + 2, y - textSize.Height);

            Debug.WriteLine($"[OVERLAY] Drew box at ({x:F0}, {y:F0}) size ({width:F0}x{height:F0})");
        }
    }

    public void Dispose()
    {
        Debug.WriteLine("[OVERLAY] Disposing...");
        if (_overlayForm != null)
        {
            try
            {
                _overlayForm.Close();
                _overlayForm.Dispose();
            }
            catch { }
            _overlayForm = null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}