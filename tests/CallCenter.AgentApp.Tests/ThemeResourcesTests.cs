using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using CallCenter.AgentApp.Services;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Every <c>{StaticResource …}</c> and <c>{DynamicResource …}</c> the Agent
/// App's XAML names is defined somewhere it can be found, and the light and
/// dark palettes can stand in for each other (A-90).
/// </summary>
/// <remarks>
/// A missing key compiles, and fails only when the screen that uses it opens:
/// the classification form crashed that way once. The check existed as a
/// throwaway script that DECISIONS kept asking to make permanent (27 Sep). It
/// is deliberately simple: keys from every XAML file in the app, against the
/// keys used in all of them, so a key defined in one view and used in another
/// would pass here and fail at run time. That has never been the pattern.
/// </remarks>
public class ThemeResourcesTests
{
    private static readonly Regex Defined = new(@"x:Key=""([^""{]+)""", RegexOptions.Compiled);

    private static readonly Regex Used = new(@"\{(?:Static|Dynamic)Resource ([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    private static readonly Regex UsedStatic = new(@"\{StaticResource ([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    /// <summary>Keys the code looks up by name with <c>FindResource</c>.</summary>
    private static readonly string[] FoundByCode = ["Warning", "Success", "Danger", "OnWarning"];

    [Fact]
    public void Every_static_resource_is_defined()
    {
        var files = XamlFiles();

        files.Should().ContainKey("Theme.xaml", "the scan is looking in the wrong place otherwise");

        var keys = files.Values.SelectMany(text => Defined.Matches(text).Select(m => m.Groups[1].Value)).ToHashSet();

        var missing = files
            .SelectMany(file => Used.Matches(file.Value).Select(m => $"{m.Groups[1].Value}  <- {file.Key}"))
            .Concat(FoundByCode.Select(key => $"{key}  <- code"))
            .Where(use => !keys.Contains(use[..use.IndexOf(' ')]))
            .Distinct()
            .ToList();

        missing.Should().BeEmpty("a missing resource fails only when the screen that uses it opens");
    }

    [Fact]
    public void Both_palettes_have_the_same_colours()
    {
        UiThread.Run(_ =>
        {
            var dark = Palette("dark");
            var light = Palette("light");

            light.Keys.Cast<object>().Should().BeEquivalentTo(dark.Keys.Cast<object>(),
                "a colour only one palette has is missing, on screen, in the other");
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A palette colour looked up with StaticResource is read once, when the
    /// screen is built, and keeps the old palette's colour after a switch.
    /// </summary>
    [Fact]
    public void Palette_colours_are_never_static_resources()
    {
        var palette = Defined.Matches(File.ReadAllText(Path.Combine(AgentApp(), "Themes", "Dark.xaml")))
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var frozen = XamlFiles()
            .SelectMany(file => UsedStatic.Matches(file.Value)
                .Select(m => m.Groups[1].Value)
                .Where(palette.Contains)
                .Select(key => $"{key}  <- {file.Key}"))
            .Distinct()
            .ToList();

        palette.Should().Contain("Accent", "the scan is reading the wrong file otherwise");
        frozen.Should().BeEmpty("it would stay in the old colours when the agent switches (A-90)");
    }

    /// <summary>
    /// Text the agent reads is at least 4.5:1 on the surfaces it sits on, in
    /// both palettes. TextFaint is left out: it marks what is switched off.
    /// </summary>
    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public void Text_is_readable_in_both_palettes(string theme)
    {
        UiThread.Run(_ =>
        {
            var palette = Palette(theme);
            Color C(string key) => ((SolidColorBrush)palette[key]).Color;

            var pairs = new List<(string Text, string On)>();
            foreach (var surface in new[] { "Bg", "Surface", "SurfaceAlt" })
            {
                pairs.Add(("Text", surface));
                pairs.Add(("TextMuted", surface));
            }

            foreach (var colour in new[] { "Accent", "Success", "Warning", "Danger" })
            {
                pairs.Add((colour, "Surface"));
            }

            pairs.AddRange([("WarningText", "WarningDim"), ("DangerText", "DangerDim"), ("OnWarning", "Warning"), ("OnSuccess", "Success")]);

            var poor = pairs
                .Select(p => (p.Text, p.On, Ratio: Contrast(C(p.Text), C(p.On))))
                .Where(p => p.Ratio < 4.5)
                .Select(p => $"{p.Text} on {p.On}: {p.Ratio:0.00}:1")
                .ToList();

            poor.Should().BeEmpty("small text needs 4.5:1 (WCAG AA)");
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// A filled button's words are in the button's own colour (A-90). Until
    /// 4 Oct the keyless TextBlock style painted them Text, which in light is
    /// near-black: black words on the blue and the green buttons.
    /// </summary>
    /// <remarks>
    /// This checks the button's own style reaches its words and gives them its
    /// colour. It cannot show the old fault: only a style in the
    /// <em>application's</em> resources crosses into a template, and these
    /// tests have no Application. That half is the checklist's to see.
    /// </remarks>
    [Theory]
    [InlineData("PrimaryButton")]
    [InlineData("SuccessButton")]
    [InlineData("DangerButton")]
    public void A_filled_buttons_words_are_white_in_light(string style)
    {
        UiThread.Run(_ =>
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(Palette("light"));
            resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                new Uri("/CallCenter.AgentApp;component/Theme.xaml", UriKind.Relative)));

            var button = new System.Windows.Controls.Button { Content = "Answer" };
            var panel = new System.Windows.Controls.StackPanel { Resources = resources };
            panel.Children.Add(button);
            button.SetResourceReference(FrameworkElement.StyleProperty, style);

            panel.Measure(new Size(400, 100));
            panel.Arrange(new Rect(0, 0, 400, 100));
            panel.UpdateLayout();

            var words = Descendants(button).OfType<System.Windows.Controls.TextBlock>().Single();

            // The template's own style reached it: the keyless style's size,
            // not one inherited from somewhere else.
            words.FontSize.Should().Be(13);
            ((SolidColorBrush)words.Foreground).Color.Should().Be(Colors.White);
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static ResourceDictionary Palette(string theme) =>
        (ResourceDictionary)Application.LoadComponent(ThemeService.PaletteUri(theme));

    /// <summary>The WCAG contrast ratio of two colours.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte c) =>
            c / 255.0 <= 0.03928 ? c / 255.0 / 12.92 : Math.Pow((c / 255.0 + 0.055) / 1.055, 2.4);

        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static Dictionary<string, string> XamlFiles() =>
        Directory.EnumerateFiles(AgentApp(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(AgentApp(), f), File.ReadAllText);

    /// <summary>
    /// The app's source, found from where this file was compiled rather than
    /// from the test binary, so the test works from any build folder.
    /// </summary>
    private static string AgentApp([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "CallCenter.AgentApp"));
}
