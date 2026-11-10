using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SystemMonitor;

/// <summary>
/// A compact 0–100 % line chart. The available samples always span the full width,
/// so the chart is readable from the very first seconds instead of growing from
/// a few pixels at the right edge (the old behaviour).
/// </summary>
internal sealed class Sparkline : Control
{
    private const int GridDivisions = 4;

    private readonly List<float> _data = new();
    private readonly Font _scaleFont = new("Segoe UI", 7f);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor { get; set; } = Color.FromArgb(76, 154, 255);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color GridColor { get; set; } = Color.FromArgb(42, 47, 58);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color MutedColor { get; set; } = Color.FromArgb(138, 147, 167);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Capacity { get; set; } = 120;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowScale { get; set; } = true;

    public Sparkline()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(31, 35, 43);
    }

    public int Count => _data.Count;

    public void Push(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
        _data.Add(Math.Clamp(value, 0f, 100f));
        Trim();
        Invalidate();
    }

    public void SetData(IEnumerable<float> values)
    {
        _data.Clear();
        if (values != null)
        {
            foreach (float value in values)
            {
                _data.Add(float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Clamp(value, 0f, 100f));
            }
        }
        Trim();
        Invalidate();
    }

    public float[] Snapshot() => _data.ToArray();

    private void Trim()
    {
        int extra = _data.Count - Math.Max(2, Capacity);
        if (extra > 0) _data.RemoveRange(0, extra);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        bool showScale = ShowScale && Width >= 220;
        int left = showScale ? 34 : 2;
        int right = Width - 6;
        int top = 6;
        int bottom = Height - 5;
        if (right - left < 8 || bottom - top < 8) return;

        DrawGrid(g, left, top, right, bottom);
        if (showScale) DrawScale(g, left, top, bottom);

        int count = _data.Count;
        if (count == 0)
        {
            using var emptyBrush = new SolidBrush(MutedColor);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("сбор данных…", _scaleFont, emptyBrush, new RectangleF(left, top, right - left, bottom - top), format);
            return;
        }

        float plotWidth = right - left;
        float plotHeight = bottom - top;

        // All samples (or the whole 2-minute window once it is full) span the full plot width.
        float X(int index) => count == 1 ? right - 1 : left + plotWidth * index / (count - 1);
        float Y(float value) => bottom - plotHeight * (value / 100f);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (count >= 2)
        {
            var points = new PointF[count];
            for (int i = 0; i < count; i++) points[i] = new PointF(X(i), Y(_data[i]));

            var polygon = new PointF[count + 2];
            Array.Copy(points, polygon, count);
            polygon[count] = new PointF(points[count - 1].X, bottom);
            polygon[count + 1] = new PointF(points[0].X, bottom);

            using (var fill = new LinearGradientBrush(
                       new Rectangle(left, top, Math.Max(1, (int)plotWidth), Math.Max(1, (int)plotHeight)),
                       Color.FromArgb(88, LineColor),
                       Color.FromArgb(6, LineColor),
                       LinearGradientMode.Vertical))
            {
                g.FillPolygon(fill, polygon);
            }

            using var pen = new Pen(LineColor, 1.7f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLines(pen, points);
        }

        float lastX = X(count - 1);
        float lastY = Y(_data[count - 1]);
        using (var halo = new SolidBrush(Color.FromArgb(70, LineColor)))
        {
            g.FillEllipse(halo, lastX - 4.5f, lastY - 4.5f, 9f, 9f);
        }
        using (var dot = new SolidBrush(LineColor))
        {
            g.FillEllipse(dot, lastX - 2.2f, lastY - 2.2f, 4.4f, 4.4f);
        }
    }

    private void DrawGrid(Graphics g, int left, int top, int right, int bottom)
    {
        using var pen = new Pen(GridColor);
        for (int i = 0; i <= GridDivisions; i++)
        {
            int y = bottom - (bottom - top) * i / GridDivisions;
            g.DrawLine(pen, left, y, right, y);
        }
    }

    private void DrawScale(Graphics g, int left, int top, int bottom)
    {
        using var brush = new SolidBrush(MutedColor);
        using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        for (int i = 0; i <= GridDivisions; i += 2)
        {
            int value = i * 100 / GridDivisions;
            int center = bottom - (bottom - top) * i / GridDivisions;
            float maxY = Math.Max(top, bottom - 14f);
            float y = Math.Clamp(center - 7f, top, maxY);
            g.DrawString(value + "%", _scaleFont, brush, new RectangleF(0, y, left - 6, 14), format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _scaleFont.Dispose();
        base.Dispose(disposing);
    }
}
