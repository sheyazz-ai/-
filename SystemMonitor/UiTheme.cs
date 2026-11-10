using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace SystemMonitor;

/// <summary>Color palette for one interface theme.</summary>
internal sealed class UiTheme
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public bool Dark { get; init; }

    public Color Bg { get; init; }
    public Color Surface { get; init; }
    public Color TextMain { get; init; }
    public Color TextDim { get; init; }
    public Color Accent { get; init; }
    public Color Danger { get; init; }
    public Color Warn { get; init; }

    public Color Selection { get; init; }
    public Color SelectionText { get; init; }
    public Color GridLine { get; init; }
    public Color HeaderBack { get; init; }
    public Color HeaderText { get; init; }
    public Color StatusBack { get; init; }
    public Color InputBack { get; init; }
    public Color InputBorder { get; init; }
    public Color HeavyRow { get; init; }
    public Color HeavyName { get; init; }

    public Color MenuBack { get; init; }
    public Color MenuText { get; init; }
    public Color MenuHover { get; init; }
    public Color MenuBorder { get; init; }
    public Color SparkGrid { get; init; }
    public Color RamLine { get; init; }

    public Color ButtonBack => Dark ? Blend(Surface, Accent, 0.06) : Blend(Surface, Accent, 0.05);
    public Color ButtonHover => Dark ? Blend(Surface, Accent, 0.16) : Blend(Surface, Accent, 0.14);
    public Color ButtonBorder => Dark ? Blend(Surface, Accent, 0.22) : Blend(Surface, Accent, 0.28);
    public Color ButtonFore => TextMain;

    public Color KillBack => Dark ? Blend(Surface, Danger, 0.10) : Blend(Surface, Danger, 0.08);
    public Color KillHover => Dark ? Blend(Surface, Danger, 0.22) : Blend(Surface, Danger, 0.16);
    public Color KillBorder => Dark ? Blend(Surface, Danger, 0.48) : Blend(Surface, Danger, 0.40);
    public Color KillFore => Dark ? Blend(Danger, Color.White, 0.35) : Blend(Danger, Color.Black, 0.30);

    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static readonly UiTheme Graphite = new()
    {
        Id = "graphite", Title = "Графит", Dark = true,
        Bg = Color.FromArgb(22, 24, 29),
        Surface = Color.FromArgb(31, 35, 43),
        TextMain = Color.FromArgb(230, 233, 239),
        TextDim = Color.FromArgb(138, 147, 167),
        Accent = Color.FromArgb(76, 154, 255),
        Danger = Color.FromArgb(255, 110, 100),
        Warn = Color.FromArgb(255, 190, 90),
        Selection = Color.FromArgb(40, 62, 96),
        SelectionText = Color.FromArgb(235, 240, 250),
        GridLine = Color.FromArgb(38, 42, 52),
        HeaderBack = Color.FromArgb(28, 31, 39),
        HeaderText = Color.FromArgb(138, 147, 167),
        StatusBack = Color.FromArgb(18, 20, 24),
        InputBack = Color.FromArgb(34, 38, 47),
        InputBorder = Color.FromArgb(64, 72, 88),
        HeavyRow = Color.FromArgb(38, 28, 32),
        HeavyName = Color.FromArgb(255, 170, 160),
        MenuBack = Color.FromArgb(28, 31, 39),
        MenuText = Color.FromArgb(214, 220, 232),
        MenuHover = Color.FromArgb(44, 50, 62),
        MenuBorder = Color.FromArgb(52, 60, 74),
        SparkGrid = Color.FromArgb(42, 47, 58),
        RamLine = Color.FromArgb(120, 200, 140),
    };

    public static readonly UiTheme Light = new()
    {
        Id = "light", Title = "Светлая", Dark = false,
        Bg = Color.FromArgb(243, 245, 249),
        Surface = Color.White,
        TextMain = Color.FromArgb(26, 32, 42),
        TextDim = Color.FromArgb(102, 112, 130),
        Accent = Color.FromArgb(32, 105, 210),
        Danger = Color.FromArgb(198, 52, 42),
        Warn = Color.FromArgb(164, 104, 0),
        Selection = Color.FromArgb(207, 224, 250),
        SelectionText = Color.FromArgb(18, 28, 45),
        GridLine = Color.FromArgb(225, 229, 237),
        HeaderBack = Color.FromArgb(236, 239, 245),
        HeaderText = Color.FromArgb(95, 105, 123),
        StatusBack = Color.FromArgb(232, 235, 242),
        InputBack = Color.White,
        InputBorder = Color.FromArgb(198, 205, 218),
        HeavyRow = Color.FromArgb(255, 238, 235),
        HeavyName = Color.FromArgb(180, 45, 35),
        MenuBack = Color.White,
        MenuText = Color.FromArgb(34, 40, 50),
        MenuHover = Color.FromArgb(233, 238, 247),
        MenuBorder = Color.FromArgb(208, 214, 225),
        SparkGrid = Color.FromArgb(224, 229, 238),
        RamLine = Color.FromArgb(44, 150, 94),
    };

    public static readonly UiTheme Midnight = new()
    {
        Id = "midnight", Title = "Полночь", Dark = true,
        Bg = Color.FromArgb(11, 14, 22),
        Surface = Color.FromArgb(19, 24, 35),
        TextMain = Color.FromArgb(222, 230, 244),
        TextDim = Color.FromArgb(118, 132, 160),
        Accent = Color.FromArgb(82, 166, 255),
        Danger = Color.FromArgb(255, 100, 112),
        Warn = Color.FromArgb(255, 196, 92),
        Selection = Color.FromArgb(30, 54, 92),
        SelectionText = Color.FromArgb(235, 243, 255),
        GridLine = Color.FromArgb(32, 39, 54),
        HeaderBack = Color.FromArgb(15, 19, 29),
        HeaderText = Color.FromArgb(118, 132, 160),
        StatusBack = Color.FromArgb(8, 10, 16),
        InputBack = Color.FromArgb(26, 32, 46),
        InputBorder = Color.FromArgb(50, 61, 82),
        HeavyRow = Color.FromArgb(40, 24, 34),
        HeavyName = Color.FromArgb(255, 150, 160),
        MenuBack = Color.FromArgb(15, 19, 29),
        MenuText = Color.FromArgb(210, 220, 238),
        MenuHover = Color.FromArgb(30, 40, 58),
        MenuBorder = Color.FromArgb(44, 54, 74),
        SparkGrid = Color.FromArgb(33, 40, 55),
        RamLine = Color.FromArgb(110, 205, 160),
    };

    public static readonly UiTheme Contrast = new()
    {
        Id = "contrast", Title = "Контраст", Dark = true,
        Bg = Color.Black,
        Surface = Color.FromArgb(14, 14, 14),
        TextMain = Color.White,
        TextDim = Color.FromArgb(190, 190, 190),
        Accent = Color.FromArgb(255, 205, 0),
        Danger = Color.FromArgb(255, 74, 74),
        Warn = Color.FromArgb(255, 205, 0),
        Selection = Color.FromArgb(70, 58, 0),
        SelectionText = Color.White,
        GridLine = Color.FromArgb(62, 62, 62),
        HeaderBack = Color.FromArgb(8, 8, 8),
        HeaderText = Color.FromArgb(235, 235, 235),
        StatusBack = Color.Black,
        InputBack = Color.FromArgb(18, 18, 18),
        InputBorder = Color.FromArgb(130, 130, 130),
        HeavyRow = Color.FromArgb(45, 0, 0),
        HeavyName = Color.FromArgb(255, 160, 160),
        MenuBack = Color.Black,
        MenuText = Color.FromArgb(245, 245, 245),
        MenuHover = Color.FromArgb(55, 55, 55),
        MenuBorder = Color.FromArgb(130, 130, 130),
        SparkGrid = Color.FromArgb(70, 70, 70),
        RamLine = Color.FromArgb(0, 230, 170),
    };

    public static readonly IReadOnlyList<UiTheme> All = new[] { Graphite, Light, Midnight, Contrast };

    public static UiTheme Get(string id)
    {
        foreach (var theme in All)
        {
            if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) return theme;
        }
        return Graphite;
    }
}

