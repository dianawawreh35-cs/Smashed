using System.Text.Json;
using CallCenter.Server.Data.Seed;
using CallCenter.Shared.Text;
using CallCenter.Shared;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Checks the seed contents against section 7 of <c>docs/SCHEMA.md</c> and the
/// requirements that depend on them, and the argument parsing for the
/// installation command in step 7 of the runbook.
/// </summary>
public class SeedDataTests
{
    [Fact]
    public void Four_branches_are_seeded()
    {
        // SRS 2.4: the restaurant operates four branches. Named rather than
        // numbered since 2026-09-20, because the delivery areas (A-65) refer to
        // them and an area cannot belong to "Branch 3".
        SeedData.Branches.Should().Equal("رافات", "بطن الهوى", "ايكون", "نابلس");
    }

    [Fact]
    public void Every_delivery_area_names_a_branch_that_is_seeded()
    {
        // A seed row naming a branch that does not exist is skipped with a
        // warning at install time, which nobody reads. Caught here instead.
        var branches = SeedData.Branches.ToHashSet();

        SeedData.DeliveryAreas
            .Where(a => !branches.Contains(a.Branch))
            .Should().BeEmpty();
    }

    [Fact]
    public void No_delivery_area_is_listed_twice()
    {
        // The unique index would refuse the second one, so a duplicate here is
        // a row silently lost at install time.
        SeedData.DeliveryAreas
            .GroupBy(a => NameNormalizer.Normalize(a.Area))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void No_delivery_area_is_priced_below_zero()
    {
        // Zero is allowed and meant: three areas beside the Rafat branch
        // deliver free. Negative is somebody's typo, and the CHECK constraint
        // would reject the whole seed.
        SeedData.DeliveryAreas.Where(a => a.Price < 0).Should().BeEmpty();
    }

    [Fact]
    public void Channels_match_the_schema_and_only_phone_is_a_system_channel()
    {
        SeedData.Channels.Select(c => c.Name)
            .Should().Equal("Phone", "WhatsApp", "Facebook", "Instagram", "Wheels", "Yummy", "FoodOnTime", "PalEat");

        SeedData.Channels.Where(c => c.IsSystem).Select(c => c.Name)
            .Should().ContainSingle("calls are always attributed to Phone, so it cannot be deleted")
            .Which.Should().Be("Phone");
    }

    [Fact]
    public void Classification_types_match_the_default_form_fields()
    {
        // A-40: Order, Cancellation, Complaint, Inquiry, Wrong number, Other.
        SeedData.ClassificationTypes.Select(t => t.Name)
            .Should().Equal("Order", "Cancellation", "Complaint", "Inquiry", "WrongNumber", "Other");
    }

    [Fact]
    public void Order_and_complaint_are_system_types()
    {
        // Reports depend on them existing: R-05, R-14, R-17.
        SeedData.ClassificationTypes.Where(t => t.IsSystem).Select(t => t.Name)
            .Should().BeEquivalentTo(["Order", "Complaint"]);
    }

    [Fact]
    public void Every_classification_type_has_both_labels()
    {
        // N-09: Arabic and English for both applications.
        foreach (var (name, ar, en, _, _) in SeedData.ClassificationTypes)
        {
            ar.Should().NotBeNullOrWhiteSpace($"{name} needs an Arabic label");
            en.Should().NotBeNullOrWhiteSpace($"{name} needs an English label");
            ar.Should().NotBe(en, $"{name}'s Arabic label should not be the English one");
        }
    }

    [Fact]
    public void Settings_are_the_keys_the_schema_lists()
    {
        SeedData.Settings.Select(s => s.Key).Should().BeEquivalentTo([
            "recording.retention_days",
            "agent.idle_logout_minutes",
            "agent.edit_window",
            "sla.answer_seconds",
            "pbx.host",
            "reports.internal_numbers",
            "agent.call_log_days",
        ]);
    }

    [Theory]
    [InlineData("recording.retention_days", "90")]   // A-33 / S-43
    [InlineData("agent.idle_logout_minutes", "240")] // A-05, raised from 30 on 2026-09-21
    [InlineData("agent.edit_window", "SameDay")]     // A-42
    [InlineData("sla.answer_seconds", "20")]         // R-21
    [InlineData("agent.call_log_days", "7")]         // A-50
    public void Setting_defaults_match_the_schema(string key, string expected)
    {
        SeedData.Settings.Single(s => s.Key == key).Value.Should().Be(expected);
    }

    [Fact]
    public void Site_specific_settings_start_blank()
    {
        // Filled in during installation once the client's PBX person confirms them.
        // reports.internal_numbers too: blank means every call counts as a
        // customer call, which is the right default before S-48 is filled in.
        foreach (var key in new[] { "pbx.host", "reports.internal_numbers" })
        {
            SeedData.Settings.Single(s => s.Key == key).Value.Should().BeEmpty();
        }
    }

    [Fact]
    public void Form_definition_v1_is_valid_json_with_the_default_fields()
    {
        using var doc = JsonDocument.Parse(SeedData.FormDefinitionV1);

        var keys = doc.RootElement.GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("key").GetString())
            .ToList();

        // A-40's default fields.
        keys.Should().Equal("type", "branch", "order_value", "notes", "follow_up");
    }

