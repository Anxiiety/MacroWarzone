using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MacroWarzone.Vision;

public sealed class OverlayRenderer : IDisposable
{
    private sealed class SkeletonTrack
    {
        public int Id;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public float Confidence;
        public float VelocityX;
        public float VelocityY;
        public long LastUpdateTicks;
        public int MissingFrames;
    }

    private Form? _overlayForm;
    private Timer? _animationTimer;
    private bool _isVisible;
    private readonly object _lock = new();
    private readonly List<SkeletonTrack> _tracks = new();
    private int _nextTrackId = 1;
    private int _frameCount;

    private const float MatchDistanceThreshold = 0.12f; // normalized distance
    private const int MaxMissingFrames = 12;             // ~400ms @ 30Hz

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

            int style = GetWindowLong(_overlayForm.Handle, -20);
            SetWindowLong(_overlayForm.Handle, -20, style | 0x80000 | 0x20);

            _overlayForm.Paint += OnPaint;
            _overlayForm.Show();
            _overlayForm.BringToFront();

            _animationTimer = new Timer { Interval = 16 }; // ~60 FPS overlay
            _animationTimer.Tick += (_, _) => AnimateTracksAndInvalidate();
            _animationTimer.Start();

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
            _frameCount++;
            UpdateTracksFromDetections(detections);
        }

        if (_frameCount % 30 == 0)
        {
            Debug.WriteLine($"[OVERLAY] Update #{_frameCount}: {detections.Count} detections, {_tracks.Count} active tracks");
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

    private void UpdateTracksFromDetections(List<AIVisionService.Detection> detections)
    {
        long now = Stopwatch.GetTimestamp();
        var unmatchedTrackIds = new HashSet<int>(_tracks.Select(t => t.Id));

        foreach (var detection in detections)
        {
            SkeletonTrack? bestTrack = null;
            float bestDistance = float.MaxValue;

            foreach (var track in _tracks)
            {
                if (!unmatchedTrackIds.Contains(track.Id))
                    continue;

                float dx = track.X - detection.X;
                float dy = track.Y - detection.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist < MatchDistanceThreshold && dist < bestDistance)
                {
                    bestDistance = dist;
                    bestTrack = track;
                }
            }

            if (bestTrack == null)
            {
                _tracks.Add(new SkeletonTrack
                {
                    Id = _nextTrackId++,
                    X = detection.X,
                    Y = detection.Y,
                    Width = detection.Width,
                    Height = detection.Height,
                    Confidence = detection.Confidence,
                    VelocityX = 0,
                    VelocityY = 0,
                    LastUpdateTicks = now,
                    MissingFrames = 0
                });

                continue;
            }

            unmatchedTrackIds.Remove(bestTrack.Id);

            float dt = MathF.Max(0.001f, (now - bestTrack.LastUpdateTicks) / (float)Stopwatch.Frequency);
            float newVx = (detection.X - bestTrack.X) / dt;
            float newVy = (detection.Y - bestTrack.Y) / dt;

            bestTrack.VelocityX = (bestTrack.VelocityX * 0.6f) + (newVx * 0.4f);
            bestTrack.VelocityY = (bestTrack.VelocityY * 0.6f) + (newVy * 0.4f);

            bestTrack.X = detection.X;
            bestTrack.Y = detection.Y;
            bestTrack.Width = (bestTrack.Width * 0.7f) + (detection.Width * 0.3f);
            bestTrack.Height = (bestTrack.Height * 0.7f) + (detection.Height * 0.3f);
            bestTrack.Confidence = detection.Confidence;
            bestTrack.LastUpdateTicks = now;
            bestTrack.MissingFrames = 0;
        }

        for (int i = _tracks.Count - 1; i >= 0; i--)
        {
            var track = _tracks[i];
            if (unmatchedTrackIds.Contains(track.Id))
            {
                track.MissingFrames++;
                if (track.MissingFrames > MaxMissingFrames)
                {
                    _tracks.RemoveAt(i);
                }
            }
        }
    }

    private void AnimateTracksAndInvalidate()
    {
        lock (_lock)
        {
            long now = Stopwatch.GetTimestamp();
            foreach (var track in _tracks)
            {
                float dt = MathF.Max(0.0f, (now - track.LastUpdateTicks) / (float)Stopwatch.Frequency);
                if (dt <= 0)
                    continue;

                float predictionDt = MathF.Min(dt, 0.05f);
                track.X = Math.Clamp(track.X + (track.VelocityX * predictionDt), 0.02f, 0.98f);
                track.Y = Math.Clamp(track.Y + (track.VelocityY * predictionDt), 0.02f, 0.98f);
                track.LastUpdateTicks = now;

                track.VelocityX *= 0.92f;
                track.VelocityY *= 0.92f;
            }
        }

        _overlayForm?.Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        List<SkeletonTrack> tracks;
        lock (_lock)
        {
            tracks = _tracks.Select(t => new SkeletonTrack
            {
                Id = t.Id,
                X = t.X,
                Y = t.Y,
                Width = t.Width,
                Height = t.Height,
                Confidence = t.Confidence,
                MissingFrames = t.MissingFrames
            }).ToList();
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var debugPen = new Pen(Color.Red, 3))
        {
            g.DrawLine(debugPen, 10, 10, 50, 50);
            g.DrawLine(debugPen, 50, 10, 10, 50);
        }

        using (var font = new Font("Arial", 14, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.Yellow))
        {
            g.DrawString($"Detections/Tracks: {tracks.Count} | Frame: {_frameCount}", font, brush, 60, 20);
        }

        int screenWidth = Screen.PrimaryScreen.Bounds.Width;
        int screenHeight = Screen.PrimaryScreen.Bounds.Height;

        foreach (var track in tracks)
        {
            float centerX = track.X * screenWidth;
            float centerY = track.Y * screenHeight;
            float width = track.Width * screenWidth;
            float height = track.Height * screenHeight;

            float x = centerX - width / 2;
            float y = centerY - height / 2;

            using var boxPen = new Pen(Color.FromArgb(220, 255, 80, 80), 2);
            g.DrawRectangle(boxPen, x, y, width, height);

            DrawSkeleton(g, x, y, width, height, track.Confidence, track.MissingFrames);
        }
    }

    private static void DrawSkeleton(Graphics g, float x, float y, float width, float height, float confidence, int missingFrames)
    {
        float alphaFactor = Math.Clamp(1f - (missingFrames / (float)MaxMissingFrames), 0.2f, 1f);
        int alpha = (int)(255 * alphaFactor);

        float cx = x + width * 0.5f;

        var head = new PointF(cx, y + height * 0.14f);
        var neck = new PointF(cx, y + height * 0.25f);
        var chest = new PointF(cx, y + height * 0.38f);
        var pelvis = new PointF(cx, y + height * 0.62f);

        var shoulderL = new PointF(x + width * 0.33f, y + height * 0.30f);
        var shoulderR = new PointF(x + width * 0.67f, y + height * 0.30f);

        var elbowL = new PointF(x + width * 0.23f, y + height * 0.46f);
        var elbowR = new PointF(x + width * 0.77f, y + height * 0.46f);

        var handL = new PointF(x + width * 0.18f, y + height * 0.62f);
        var handR = new PointF(x + width * 0.82f, y + height * 0.62f);

        var hipL = new PointF(x + width * 0.42f, y + height * 0.63f);
        var hipR = new PointF(x + width * 0.58f, y + height * 0.63f);

        var kneeL = new PointF(x + width * 0.38f, y + height * 0.80f);
        var kneeR = new PointF(x + width * 0.62f, y + height * 0.80f);

        var footL = new PointF(x + width * 0.36f, y + height * 0.97f);
        var footR = new PointF(x + width * 0.64f, y + height * 0.97f);

        using var bonePen = new Pen(Color.FromArgb(alpha, 0, 255, 210), 2.5f);
        using var jointBrush = new SolidBrush(Color.FromArgb(alpha, 255, 230, 0));
        using var headPen = new Pen(Color.FromArgb(alpha, 255, 0, 80), 2);

        g.DrawEllipse(headPen, head.X - width * 0.07f, head.Y - height * 0.06f, width * 0.14f, height * 0.12f);

        g.DrawLine(bonePen, neck, chest);
        g.DrawLine(bonePen, chest, pelvis);

        g.DrawLine(bonePen, shoulderL, shoulderR);
        g.DrawLine(bonePen, shoulderL, elbowL);
        g.DrawLine(bonePen, elbowL, handL);
        g.DrawLine(bonePen, shoulderR, elbowR);
        g.DrawLine(bonePen, elbowR, handR);

        g.DrawLine(bonePen, pelvis, hipL);
        g.DrawLine(bonePen, pelvis, hipR);
        g.DrawLine(bonePen, hipL, kneeL);
        g.DrawLine(bonePen, kneeL, footL);
        g.DrawLine(bonePen, hipR, kneeR);
        g.DrawLine(bonePen, kneeR, footR);

        var joints = new[] { neck, chest, pelvis, shoulderL, shoulderR, elbowL, elbowR, handL, handR, hipL, hipR, kneeL, kneeR, footL, footR };
        foreach (var joint in joints)
        {
            g.FillEllipse(jointBrush, joint.X - 2.5f, joint.Y - 2.5f, 5f, 5f);
        }

        using var textFont = new Font("Arial", 10, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 0));
        g.DrawString($"AI {confidence:P0}", textFont, textBrush, x, y - 16);
    }

    public void Dispose()
    {
        Debug.WriteLine("[OVERLAY] Disposing...");

        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _animationTimer = null;
        }

        if (_overlayForm != null)
        {
            try
            {
                _overlayForm.Close();
                _overlayForm.Dispose();
            }
            catch
            {
                // ignored
            }

            _overlayForm = null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