/// <summary>Row, card and chart sizes for the selected interface density.</summary>
internal sealed class UiDensity
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public float BaseFont { get; init; }
    public float CardTitleFont { get; init; }
    public float CardValueFont { get; init; }
    public float CardSubFont { get; init; }
    public float GridFont { get; init; }
    public int HeaderHeight { get; init; }
    public int SparkHeight { get; init; }
    public int ToolbarHeight { get; init; }
    public int RowHeight { get; init; }
    public int GridHeaderHeight { get; init; }
    public int MenuHeight { get; init; }
    public int SearchHeight { get; init; }
    public int ButtonHeight { get; init; }
    public Padding CardPadding { get; init; }

    public static readonly UiDensity Compact = new()
    {
        Id = "compact", Title = "Компактная",
        BaseFont = 8.5f, CardTitleFont = 7.5f, CardValueFont = 15f, CardSubFont = 8f, GridFont = 8.5f,
        HeaderHeight = 96, SparkHeight = 96, ToolbarHeight = 46, RowHeight = 22, GridHeaderHeight = 28,
        MenuHeight = 28, SearchHeight = 28, ButtonHeight = 28, CardPadding = new Padding(12, 6, 12, 6),
    };

    public static readonly UiDensity Normal = new()
    {
        Id = "normal", Title = "Обычная",
        BaseFont = 9f, CardTitleFont = 8f, CardValueFont = 19f, CardSubFont = 8.5f, GridFont = 9f,
        HeaderHeight = 116, SparkHeight = 122, ToolbarHeight = 52, RowHeight = 26, GridHeaderHeight = 32,
        MenuHeight = 30, SearchHeight = 30, ButtonHeight = 30, CardPadding = new Padding(12, 8, 12, 8),
    };

    public static readonly UiDensity Spacious = new()
    {
        Id = "spacious", Title = "Просторная",
        BaseFont = 9.5f, CardTitleFont = 8.5f, CardValueFont = 23f, CardSubFont = 9f, GridFont = 9.5f,
        HeaderHeight = 136, SparkHeight = 156, ToolbarHeight = 58, RowHeight = 30, GridHeaderHeight = 36,
        MenuHeight = 32, SearchHeight = 34, ButtonHeight = 34, CardPadding = new Padding(14, 10, 14, 10),
    };

    public static readonly IReadOnlyList<UiDensity> All = new[] { Compact, Normal, Spacious };

    public static UiDensity Get(string id)
    {
        foreach (var density in All)
        {
            if (string.Equals(density.Id, id, StringComparison.OrdinalIgnoreCase)) return density;
        }
        return Normal;
    }
}