    [Fact]
    public void Form_definition_v1_requires_type_and_branch()
    {
        using var doc = JsonDocument.Parse(SeedData.FormDefinitionV1);

        var required = doc.RootElement.GetProperty("fields")
            .EnumerateArray()
            .Where(f => f.TryGetProperty("required", out var r) && r.GetBoolean())
            .Select(f => f.GetProperty("key").GetString())
            .ToList();

        required.Should().BeEquivalentTo(["type", "branch"],
            "every communication is assigned a branch and a type (SRS 2.4)");
    }

    [Fact]
    public void Order_value_only_shows_for_order_and_cancellation()
    {
        using var doc = JsonDocument.Parse(SeedData.FormDefinitionV1);

        var field = doc.RootElement.GetProperty("fields")
            .EnumerateArray()
            .Single(f => f.GetProperty("key").GetString() == "order_value");

        field.GetProperty("showWhenType").EnumerateArray()
            .Select(t => t.GetString())
            .Should().BeEquivalentTo(["Order", "Cancellation"]);
    }

    [Fact]
    public void The_first_account_is_a_supervisor()
    {
        SeedData.FirstUserRole.Should().Be(UserRoles.Supervisor);
    }
}

public class SeedCommandTests
{
    private static SeedCommand.Options Parse(params string[] args) =>
        SeedCommand.ParseArguments(["seed", .. args]);

    [Fact]
    public void Recognises_the_verb()
    {
        SeedCommand.IsRequested(["seed"]).Should().BeTrue();
        SeedCommand.IsRequested(["SEED", "--admin-user", "x"]).Should().BeTrue();
        SeedCommand.IsRequested([]).Should().BeFalse();
        SeedCommand.IsRequested(["--urls", "http://localhost:5000"]).Should().BeFalse();
    }

    [Fact]
    public void Parses_the_runbook_command()
    {
        var options = Parse(
            "--admin-user", "supervisor",
            "--admin-password", "TempPass!2026",
            "--branches", "Branch 1,Branch 2,Branch 3,Branch 4");

        options.Error.Should().BeNull();
        options.AdminLogin.Should().Be("supervisor");
        options.AdminPassword.Should().Be("TempPass!2026");
        options.Branches.Should().Equal("Branch 1", "Branch 2", "Branch 3", "Branch 4");
    }

    [Fact]
    public void Branch_names_are_trimmed_and_blanks_dropped()
    {
        Parse("--branches", " Ramallah , Nablus ,, Hebron ")
            .Branches.Should().Equal("Ramallah", "Nablus", "Hebron");
    }

    [Fact]
    public void Seeding_reference_data_without_a_user_is_allowed()
    {
        var options = Parse();

        options.Error.Should().BeNull();
        options.AdminLogin.Should().BeNull();
    }

    [Fact]
    public void A_login_without_a_password_is_refused()
    {
        Parse("--admin-user", "supervisor").Error
            .Should().Contain("--admin-password");
    }

    [Theory]
    [InlineData("--admin-user")]
    [InlineData("--admin-password")]
    [InlineData("--branches")]
    public void An_option_missing_its_value_is_refused(string option)
    {
        Parse(option).Error.Should().Contain(option);
    }

    [Fact]
    public void An_unknown_option_is_refused_rather_than_ignored()
    {
        Parse("--admin-users", "supervisor").Error.Should().Contain("--admin-users");
    }

    [Fact]
    public void Empty_branch_list_is_refused()
    {
        Parse("--branches", ",,").Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_is_available(string flag)
    {
        var options = Parse(flag);

        options.ShowHelp.Should().BeTrue();
        options.Error.Should().BeNull();
    }
}
