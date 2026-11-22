using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class NavigationMenuTests
{
    private static readonly XNamespace WpfNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreeCleaner.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FreeCleaner.slnx from test output directory.");
    }

    private static List<string> ReadViewModelSectionKeys(string repoRoot)
    {
        string vmCs = string.Concat(
            Directory.GetFiles(Path.Combine(repoRoot, "FreeCleaner", "ViewModels"), "MainViewModel*.cs")
                .Select(path => File.ReadAllText(path, Encoding.UTF8)));
        return Regex.Matches(vmCs, @"public bool Is([A-Za-z0-9]+)Section\s*=>")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Sidebar_ContainsAll60SectionKeysExactlyOnce_WithPinnedDashboardAndSettings()
    {
        string repoRoot = FindRepoRoot();
        XDocument doc = XDocument.Load(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"));

        List<XElement> navButtons = doc.Descendants(WpfNs + "RadioButton")
            .Where(rb => string.Equals((string?)rb.Attribute("Style"), "{StaticResource NavRadioButtonStyle}", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(60, navButtons.Count);

        List<string> sidebarKeys = navButtons
            .Select(rb => (string?)rb.Attribute("CommandParameter") ?? string.Empty)
            .ToList();

        Assert.All(sidebarKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(60, sidebarKeys.Distinct(StringComparer.Ordinal).Count());

        List<string> vmSectionKeys = ReadViewModelSectionKeys(repoRoot);
        Assert.Equal(60, vmSectionKeys.Count);
        Assert.True(
            sidebarKeys.ToHashSet(StringComparer.Ordinal).SetEquals(vmSectionKeys),
            "Sidebar CommandParameters must match all 60 MainViewModel Is*Section properties.");

        XElement? pinnedDashboard = doc.Descendants(WpfNs + "RadioButton")
            .FirstOrDefault(rb => string.Equals((string?)rb.Attribute(XamlNs + "Name"), "PinnedDashboardNavBtn", StringComparison.Ordinal));
        XElement? pinnedSettings = doc.Descendants(WpfNs + "RadioButton")
            .FirstOrDefault(rb => string.Equals((string?)rb.Attribute(XamlNs + "Name"), "PinnedSettingsNavBtn", StringComparison.Ordinal));

        Assert.NotNull(pinnedDashboard);
        Assert.NotNull(pinnedSettings);
        Assert.Equal("Dashboard", (string?)pinnedDashboard!.Attribute("CommandParameter"));
        Assert.Equal("Settings", (string?)pinnedSettings!.Attribute("CommandParameter"));
        Assert.Empty(pinnedDashboard.Ancestors(WpfNs + "Expander"));
        Assert.Empty(pinnedSettings.Ancestors(WpfNs + "Expander"));
    }

    [Fact]
    public void Sidebar_Has8BalancedCategories_WithExactHeaderCounts_AndDistinctIconsPerCategory()
    {
        string repoRoot = FindRepoRoot();
        XDocument doc = XDocument.Load(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml"));

        XElement? navMenuStack = doc.Descendants(WpfNs + "StackPanel")
            .FirstOrDefault(sp => string.Equals((string?)sp.Attribute(XamlNs + "Name"), "NavMenuStackPanel", StringComparison.Ordinal));
        Assert.NotNull(navMenuStack);

        List<XElement> categoryExpanders = navMenuStack!.Elements(WpfNs + "Expander")
            .Where(exp => !string.Equals((string?)exp.Attribute(XamlNs + "Name"), "FavoritesExpander", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(8, categoryExpanders.Count);

        int[] expectedCounts = [7, 7, 8, 4, 10, 6, 10, 6];
        Assert.Equal(58, expectedCounts.Sum());

        for (int i = 0; i < categoryExpanders.Count; i++)
        {
            XElement expander = categoryExpanders[i];
            List<XElement> items = expander.Descendants(WpfNs + "RadioButton").ToList();
            Assert.Equal(expectedCounts[i], items.Count);

            string headerRaw = (string?)expander.Attribute("Header") ?? string.Empty;
            Match locMatch = Regex.Match(headerRaw, @"\{DynamicResource\s+(Loc_Cat[A-Za-z0-9_]+)\}");
            Assert.True(locMatch.Success, $"Missing Loc_Cat* DynamicResource in expander header: {headerRaw}");

            string locKey = locMatch.Groups[1].Value;
            foreach (string lang in new[] { "tr-TR", "en-US" })
            {
                string localizedHeader = LocalizationService.Get(locKey, lang);
                Match countMatch = Regex.Match(localizedHeader, @"\((\d+)\)\s*$");
                Assert.True(countMatch.Success, $"Missing count suffix in {locKey} ({lang}): '{localizedHeader}'");
                Assert.Equal(items.Count, int.Parse(countMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            List<string> iconGlyphs = items
                .Select(rb => (string?)rb.Descendants(WpfNs + "TextBlock")
                    .FirstOrDefault(tb => string.Equals((string?)tb.Attribute("FontFamily"), "Segoe MDL2 Assets", StringComparison.Ordinal))
                    ?.Attribute("Text") ?? string.Empty)
                .ToList();

            Assert.All(iconGlyphs, g => Assert.False(string.IsNullOrWhiteSpace(g)));
            Assert.Equal(iconGlyphs.Count, iconGlyphs.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void MainWindow_AutoRevealsActiveNavSection_AndCatalogCoversAll60Modules()
    {
        string repoRoot = FindRepoRoot();
        string mainWindowCs = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml.cs"), Encoding.UTF8);

        Assert.Contains("private void RevealActiveNavSection(", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("RevealActiveNavSection(viewModel.ActiveSection);", mainWindowCs, StringComparison.Ordinal);
        Assert.Contains("BringIntoView()", mainWindowCs, StringComparison.Ordinal);

        string dashboardVmCs = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "ViewModels", "MainViewModel.Dashboard.cs"), Encoding.UTF8);
        Assert.Contains("const string TrStorage =", dashboardVmCs, StringComparison.Ordinal);
        Assert.Contains("const string EnStorage =", dashboardVmCs, StringComparison.Ordinal);
        Assert.Contains("const string TrApps =", dashboardVmCs, StringComparison.Ordinal);
        Assert.Contains("const string EnApps =", dashboardVmCs, StringComparison.Ordinal);

        List<string> catalogModuleKeys = Regex.Matches(dashboardVmCs, @"AddEntry\(""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(key => !key.StartsWith("CMD:", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(60, catalogModuleKeys.Count);
        Assert.Equal(60, catalogModuleKeys.Distinct(StringComparer.Ordinal).Count());

        List<string> vmSectionKeys = ReadViewModelSectionKeys(repoRoot);
        Assert.True(
            catalogModuleKeys.ToHashSet(StringComparer.Ordinal).SetEquals(vmSectionKeys),
            "Command Palette module catalog must cover all 60 MainViewModel Is*Section keys.");
    }
}