/// <summary>Which panels are visible in the selected interface layout.</summary>
internal sealed class UiLayout
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public bool ShowCards { get; init; }
    public bool ShowCharts { get; init; }
    public bool ShowTable { get; init; }
    public bool ShowToolbar { get; init; }
    public bool ChartsFill { get; init; }

    public static readonly UiLayout Full = new()
    {
        Id = "full", Title = "Полный", ShowCards = true, ShowCharts = true, ShowTable = true, ShowToolbar = true,
    };

    public static readonly UiLayout NoCharts = new()
    {
        Id = "nocharts", Title = "Без графиков", ShowCards = true, ShowCharts = false, ShowTable = true, ShowToolbar = true,
    };

    public static readonly UiLayout TableOnly = new()
    {
        Id = "table", Title = "Только список", ShowCards = false, ShowCharts = false, ShowTable = true, ShowToolbar = true,
    };

    public static readonly UiLayout Dashboard = new()
    {
        Id = "dashboard", Title = "Дашборд", ShowCards = true, ShowCharts = true, ShowTable = false, ShowToolbar = false, ChartsFill = true,
    };

    public static readonly IReadOnlyList<UiLayout> All = new[] { Full, NoCharts, TableOnly, Dashboard };

    public static UiLayout Get(string id)
    {
        foreach (var layout in All)
        {
            if (string.Equals(layout.Id, id, StringComparison.OrdinalIgnoreCase)) return layout;
        }
        return Full;
    }
}

/// <summary>Persisted choice of interface theme, density and layout.</summary>
internal sealed class UiSettings
{
    public string Theme { get; set; } = UiTheme.Graphite.Id;
    public string Density { get; set; } = UiDensity.Normal.Id;
    public string Layout { get; set; } = UiLayout.Full.Id;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SystemMonitor", "ui.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath));
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Broken or unreadable settings should never block startup.
        }
        return new UiSettings();
    }

    public void Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings are a convenience only.
        }
    }
}
