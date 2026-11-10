using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SystemMonitor;

public sealed class MainForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private const int ColMark = 0, ColName = 1, ColPid = 2, ColCpu = 3, ColRam = 4, ColStart = 5, ColPath = 6;
    private const double HeavyCpu = 10.0;
    private const long HeavyRam = 500L * 1024 * 1024;

    private static readonly string[] Headers = { "!", "Процесс", "PID", "CPU %", "RAM, МБ", "Запущен", "Путь" };
    private static readonly int[] ColumnWidths = { 30, 190, 66, 76, 90, 100, 360 };

    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "memory compression", "idle", "secure system", "msmpeng", "nissrv",
        "securityhealthservice", "securityhealthsystray", "trustedinstaller", "tiworker", "wuauclt",
        "usoclient", "backgroundtaskhost", "taskhostw", "dllhost", "wmiprvse", "audiodg", "spoolsv",
    };

    private readonly ProcessSampler _sampler = new();
    private readonly HardwareMonitor _hw = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private readonly List<ProcRow> _view = new();
    private readonly List<ProcRow> _userApps = new();
    private readonly List<float> _cpuHistory = new();
    private readonly List<float> _ramHistory = new();
    private const int HistoryCapacity = 120;

    private readonly UiSettings _settings;
    private string _themeId = UiTheme.Graphite.Id;
    private string _densityId = UiDensity.Normal.Id;
    private string _layoutId = UiLayout.Full.Id;
    private UiTheme _theme = UiTheme.Graphite;
    private UiDensity _density = UiDensity.Normal;
    private UiLayout _layout = UiLayout.Full;

    private Label _cpuValue, _cpuSub, _ramValue, _ramSub, _tempValue, _tempSub, _displayValue, _displaySub, _appsValue, _appsSub;
    private Sparkline _cpuSpark, _ramSpark;
    private TextBox _search;
    private CheckBox _onlyHeavy;
    private Label _statusLabel;
    private DataGridView _grid;
    private MenuStrip _menu;
    private Panel _headerHost, _sparksHost, _toolbarHost;

    private int _totalProcesses;
    private int _sortCol = ColCpu;
    private bool _sortAsc;
    private bool _busy;
    private bool _rebuildQueued;
    private DateTime _lastDisplayCheck = DateTime.MinValue;
    private string _displayText = "…";
    private int _displayHz;

    public MainForm()
    {
        _settings = UiSettings.Load();
        _themeId = _settings.Theme;
        _densityId = _settings.Density;
        _layoutId = _settings.Layout;
        NormalizeInterfaceIds();

        Text = "System Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 620);
        Size = new Size(1240, 800);
        KeyPreview = true;
        DoubleBuffered = true;

        BuildUi();

        _hw.Start();
        _timer.Tick += OnTick;
        _timer.Start();
        OnTick(null, EventArgs.Empty);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) KillSelected(false);
            else if (e.KeyCode == Keys.F5) OnTick(null, EventArgs.Empty);
        };
    }

    private void BuildUi()
    {
        BackColor = _theme.Bg;
        ForeColor = _theme.TextMain;
        Font = new Font("Segoe UI", _density.BaseFont);

        Control gridHost = null;
        if (_layout.ShowTable)
        {
            BuildGrid();
            gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0), BackColor = _theme.Bg };
            gridHost.Controls.Add(_grid);
        }

        if (_layout.ShowCards) _headerHost = BuildHeader();
        if (_layout.ShowCharts) _sparksHost = BuildSparks(_layout.ChartsFill);
        if (_layout.ShowToolbar) _toolbarHost = BuildToolbar();

        var status = BuildStatus();
        _menu = BuildMenuBar();

        // Dock order: the last control added is laid out first, so the menu ends up on top.
        if (gridHost != null) Controls.Add(gridHost);
        Controls.Add(status);
        if (_toolbarHost != null) Controls.Add(_toolbarHost);
        if (_sparksHost != null) Controls.Add(_sparksHost);
        if (_headerHost != null) Controls.Add(_headerHost);
        Controls.Add(_menu);
    }

    private void BuildGrid()
    {
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = _theme.Bg,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = _theme.GridLine,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = _density.GridHeaderHeight,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = _theme.Bg,
                ForeColor = _theme.TextMain,
                SelectionBackColor = _theme.Selection,
                SelectionForeColor = _theme.SelectionText,
                Font = new Font("Segoe UI", _density.GridFont),
                Padding = new Padding(6, 0, 6, 0),
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = _theme.HeaderBack,
                ForeColor = _theme.HeaderText,
                SelectionBackColor = _theme.HeaderBack,
                SelectionForeColor = _theme.HeaderText,
                Font = new Font("Segoe UI", Math.Max(7.5f, _density.GridFont - 0.5f), FontStyle.Bold),
                Padding = new Padding(6, 0, 6, 0),
            },
        };
        _grid.RowTemplate.Height = _density.RowHeight;
        SetDoubleBuffered(_grid);

        AddColumn(Headers[ColMark], ColumnWidths[ColMark], DataGridViewContentAlignment.MiddleLeft);
        AddColumn(Headers[ColName], ColumnWidths[ColName], DataGridViewContentAlignment.MiddleLeft);
        AddColumn(Headers[ColPid], ColumnWidths[ColPid], DataGridViewContentAlignment.MiddleLeft);
        AddColumn(Headers[ColCpu], ColumnWidths[ColCpu], DataGridViewContentAlignment.MiddleRight);
        AddColumn(Headers[ColRam], ColumnWidths[ColRam], DataGridViewContentAlignment.MiddleRight);
        AddColumn(Headers[ColStart], ColumnWidths[ColStart], DataGridViewContentAlignment.MiddleLeft);
        AddColumn(Headers[ColPath], ColumnWidths[ColPath], DataGridViewContentAlignment.MiddleLeft);

        UpdateHeaderGlyphs();

        _grid.CellValueNeeded += GridCellValueNeeded;
        _grid.CellFormatting += GridCellFormatting;
        _grid.ColumnHeaderMouseClick += GridHeaderClick;
        _grid.CellMouseDown += GridCellMouseDown;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.RowIndex < _view.Count) OpenLocation(ProcAt(e.RowIndex).Path); };
    }

    private void AddColumn(string title, int width, DataGridViewContentAlignment align)
    {
        int index = _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = title,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = align,
                BackColor = _theme.Bg,
                SelectionBackColor = _theme.Selection,
            },
        });
        _grid.Columns[index].HeaderCell.Style.Alignment = align;
        if (index == ColPath)
        {
            _grid.Columns[index].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns[index].MinimumWidth = 200;
        }
    }

    private void UpdateHeaderGlyphs()
    {
        if (_grid == null) return;
        for (int i = 0; i < _grid.Columns.Count && i < Headers.Length; i++)
        {
            string text = Headers[i];
            if (i == _sortCol && i != ColMark) text += _sortAsc ? " ↑" : " ↓";
            _grid.Columns[i].HeaderText = text;
        }
    }

    private Panel BuildHeader()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = _density.HeaderHeight,
            BackColor = _theme.Bg,
            Padding = new Padding(10, 10, 10, 4),
        };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = _theme.Bg };
        for (int i = 0; i < 5; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));

        table.Controls.Add(MakeCard("ПРОЦЕССОР", out _cpuValue, out _cpuSub), 0, 0);
        table.Controls.Add(MakeCard("ПАМЯТЬ", out _ramValue, out _ramSub), 1, 0);
        table.Controls.Add(MakeCard("НАГРЕВ", out _tempValue, out _tempSub), 2, 0);
        table.Controls.Add(MakeCard("ЭКРАН", out _displayValue, out _displaySub), 3, 0);
        table.Controls.Add(MakeCard("ПРОГРАММЫ", out _appsValue, out _appsSub), 4, 0);

        host.Controls.Add(table);
        return host;
    }

    private Panel MakeCard(string title, out Label value, out Label sub)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Surface,
            Margin = new Padding(4),
            Padding = _density.CardPadding,
        };

        int titleHeight = (int)Math.Ceiling(_density.CardTitleFont * 1.6) + 2;
        int subHeight = (int)Math.Ceiling(_density.CardSubFont * 1.6) + 2;

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = titleHeight,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", _density.CardTitleFont, FontStyle.Bold),
            AutoSize = false,
        };
        sub = new Label
        {
            Text = "—",
            Dock = DockStyle.Bottom,
            Height = subHeight,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", _density.CardSubFont),
            AutoSize = false,
        };
        value = new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            ForeColor = _theme.TextMain,
            Font = new Font("Segoe UI", _density.CardValueFont, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
        };

        card.Controls.Add(value);
        card.Controls.Add(sub);
        card.Controls.Add(titleLabel);
        return card;
    }

    private Panel BuildSparks(bool fill)
    {
        var host = new Panel
        {
            Dock = fill ? DockStyle.Fill : DockStyle.Top,
            Height = _density.SparkHeight,
            BackColor = _theme.Bg,
            Padding = new Padding(10, 4, 10, 6),
        };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _theme.Bg };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        _cpuSpark = new Sparkline
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Surface,
            LineColor = _theme.Accent,
            GridColor = _theme.SparkGrid,
            MutedColor = _theme.TextDim,
            Margin = new Padding(0),
        };
        _ramSpark = new Sparkline
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Surface,
            LineColor = _theme.RamLine,
            GridColor = _theme.SparkGrid,
            MutedColor = _theme.TextDim,
            Margin = new Padding(0),
        };

        table.Controls.Add(WrapSpark("Загрузка процессора", "2 минуты · 0–100 %", _cpuSpark), 0, 0);
        table.Controls.Add(WrapSpark("Заполнение памяти", "2 минуты · 0–100 %", _ramSpark), 1, 0);

        host.Controls.Add(table);
        return host;
    }

    private Panel WrapSpark(string caption, string hint, Sparkline spark)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Surface,
            Margin = new Padding(4),
            Padding = new Padding(10, 6, 10, 8),
        };

        var head = new Panel { Dock = DockStyle.Top, Height = 20, BackColor = _theme.Surface };
        var captionLabel = new Label
        {
            Text = caption,
            Dock = DockStyle.Left,
            Width = 210,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", _density.CardSubFont),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
        };
        var hintLabel = new Label
        {
            Text = hint,
            Dock = DockStyle.Right,
            Width = 150,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", _density.CardSubFont),
            TextAlign = ContentAlignment.MiddleRight,
            AutoSize = false,
        };
        head.Controls.Add(captionLabel);
        head.Controls.Add(hintLabel);

        panel.Controls.Add(spark);
        panel.Controls.Add(head);
        return panel;
    }

    private Panel BuildToolbar()
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = _density.ToolbarHeight,
            BackColor = _theme.Bg,
            Padding = new Padding(10, 8, 10, 8),
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = _theme.Bg };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var left = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = _theme.Bg, Margin = new Padding(0) };

        var searchHost = new Panel
        {
            Width = 268,
            Height = _density.SearchHeight,
            BackColor = _theme.InputBack,
            Padding = new Padding(8, Math.Max(3, (_density.SearchHeight - 20) / 2), 8, 0),
            Margin = new Padding(0),
        };
        searchHost.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.InputBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, searchHost.Width - 1, searchHost.Height - 1);
        };
        _search = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, PlaceholderText = "Поиск по имени или пути…" };
        StyleTextBox(_search);
        _search.TextChanged += (_, _) => ApplyFilter();
        searchHost.Controls.Add(_search);

        _onlyHeavy = new CheckBox
        {
            Text = "Только тяжёлые",
            AutoSize = true,
            ForeColor = _theme.TextMain,
            Margin = new Padding(14, Math.Max(3, (_density.ButtonHeight - 20) / 2), 0, 0),
        };
        _onlyHeavy.CheckedChanged += (_, _) => ApplyFilter();

        left.Controls.Add(searchHost);
        left.Controls.Add(_onlyHeavy);

        var right = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = _theme.Bg, Margin = new Padding(0) };
        var killButton = MakeButton("Завершить процесс", 168, kill: true);
        killButton.Click += (_, _) => KillSelected(false);
        var refreshButton = MakeButton("Обновить", 100, kill: false);
        refreshButton.Click += (_, _) => OnTick(null, EventArgs.Empty);
        right.Controls.Add(killButton);
        right.Controls.Add(refreshButton);

        table.Controls.Add(left, 0, 0);
        table.Controls.Add(new Panel { BackColor = _theme.Bg, Dock = DockStyle.Fill, Margin = new Padding(0) }, 1, 0);
        table.Controls.Add(right, 2, 0);

        host.Controls.Add(table);
        return host;
    }

    private Button MakeButton(string text, int width, bool kill)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, _density.ButtonHeight),
            FlatStyle = FlatStyle.Flat,
            BackColor = kill ? _theme.KillBack : _theme.ButtonBack,
            ForeColor = kill ? _theme.KillFore : _theme.ButtonFore,
            Margin = new Padding(0, 0, kill ? 8 : 0, 0),
            Font = new Font("Segoe UI", 8.75f),
        };
        button.FlatAppearance.BorderColor = kill ? _theme.KillBorder : _theme.ButtonBorder;
        button.FlatAppearance.MouseOverBackColor = kill ? _theme.KillHover : _theme.ButtonHover;
        button.FlatAppearance.MouseDownBackColor = kill ? _theme.KillHover : _theme.ButtonHover;
        return button;
    }

    private Panel BuildStatus()
    {
        var host = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = _theme.StatusBack };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
        };
        host.Controls.Add(_statusLabel);
        return host;
    }

    private MenuStrip BuildMenuBar()
    {
        var menu = new MenuStrip
        {
            Name = "interfaceMenu",
            Dock = DockStyle.Top,
            BackColor = _theme.MenuBack,
            ForeColor = _theme.MenuText,
            Renderer = new UiMenuRenderer(_theme),
            GripStyle = ToolStripGripStyle.Hidden,
            CanOverflow = false,
            ShowItemToolTips = false,
            AutoSize = false,
            Height = _density.MenuHeight,
            Padding = new Padding(8, 2, 10, 2),
            Font = new Font("Segoe UI", 9f),
        };

        menu.Items.Add(BuildInterfaceMenu());
        menu.Items.Add(BuildHelpMenu());
        menu.Items.Add(new ToolStripLabel($"{_theme.Title} · {_density.Title} · {_layout.Title}")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = _theme.TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0, 5, 4, 0),
        });

        return menu;
    }

    private ToolStripMenuItem BuildInterfaceMenu()
    {
        var root = new ToolStripMenuItem("Интерфейс");

        var themes = new ToolStripMenuItem("Тема оформления");
        foreach (var theme in UiTheme.All)
        {
            var item = new ToolStripMenuItem(theme.Title) { Checked = theme.Id == _theme.Id, CheckOnClick = false };
            item.Click += (_, _) => RequestInterface(themeId: theme.Id);
            themes.DropDownItems.Add(item);
        }

        var densities = new ToolStripMenuItem("Плотность");
        foreach (var density in UiDensity.All)
        {
            var item = new ToolStripMenuItem(density.Title) { Checked = density.Id == _density.Id, CheckOnClick = false };
            item.Click += (_, _) => RequestInterface(densityId: density.Id);
            densities.DropDownItems.Add(item);
        }

        var layouts = new ToolStripMenuItem("Состав экрана");
        foreach (var layout in UiLayout.All)
        {
            var item = new ToolStripMenuItem(layout.Title) { Checked = layout.Id == _layout.Id, CheckOnClick = false };
            item.Click += (_, _) => RequestInterface(layoutId: layout.Id);
            layouts.DropDownItems.Add(item);
        }

        root.DropDownItems.Add(themes);
        root.DropDownItems.Add(densities);
        root.DropDownItems.Add(layouts);
        root.DropDownItems.Add(new ToolStripSeparator());

        var reset = new ToolStripMenuItem("Сбросить оформление");
        reset.Click += (_, _) => RequestInterface(UiTheme.Graphite.Id, UiDensity.Normal.Id, UiLayout.Full.Id);
        root.DropDownItems.Add(reset);

        return root;
    }

    private ToolStripMenuItem BuildHelpMenu()
    {
        var help = new ToolStripMenuItem("Справка");

        var keys = new ToolStripMenuItem("Горячие клавиши");
        keys.Click += (_, _) => MessageBox.Show(
            this,
            "F5 — обновить данные\n" +
            "Delete — завершить выбранные процессы\n" +
            "Двойной клик — открыть папку с файлом\n" +
            "Правый клик — меню процесса\n\n" +
            "Верхнее меню «Интерфейс» переключает тему, плотность и состав экрана.",
            "Горячие клавиши",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        help.DropDownItems.Add(keys);

        var about = new ToolStripMenuItem("О программе");
        about.Click += (_, _) => MessageBox.Show(
            this,
            "System Monitor 1.1\n\n" +
            "Процессор, память, температура, экран и пользовательские программы.\n" +
            "Графики за последние 2 минуты обновляются раз в секунду.",
            "О программе",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        help.DropDownItems.Add(about);

        return help;
    }

    private void StyleTextBox(TextBox box)
    {
        box.BackColor = _theme.InputBack;
        box.ForeColor = _theme.TextMain;
    }

    private static void SetDoubleBuffered(Control control)
    {
        try
        {
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(control, true);
        }
        catch { }
    }

    private void RequestInterface(string themeId = null, string densityId = null, string layoutId = null, bool persist = true)
    {
        if (themeId != null) _themeId = themeId;
        if (densityId != null) _densityId = densityId;
        if (layoutId != null) _layoutId = layoutId;
        NormalizeInterfaceIds();
        if (persist) SaveSettings();

        if (!IsHandleCreated)
        {
            RebuildUi();
            return;
        }
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        try
        {
            BeginInvoke(new Action(() => { _rebuildQueued = false; RebuildUi(); }));
        }
        catch
        {
            _rebuildQueued = false;
            RebuildUi();
        }
    }

    private void NormalizeInterfaceIds()
    {
        _theme = UiTheme.Get(_themeId);
        _themeId = _theme.Id;
        _density = UiDensity.Get(_densityId);
        _densityId = _density.Id;
        _layout = UiLayout.Get(_layoutId);
        _layoutId = _layout.Id;
    }

    private void SaveSettings()
    {
        _settings.Theme = _themeId;
        _settings.Density = _densityId;
        _settings.Layout = _layoutId;
        _settings.Save();
    }

    /// <summary>Rebuilds all controls for the current theme, density and layout.</summary>
    private void RebuildUi()
    {
        if (IsDisposed || Disposing) return;

        string query = _search?.Text ?? "";
        bool onlyHeavy = _onlyHeavy?.Checked ?? false;
        var selected = _grid != null ? SelectedPids() : new HashSet<int>();
        int scroll = _grid != null ? SafeScrollIndex() : -1;

        SuspendLayout();
        var oldControls = new List<Control>();
        foreach (Control control in Controls) oldControls.Add(control);
        Controls.Clear();

        _grid = null;
        _search = null;
        _onlyHeavy = null;
        _statusLabel = null;
        _cpuSpark = null;
        _ramSpark = null;
        _menu = null;
        _headerHost = null;
        _sparksHost = null;
        _toolbarHost = null;

        foreach (Control control in oldControls) control.Dispose();

        BuildUi();

        if (_search != null) _search.Text = query;
        if (_onlyHeavy != null) _onlyHeavy.Checked = onlyHeavy;

        _cpuSpark?.SetData(_cpuHistory);
        _ramSpark?.SetData(_ramHistory);

        ApplyFilter();
        UpdateCards();

        if (_grid != null)
        {
            if (selected.Count > 0)
            {
                _grid.ClearSelection();
                foreach (int pid in selected)
                {
                    int index = _view.FindIndex(row => row.Pid == pid);
                    if (index >= 0) _grid.Rows[index].Selected = true;
                }
            }
            if (scroll >= 0 && _grid.RowCount > 0)
            {
                try { _grid.FirstDisplayedScrollingRowIndex = Math.Min(scroll, _grid.RowCount - 1); } catch { }
            }
        }

        ResumeLayout(true);
        ApplyWindowChrome();
        Invalidate(true);
    }

    private void ApplyWindowChrome()
    {
        if (!IsHandleCreated) return;
        try
        {
            int dark = _theme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) == 0)
            {
                // Re-apply the frame so the title bar repaints immediately after a theme switch.
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            }
        }
        catch
        {
            // Older Windows builds simply keep the default title bar.
        }
    }

    internal void ApplyInterfaceForTest(string themeId, string densityId, string layoutId)
    {
        _themeId = themeId ?? _themeId;
        _densityId = densityId ?? _densityId;
        _layoutId = layoutId ?? _layoutId;
        NormalizeInterfaceIds();
        RebuildUi();
    }

    /// <summary>Exercises the same deferred rebuild path as a real menu click.</summary>
    internal void SelectInterfaceForTest(string themeId, string densityId, string layoutId)
    {
        RequestInterface(themeId, densityId, layoutId, persist: false);
    }

    internal void ShowInterfaceMenuForTest(bool openThemeSubmenu = false)
    {
        if (_menu != null && _menu.Items.Count > 0 && _menu.Items[0] is ToolStripMenuItem item)
        {
            item.ShowDropDown();
            if (openThemeSubmenu && item.DropDownItems.Count > 0 && item.DropDownItems[0] is ToolStripMenuItem themes)
            {
                themes.ShowDropDown();
            }
        }
    }

    internal void HideInterfaceMenuForTest()
    {
        try
        {
            if (_menu == null) return;
            foreach (ToolStripItem item in _menu.Items)
            {
                if (item is not ToolStripMenuItem menuItem) continue;
                foreach (ToolStripItem sub in menuItem.DropDownItems)
                {
                    if (sub is ToolStripMenuItem subItem) subItem.HideDropDown();
                }
                menuItem.HideDropDown();
            }
        }
        catch { }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            RefreshView(_sampler.Sample());
            PushHistory();
            UpdateCards();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Charts always keep the full 2-minute history, even while the charts panel is hidden,
    /// so switching interface layouts does not reset the graphs.
    /// </summary>
    private void PushHistory()
    {
        double total = _sampler.MemTotal;
        double used = total - _sampler.MemAvail;
        double ramPercent = total > 0 ? used / total * 100 : 0;

        AppendHistory(_cpuHistory, _sampler.TotalCpu);
        AppendHistory(_ramHistory, ramPercent);

        _cpuSpark?.SetData(_cpuHistory);
        _ramSpark?.SetData(_ramHistory);
    }

    private static void AppendHistory(List<float> history, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        history.Add((float)Math.Clamp(value, 0, 100));
        int extra = history.Count - HistoryCapacity;
        if (extra > 0) history.RemoveRange(0, extra);
    }

    private void RefreshView(List<ProcRow> sampled)
    {
        _totalProcesses = sampled.Count;
        _userApps.Clear();
        foreach (var row in sampled)
        {
            if (!IsUserApp(row)) continue;
            row.Heavy = row.Cpu >= HeavyCpu || row.WorkingSet >= HeavyRam;
            _userApps.Add(row);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var previousSelection = SelectedPids();
        int previousScroll = SafeScrollIndex();

        string query = (_search?.Text ?? "").Trim();
        bool onlyHeavy = _onlyHeavy?.Checked ?? false;

        _view.Clear();
        foreach (var row in _userApps)
        {
            if (onlyHeavy && !row.Heavy) continue;
            if (query.Length > 0 &&
                row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                row.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _view.Add(row);
        }

        SortView();
        UpdateHeaderGlyphs();

        if (_grid != null) Rebind(previousSelection, previousScroll);
    }

    private void Rebind(HashSet<int> previousSelection, int previousScroll)
    {
        if (_grid == null) return;

        int count = _view.Count;
        if (_grid.RowCount != count) _grid.RowCount = count;

        if (previousSelection != null && previousSelection.Count > 0)
        {
            _grid.ClearSelection();
            foreach (int pid in previousSelection)
            {
                int idx = _view.FindIndex(r => r.Pid == pid);
                if (idx >= 0) _grid.Rows[idx].Selected = true;
            }
        }

        if (previousScroll >= 0 && count > 0)
        {
            try { _grid.FirstDisplayedScrollingRowIndex = Math.Min(previousScroll, count - 1); } catch { }
        }

        _grid.Invalidate();
    }

    private int SafeScrollIndex()
    {
        if (_grid == null) return -1;
        try { return _grid.RowCount > 0 ? _grid.FirstDisplayedScrollingRowIndex : -1; }
        catch { return -1; }
    }

    private static bool DefaultAscending(int column) => column is ColName or ColPid or ColPath;

    private void SortView()
    {
        // Comparators always describe ascending order; the arrow/status direction is applied below.
        Comparison<ProcRow> cmp;
        switch (_sortCol)
        {
            case ColName: cmp = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
            case ColPid: cmp = (a, b) => a.Pid.CompareTo(b.Pid); break;
            case ColRam: cmp = (a, b) => a.WorkingSet.CompareTo(b.WorkingSet); break;
            case ColStart: cmp = (a, b) => a.Start.CompareTo(b.Start); break;
            case ColPath: cmp = (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase); break;
            default: cmp = (a, b) => a.Cpu.CompareTo(b.Cpu); break;
        }
        _view.Sort((a, b) =>
        {
            int result = _sortAsc ? cmp(a, b) : cmp(b, a);
            return result != 0 ? result : a.Pid.CompareTo(b.Pid);
        });
    }

    private void UpdateCards()
    {
        double cpu = _sampler.TotalCpu;
        double totalGb = _sampler.MemTotal / 1073741824.0;
        double usedGb = (_sampler.MemTotal - _sampler.MemAvail) / 1073741824.0;
        double ramPercent = totalGb > 0 ? usedGb / totalGb * 100 : 0;

        if (_cpuValue != null)
        {
            _cpuValue.Text = cpu.ToString("0") + " %";
            _cpuValue.ForeColor = cpu >= 80 ? _theme.Danger : cpu >= 50 ? _theme.Warn : _theme.TextMain;
            _cpuSub.Text = $"{_sampler.Cores} потоков · 1 с";
        }

        if (_ramValue != null)
        {
            _ramValue.Text = $"{usedGb:0.0} / {totalGb:0.0} ГБ";
            _ramValue.ForeColor = ramPercent >= 90 ? _theme.Danger : ramPercent >= 75 ? _theme.Warn : _theme.TextMain;
            _ramSub.Text = $"{ramPercent:0}% занято";
        }

        if (_tempValue != null)
        {
            float cpuTemp = _hw.CpuTemp;
            float gpuTemp = _hw.GpuTemp;
            _tempValue.Text = float.IsNaN(cpuTemp) && float.IsNaN(gpuTemp) ? "н/д" : $"{(float.IsNaN(cpuTemp) ? gpuTemp : cpuTemp):0} °C";
            _tempValue.ForeColor = !float.IsNaN(cpuTemp) && cpuTemp >= 90 ? _theme.Danger
                : !float.IsNaN(cpuTemp) && cpuTemp >= 75 ? _theme.Warn
                : _theme.TextMain;
            _tempSub.Text = float.IsNaN(cpuTemp) && float.IsNaN(gpuTemp)
                ? _hw.Status
                : $"CPU {(float.IsNaN(cpuTemp) ? "н/д" : cpuTemp.ToString("0") + "°")} · GPU {(float.IsNaN(gpuTemp) ? "н/д" : gpuTemp.ToString("0") + "°")}";
        }

        if (_displayValue != null)
        {
            if (DateTime.UtcNow - _lastDisplayCheck > TimeSpan.FromSeconds(5))
            {
                _displayText = DisplayInfo.ResolutionsText();
                _displayHz = DisplayInfo.PrimaryHz();
                _lastDisplayCheck = DateTime.UtcNow;
            }
            _displayValue.Text = _displayHz > 0 ? _displayHz + " Гц" : "н/д";
            _displaySub.Text = _displayText;
        }

        int heavyCount = _userApps.Count(r => r.Heavy);
        if (_appsValue != null)
        {
            _appsValue.Text = _userApps.Count.ToString();
            _appsSub.Text = heavyCount > 0 ? $"тяжёлых: {heavyCount}" : "приложений без служб";
            _appsSub.ForeColor = heavyCount > 0 ? _theme.Warn : _theme.TextDim;
        }

        if (_statusLabel != null)
        {
            if (!_layout.ShowTable)
            {
                _statusLabel.Text = $"Приложений: {_userApps.Count}   ·   Тяжёлых: {heavyCount}   ·   Процессов: {_totalProcesses}   ·   Режим: {_layout.Title}";
                return;
            }

            string sortName = _sortCol switch
            {
                ColName => "имени",
                ColPid => "PID",
                ColRam => "памяти",
                ColStart => "времени запуска",
                ColPath => "пути",
                _ => "CPU",
            };
            _statusLabel.Text = $"В списке: {_view.Count}   ·   Приложений: {_userApps.Count}   ·   Тяжёлых: {heavyCount}   ·   Процессов: {_totalProcesses}   ·   Сортировка: {sortName}{(_sortAsc ? " ↑" : " ↓")}   ·   Delete — завершить";
        }
    }

    private bool IsUserApp(ProcRow row)
    {
        if (row.Pid == Environment.ProcessId) return false;
        if (row.Session <= 0) return false;
        if (string.IsNullOrWhiteSpace(row.Path)) return false;
        if (SystemNames.Contains(row.Name)) return false;
        if (row.Path.StartsWith(_windowsDir, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private ProcRow ProcAt(int index) => _view[index];

    private HashSet<int> SelectedPids()
    {
        var set = new HashSet<int>();
        if (_grid == null) return set;
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.Index >= 0 && row.Index < _view.Count) set.Add(_view[row.Index].Pid);
        }
        return set;
    }

    private void GridCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        var row = _view[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            ColMark => row.Heavy ? "●" : "",
            ColName => row.Name,
            ColPid => row.Pid,
            ColCpu => row.Cpu.ToString("0.0"),
            ColRam => (row.WorkingSet / 1048576.0).ToString("0"),
            ColStart => row.Start == default ? "" : row.Start.ToString("dd.MM HH:mm"),
            ColPath => row.Path,
            _ => "",
        };
    }

    private void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        var row = _view[e.RowIndex];

        e.CellStyle.BackColor = row.Heavy ? _theme.HeavyRow : _theme.Bg;
        e.CellStyle.SelectionBackColor = _theme.Selection;
        e.CellStyle.SelectionForeColor = _theme.SelectionText;
        e.CellStyle.ForeColor = _theme.TextMain;

        if (e.ColumnIndex == ColMark)
        {
            e.CellStyle.ForeColor = row.Heavy ? _theme.Danger : e.CellStyle.BackColor;
        }
        else if (e.ColumnIndex == ColCpu)
        {
            e.CellStyle.ForeColor = row.Cpu >= 50 ? _theme.Danger : row.Cpu >= HeavyCpu ? _theme.Warn : _theme.TextMain;
        }
        else if (e.ColumnIndex == ColName)
        {
            e.CellStyle.ForeColor = row.Heavy ? _theme.HeavyName : _theme.TextMain;
        }
        else if (e.ColumnIndex == ColStart || e.ColumnIndex == ColPath)
        {
            e.CellStyle.ForeColor = _theme.TextDim;
        }
    }

    private void GridHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.ColumnIndex == ColMark) return;
        if (_sortCol == e.ColumnIndex) _sortAsc = !_sortAsc;
        else
        {
            _sortCol = e.ColumnIndex;
            _sortAsc = DefaultAscending(e.ColumnIndex);
        }
        UpdateHeaderGlyphs();
        ApplyFilter();
        UpdateCards();
    }

    private void GridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
        if (!_grid.Rows[e.RowIndex].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;
        }

        var row = _view[e.RowIndex];
        var menu = new ContextMenuStrip
        {
            Renderer = new UiMenuRenderer(_theme),
            BackColor = _theme.MenuBack,
            ForeColor = _theme.MenuText,
            ShowImageMargin = false,
        };
        menu.Items.Add("Завершить процесс", null, (_, _) => KillSelected(false));
        menu.Items.Add("Завершить дерево процессов", null, (_, _) => KillSelected(true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Открыть расположение файла", null, (_, _) => OpenLocation(row.Path));
        menu.Items.Add("Скопировать путь", null, (_, _) =>
        {
            try { Clipboard.SetText(row.Path); _statusLabel.Text = "Путь скопирован: " + row.Path; } catch { }
        });
        menu.Items.Add("Свойства файла", null, (_, _) => OpenProperties(row.Path));
        menu.Closed += (_, _) =>
        {
            try { BeginInvoke(new Action(() => menu.Dispose())); }
            catch { menu.Dispose(); }
        };
        menu.Show(_grid, _grid.PointToClient(Cursor.Position));
    }

    private void KillSelected(bool tree)
    {
        if (_grid == null) return;

        var pids = SelectedPids();
        if (pids.Count == 0)
        {
            _statusLabel.Text = "Ничего не выбрано — выберите процесс в таблице.";
            return;
        }

        var names = string.Join(", ", _view.Where(r => pids.Contains(r.Pid)).Select(r => $"{r.Name} ({r.Pid})"));
        string action = tree ? "Завершить дерево процессов" : "Завершить процесс";
        var answer = MessageBox.Show(this, $"{action}:\n{names}", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        int killed = 0, failed = 0;
        string lastError = "";
        foreach (int pid in pids)
        {
            if (pid == Environment.ProcessId) continue;
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(tree);
                killed++;
            }
            catch (Exception ex)
            {
                failed++;
                lastError = ex.Message;
            }
        }

        _statusLabel.Text = failed == 0
            ? $"Завершено процессов: {killed}"
            : $"Завершено: {killed}, ошибок: {failed} — {lastError}";

        RefreshView(_sampler.Sample());
        UpdateCards();
    }

    private void OpenLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            _statusLabel.Text = "Путь недоступен: " + path;
            return;
        }
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { _statusLabel.Text = "Не удалось открыть: " + ex.Message; }
    }

    private void OpenProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    internal int ViewCount => _view.Count;
    internal int AppCount => _userApps.Count;
    internal int HeavyCount => _userApps.Count(r => r.Heavy);

    internal string DebugSnapshot()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ui={_theme.Id}/{_density.Id}/{_layout.Id} sparkCpu={_cpuSpark?.Count ?? 0} sparkRam={_ramSpark?.Count ?? 0}");
        sb.AppendLine($"cpu={_sampler.TotalCpu:0.0} ram={(_sampler.MemTotal - _sampler.MemAvail) / 1073741824.0:0.0}/{_sampler.MemTotal / 1073741824.0:0.0}GB");
        sb.AppendLine("display=" + (_displayValue?.Text ?? "-") + " | " + (_displaySub?.Text ?? "-"));
        sb.AppendLine("temp=" + (_tempValue?.Text ?? "-") + " | " + (_tempSub?.Text ?? "-"));
        sb.AppendLine("hwOk=" + _hw.Ok + " status=" + _hw.Status);
        foreach (var row in _view.Take(8))
        {
            sb.AppendLine($"  {row.Name} pid={row.Pid} cpu={row.Cpu:0.0} ram={row.WorkingSet / 1048576.0:0}MB heavy={row.Heavy} {row.Path}");
        }
        return sb.ToString();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowChrome();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        _hw.Dispose();
        base.OnFormClosed(e);
    }
}
