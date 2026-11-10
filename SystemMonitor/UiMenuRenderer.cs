using System.Drawing;
using System.Windows.Forms;

namespace SystemMonitor;

/// <summary>Dark/light renderer for the top interface menu and context menus.</summary>
internal sealed class UiMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly UiTheme _theme;

    public UiMenuRenderer(UiTheme theme)
        : base(new UiMenuColorTable(theme))
    {
        _theme = theme;
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_theme.MenuBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_theme.MenuBorder);
        if (e.ToolStrip is MenuStrip)
        {
            e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }
        else
        {
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected && !e.Item.Pressed) return;
        using var brush = new SolidBrush(_theme.MenuHover);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripLabel)
        {
            e.TextColor = e.Item.ForeColor;
        }
        else if (!e.Item.Enabled)
        {
            e.TextColor = _theme.TextDim;
        }
        else
        {
            e.TextColor = e.Item.Selected || e.Item.Pressed ? _theme.TextMain : _theme.MenuText;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_theme.MenuBorder);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _theme.TextDim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var rect = e.ImageRectangle;
        int cx = rect.Width > 0 ? rect.Left + rect.Width / 2 : 14;
        int cy = rect.Height > 0 ? rect.Top + rect.Height / 2 : e.Item.Height / 2;
        using var brush = new SolidBrush(_theme.Accent);
        e.Graphics.FillEllipse(brush, cx - 3, cy - 3, 6, 6);
    }

    private sealed class UiMenuColorTable : ProfessionalColorTable
    {
        private readonly UiTheme _theme;

        public UiMenuColorTable(UiTheme theme)
        {
            _theme = theme;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _theme.MenuBack;
        public override Color ImageMarginGradientBegin => _theme.MenuBack;
        public override Color ImageMarginGradientMiddle => _theme.MenuBack;
        public override Color ImageMarginGradientEnd => _theme.MenuBack;
        public override Color MenuBorder => _theme.MenuBorder;
        public override Color MenuItemBorder => _theme.MenuHover;
        public override Color MenuItemSelected => _theme.MenuHover;
        public override Color MenuItemSelectedGradientBegin => _theme.MenuHover;
        public override Color MenuItemSelectedGradientEnd => _theme.MenuHover;
        public override Color MenuItemPressedGradientBegin => _theme.MenuHover;
        public override Color MenuItemPressedGradientEnd => _theme.MenuHover;
        public override Color MenuItemPressedGradientMiddle => _theme.MenuHover;
        public override Color SeparatorDark => _theme.MenuBorder;
        public override Color SeparatorLight => _theme.MenuBorder;
        public override Color CheckBackground => _theme.MenuHover;
        public override Color CheckSelectedBackground => _theme.MenuHover;
        public override Color CheckPressedBackground => _theme.MenuHover;
    }
}
