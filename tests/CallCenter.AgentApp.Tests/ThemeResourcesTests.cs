using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Every <c>{StaticResource …}</c> the Agent App's XAML names is defined
/// somewhere it can be found.
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

    private static readonly Regex Used = new(@"\{StaticResource ([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    /// <summary>Keys the code looks up by name with <c>FindResource</c>.</summary>
    private static readonly string[] FoundByCode = ["Warning", "Success", "Danger", "OnWarning"];

    [Fact]
    public void Every_static_resource_is_defined()
    {
        var files = Directory.EnumerateFiles(AgentApp(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);

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

    /// <summary>
    /// The app's source, found from where this file was compiled rather than
    /// from the test binary, so the test works from any build folder.
    /// </summary>
    private static string AgentApp([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "CallCenter.AgentApp"));
}
