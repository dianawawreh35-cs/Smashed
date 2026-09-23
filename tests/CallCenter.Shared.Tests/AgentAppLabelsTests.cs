using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>
/// The Agent App's two language files, checked against each other and against
/// every label the app asks for (A-80).
/// </summary>
/// <remarks>
/// A missing label does not crash the app. <c>Localizer</c> writes a warning
/// and shows the key itself, so the agent sees <c>callLog.unclassified</c> on a
/// chip where "Not classified" belongs. That is the right behaviour at runtime
/// and exactly why nothing else ever caught it: the compiler cannot see a JSON
/// key, the type checker cannot see a XAML binding, and the app carries on.
///
/// This happened four times before it became a test — the last time on
/// 22 September 2026, when removing one label called <c>unclassified</c> took a
/// second, unrelated label of the same name in a different section with it.
/// The check existed all along as a throwaway script; DECISIONS asked three
/// times for it to be here.
///
/// The test reads the app's source tree, which a unit test normally would not.
/// It is a check on data files, not on code, and the alternative was a fifth
/// incident.
/// </remarks>
public class AgentAppLabelsTests
{
    private static readonly string AgentApp = FindAgentApp();

    /// <summary>
    /// A label reference is <c>Localizer[section.key]</c> in XAML or
    /// <c>localizer["section.key"]</c> in C#. The dot is required: it is what
    /// tells a literal key apart from a variable such as <c>Localizer[StatusKey]</c>.
    /// </summary>
    private static readonly Regex LabelReference = new(
        @"[Ll]ocalizer\[(?:""(?<q>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)""|(?<b>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+))\]",
        RegexOptions.Compiled);

    [Fact]
    public void Arabic_and_English_carry_the_same_keys()
    {
        var arabic = Load("ar");
        var english = Load("en");

        arabic.Keys.Except(english.Keys).Should().BeEmpty("every Arabic label needs an English one");
        english.Keys.Except(arabic.Keys).Should().BeEmpty("every English label needs an Arabic one");
    }

    [Fact]
    public void No_label_is_blank_in_either_language()
    {
        foreach (var language in new[] { "ar", "en" })
        {
            Load(language)
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .Should().BeEmpty($"a blank {language} label shows as nothing on screen");
        }
    }

    [Fact]
    public void Every_label_the_app_asks_for_exists()
    {
        var english = Load("en");

        var referenced = SourceFiles()
            .SelectMany(file => LabelReference.Matches(File.ReadAllText(file))
                .Select(m => (Key: m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["b"].Value,
                              File: Path.GetRelativePath(AgentApp, file))))
            .GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => g.Select(r => r.File).Distinct().Order().ToList());

        referenced.Should().NotBeEmpty("the scan found no label references at all, so it is looking in the wrong place");

        var missing = referenced
            .Where(kv => !english.ContainsKey(kv.Key))
            .Select(kv => $"{kv.Key}  <- {string.Join(", ", kv.Value)}")
            .ToList();

        missing.Should().BeEmpty(
            "a label the app asks for is not in en.json, so the agent would see the raw key on screen");
    }

    /// <summary>
    /// The statuses a call can have all need a label, because the call log
    /// shows them through <c>callLog.status.{Status}</c> built at runtime — a
    /// key the regex above cannot see.
    /// </summary>
    [Fact]
    public void Every_communication_status_has_a_call_log_label()
    {
        var english = Load("en");

        CommunicationStatuses.All
            .Where(status => !english.ContainsKey($"callLog.status.{status}"))
            .Should().BeEmpty("a call with this status would show the raw key in the call log");
    }

    private static Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AgentApp, "i18n", $"{language}.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var flat = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(document.RootElement, prefix: null, flat);
        return flat;
    }

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, key, into);
            }
            else
            {
                into[key] = property.Value.GetString() ?? string.Empty;
            }
        }
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(AgentApp, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Walks up from the test binary until it finds the Agent App project. Not a
    /// fixed relative path, so the test survives being run from the IDE, from
    /// the command line and from a build server that lays the tree out
    /// differently.
    /// </summary>
    private static string FindAgentApp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "CallCenter.AgentApp");

            if (Directory.Exists(Path.Combine(candidate, "i18n")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find src/CallCenter.AgentApp above the test binary. The label tests need the source tree.");
    }
}
