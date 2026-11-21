using System.Windows;
using System.Windows.Media;

namespace FreeCleaner.Services;

public static class ThemeManagerService
{
    public static readonly string[] AvailableThemes =
    [
        "ObsidianNeon",
        "MidnightViolet",
        "EmeraldMatrix",
        "FluentLight"
    ];

    private sealed record ThemePalette(
        Color BgPrimary,
        Color BgSecondary,
        Color BgCard,
        Color BgCardHover,
        Color BorderSubtle,
        Color AccentBlue,
        Color AccentBlueHover,
        Color AccentEmerald,
        Color AccentEmeraldHover,
        Color DangerRed,
        Color WarningAmber,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color SidebarTop,
        Color SidebarBottom,
        Color CardGradStart,
        Color CardGradEnd,
        Color PrimaryGradStart,
        Color PrimaryGradMid,
        Color PrimaryGradEnd);

    private static readonly Dictionary<string, ThemePalette> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ObsidianNeon"] = new ThemePalette(
            BgPrimary: ParseColor("#090D16"),
            BgSecondary: ParseColor("#0F1624"),
            BgCard: ParseColor("#151E31"),
            BgCardHover: ParseColor("#1E2B45"),
            BorderSubtle: ParseColor("#24324D"),
            AccentBlue: ParseColor("#38BDF8"),
            AccentBlueHover: ParseColor("#0284C7"),
            AccentEmerald: ParseColor("#10B981"),
            AccentEmeraldHover: ParseColor("#059669"),
            DangerRed: ParseColor("#F43F5E"),
            WarningAmber: ParseColor("#F59E0B"),
            TextPrimary: ParseColor("#F8FAFC"),
            TextSecondary: ParseColor("#94A3B8"),
            TextMuted: ParseColor("#64748B"),
            SidebarTop: ParseColor("#0D1322"),
            SidebarBottom: ParseColor("#090D16"),
            CardGradStart: ParseColor("#172237"),
            CardGradEnd: ParseColor("#11192B"),
            PrimaryGradStart: ParseColor("#2563EB"),
            PrimaryGradMid: ParseColor("#4F46E5"),
            PrimaryGradEnd: ParseColor("#06B6D4")),

        ["MidnightViolet"] = new ThemePalette(
            BgPrimary: ParseColor("#0B0917"),
            BgSecondary: ParseColor("#130F26"),
            BgCard: ParseColor("#1B1536"),
            BgCardHover: ParseColor("#261E4A"),
            BorderSubtle: ParseColor("#332860"),
            AccentBlue: ParseColor("#A78BFA"),
            AccentBlueHover: ParseColor("#7C3AED"),
            AccentEmerald: ParseColor("#2DD4BF"),
            AccentEmeraldHover: ParseColor("#0D9488"),
            DangerRed: ParseColor("#FB7185"),
            WarningAmber: ParseColor("#FBBF24"),
            TextPrimary: ParseColor("#F5F3FF"),
            TextSecondary: ParseColor("#C4B5FD"),
            TextMuted: ParseColor("#8B7EC8"),
            SidebarTop: ParseColor("#110D22"),
            SidebarBottom: ParseColor("#0A0815"),
            CardGradStart: ParseColor("#211942"),
            CardGradEnd: ParseColor("#16112C"),
            PrimaryGradStart: ParseColor("#7C3AED"),
            PrimaryGradMid: ParseColor("#9333EA"),
            PrimaryGradEnd: ParseColor("#EC4899")),

        ["EmeraldMatrix"] = new ThemePalette(
            BgPrimary: ParseColor("#06110D"),
            BgSecondary: ParseColor("#0B1B15"),
            BgCard: ParseColor("#10261E"),
            BgCardHover: ParseColor("#17362A"),
            BorderSubtle: ParseColor("#1F4738"),
            AccentBlue: ParseColor("#34D399"),
            AccentBlueHover: ParseColor("#059669"),
            AccentEmerald: ParseColor("#22C55E"),
            AccentEmeraldHover: ParseColor("#16A34A"),
            DangerRed: ParseColor("#F43F5E"),
            WarningAmber: ParseColor("#F59E0B"),
            TextPrimary: ParseColor("#ECFDF5"),
            TextSecondary: ParseColor("#A7F3D0"),
            TextMuted: ParseColor("#5F9A82"),
            SidebarTop: ParseColor("#091812"),
            SidebarBottom: ParseColor("#050F0B"),
            CardGradStart: ParseColor("#132D23"),
            CardGradEnd: ParseColor("#0C1E17"),
            PrimaryGradStart: ParseColor("#059669"),
            PrimaryGradMid: ParseColor("#10B981"),
            PrimaryGradEnd: ParseColor("#06B6D4")),

        ["FluentLight"] = new ThemePalette(
            BgPrimary: ParseColor("#EEF2F6"),
            BgSecondary: ParseColor("#FFFFFF"),
            BgCard: ParseColor("#E2E8F0"),
            BgCardHover: ParseColor("#CBD5E1"),
            BorderSubtle: ParseColor("#CBD5E1"),
            AccentBlue: ParseColor("#0284C7"),
            AccentBlueHover: ParseColor("#0369A1"),
            AccentEmerald: ParseColor("#059669"),
            AccentEmeraldHover: ParseColor("#047857"),
            DangerRed: ParseColor("#E11D48"),
            WarningAmber: ParseColor("#D97706"),
            TextPrimary: ParseColor("#0F172A"),
            TextSecondary: ParseColor("#334155"),
            TextMuted: ParseColor("#64748B"),
            SidebarTop: ParseColor("#F8FAFC"),
            SidebarBottom: ParseColor("#E2E8F0"),
            CardGradStart: ParseColor("#FFFFFF"),
            CardGradEnd: ParseColor("#F1F5F9"),
            PrimaryGradStart: ParseColor("#2563EB"),
            PrimaryGradMid: ParseColor("#0284C7"),
            PrimaryGradEnd: ParseColor("#0891B2"))
    };

    /// <summary>
    /// Applies the requested theme palette to Application.Current.Resources, updating unfrozen brushes in place.
    /// </summary>
    public static void ApplyTheme(string? themeKey)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        if (string.IsNullOrWhiteSpace(themeKey) || !Palettes.TryGetValue(themeKey, out var p))
            p = Palettes["ObsidianNeon"];

        SetSolidBrush(resources, "BgPrimaryBrush", p.BgPrimary);
        SetSolidBrush(resources, "BgSecondaryBrush", p.BgSecondary);
        SetSolidBrush(resources, "BgCardBrush", p.BgCard);
        SetSolidBrush(resources, "BgCardHoverBrush", p.BgCardHover);
        SetSolidBrush(resources, "BorderSubtleBrush", p.BorderSubtle);
        SetSolidBrush(resources, "AccentBlueBrush", p.AccentBlue);
        SetSolidBrush(resources, "AccentBlueHoverBrush", p.AccentBlueHover);
        SetSolidBrush(resources, "AccentEmeraldBrush", p.AccentEmerald);
        SetSolidBrush(resources, "AccentEmeraldHoverBrush", p.AccentEmeraldHover);
        SetSolidBrush(resources, "DangerRedBrush", p.DangerRed);
        SetSolidBrush(resources, "AccentRedBrush", p.DangerRed);
        SetSolidBrush(resources, "WarningAmberBrush", p.WarningAmber);
        SetSolidBrush(resources, "AccentAmberBrush", p.WarningAmber);
        SetSolidBrush(resources, "TextPrimaryBrush", p.TextPrimary);
        SetSolidBrush(resources, "TextSecondaryBrush", p.TextSecondary);
        SetSolidBrush(resources, "TextMutedBrush", p.TextMuted);

        SetGradientBrush(resources, "SidebarBgGradientBrush",
            new Point(0, 0), new Point(0, 1),
            (p.SidebarTop, 0.0), (p.SidebarBottom, 1.0));

        SetGradientBrush(resources, "CardSurfaceGradientBrush",
            new Point(0, 0), new Point(1, 1),
            (p.CardGradStart, 0.0), (p.CardGradEnd, 1.0));

        SetGradientBrush(resources, "PrimaryGradientBrush",
            new Point(0, 0), new Point(1, 1),
            (p.PrimaryGradStart, 0.0), (p.PrimaryGradMid, 0.55), (p.PrimaryGradEnd, 1.0));
    }

    private static void SetSolidBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static void SetGradientBrush(
        ResourceDictionary resources,
        string key,
        Point start,
        Point end,
        params (Color Color, double Offset)[] stops)
    {
        if (resources[key] is LinearGradientBrush existing && !existing.IsFrozen)
        {
            existing.StartPoint = start;
            existing.EndPoint = end;
            existing.GradientStops.Clear();
            foreach (var (c, off) in stops)
                existing.GradientStops.Add(new GradientStop(c, off));
            return;
        }

        var grad = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (c, off) in stops)
            grad.GradientStops.Add(new GradientStop(c, off));
        resources[key] = grad;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
