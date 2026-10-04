using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The app draws its text with the fonts inside it, not with whatever the
/// laptop has installed (N-10).
/// </summary>
/// <remarks>
/// On 3 and 4 Oct two laptops' apps closed as a call ended: Windows listed a
/// font whose file was gone, and every layout pass that drew text in it threw
/// <see cref="System.IO.FileNotFoundException"/>. A font taken from the
/// program cannot go missing that way. Each test asks WPF where the shapes it
/// would draw with come from.
/// </remarks>
public class BundledFontsTests
{
    [Theory]
    [InlineData("UiFont", "Cairo", 400)]
    [InlineData("UiFont", "Cairo", 600)]
    [InlineData("UiFont", "Cairo", 700)]
    [InlineData("MonoFont", "Cascadia Mono", 400)]
    [InlineData("MonoFont", "Cascadia Mono", 600)]
    [InlineData("MonoFont", "Cascadia Mono", 700)]
    public void Each_weight_comes_from_inside_the_app(string key, string family, int weight) => UiThread.Run(_ =>
    {
        var face = Face(key, FontWeight.FromOpenTypeWeight(weight));

        face.FontUri.Scheme.Should().Be("pack", "a file: address would be the laptop's own copy");
        face.FontUri.AbsoluteUri.Should().Contain("/CallCenter.AgentApp;component/Assets/Fonts/");
        face.FamilyNames.Values.Should().Contain(family);
        face.Weight.ToOpenTypeWeight().Should().Be(weight, "a missing weight would be faked from another");
        return Task.CompletedTask;
    });

    [Fact]
    public void Cairo_has_the_Arabic_the_agents_type() => UiThread.Run(_ =>
    {
        var face = Face("UiFont", FontWeights.Normal);

        // The letters a keyboard types (the font joins them itself), Arabic-
        // Indic digits, and the Latin and digits of a phone number or a price.
        foreach (var c in "ابتثجحخدذرزسشصضطظعغفقكلمنهويىءآأإؤئة٠١٢٣٤٥٦٧٨٩Az09+-.")
        {
            face.CharacterToGlyphMap.Should().ContainKey(c, $"'{c}' (U+{(int)c:X4}) would fall back to a laptop font");
        }

        return Task.CompletedTask;
    });

    private static GlyphTypeface Face(string key, FontWeight weight)
    {
        var theme = (ResourceDictionary)Application.LoadComponent(
            new Uri("/CallCenter.AgentApp;component/Theme.xaml", UriKind.Relative));

        var typeface = new Typeface((FontFamily)theme[key], FontStyles.Normal, weight, FontStretches.Normal);

        typeface.TryGetGlyphTypeface(out var face).Should().BeTrue($"{key} should resolve to a font");
        return face;
    }
}
