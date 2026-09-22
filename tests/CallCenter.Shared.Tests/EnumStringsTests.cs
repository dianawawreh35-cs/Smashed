using CallCenter.Shared;
using FluentAssertions;
using Xunit;
using TaskStatus = CallCenter.Shared.TaskStatus;

namespace CallCenter.Shared.Tests;

/// <summary>
/// Locks the enum string values against the CHECK constraints in
/// <c>docs/SCHEMA.md</c>. If one of these fails, either the schema changed and
/// a migration is owed, or someone renamed a member without meaning to.
/// </summary>
public class EnumStringsTests
{
    [Fact]
    public void Communication_kinds_match_the_check_constraint()
    {
        CommunicationKinds.All.Should().Equal("Call", "App");
    }

    [Fact]
    public void Directions_match_the_check_constraint()
    {
        Directions.All.Should().Equal("In", "Out", "None");
    }

    [Fact]
    public void Communication_statuses_match_the_check_constraint()
    {
        CommunicationStatuses.All.Should().Equal(
            "Ringing", "Answered", "Missed", "Rejected", "Blocked",
            "Abandoned", "Overflowed", "NoAnswer", "Failed", "Logged");
    }

    [Fact]
    public void Communication_sources_match_the_check_constraint()
    {
        CommunicationSources.All.Should().Equal("AgentApp", "AMI", "CDR", "Manual");
    }

    [Fact]
    public void User_roles_match_the_check_constraint()
    {
        UserRoles.All.Should().Equal("Agent", "Supervisor");
    }

    [Fact]
    public void Task_statuses_match_the_check_constraint()
    {
        TaskStatuses.All.Should().Equal("Open", "Done", "Cancelled");
    }

    [Fact]
    public void Task_origins_match_the_check_constraint()
    {
        TaskOrigins.All.Should().Equal("Complaint", "Abandoned", "Missed", "Manual");
    }

    [Fact]
    public void Pbx_event_sources_match_the_check_constraint()
    {
        PbxEventSources.All.Should().Equal("AMI", "CDR", "SIP");
    }

    [Theory]
    [InlineData(CommunicationSource.AgentApp, "AgentApp")]
    [InlineData(CommunicationSource.Ami, "AMI")]
    [InlineData(CommunicationSource.Cdr, "CDR")]
    [InlineData(CommunicationSource.Manual, "Manual")]
    public void Acronym_sources_serialise_to_their_uppercase_db_value(CommunicationSource value, string expected)
    {
        value.ToDbValue().Should().Be(expected);
    }

    [Theory]
    [InlineData("AMI", CommunicationSource.Ami)]
    [InlineData("ami", CommunicationSource.Ami)]
    [InlineData("CDR", CommunicationSource.Cdr)]
    [InlineData(" AgentApp ", CommunicationSource.AgentApp)]
    public void Sources_parse_case_insensitively(string dbValue, CommunicationSource expected)
    {
        EnumStrings.Parse<CommunicationSource>(dbValue).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Whatsapp")]
    [InlineData("1")]      // the underlying number must not parse as a member
    [InlineData("-1")]
    public void TryParse_rejects_values_outside_the_constraint(string? dbValue)
    {
        EnumStrings.TryParse<CommunicationSource>(dbValue, out _).Should().BeFalse();
    }

    [Fact]
    public void Every_enum_member_round_trips_through_its_db_value()
    {
        AssertRoundTrip<CommunicationKind>(v => v.ToDbValue(), CommunicationKinds.All);
        AssertRoundTrip<Direction>(v => v.ToDbValue(), Directions.All);
        AssertRoundTrip<CommunicationStatus>(v => v.ToDbValue(), CommunicationStatuses.All);
        AssertRoundTrip<CommunicationSource>(v => v.ToDbValue(), CommunicationSources.All);
        AssertRoundTrip<UserRole>(v => v.ToDbValue(), UserRoles.All);
        AssertRoundTrip<TaskStatus>(v => v.ToDbValue(), TaskStatuses.All);
        AssertRoundTrip<TaskOrigin>(v => v.ToDbValue(), TaskOrigins.All);
        AssertRoundTrip<PbxEventSource>(v => v.ToDbValue(), PbxEventSources.All);
    }

    /// <summary>
    /// Each member serialises to a distinct allowed value, every allowed value is
    /// produced by exactly one member, and parsing the value returns the member.
    /// </summary>
    private static void AssertRoundTrip<TEnum>(Func<TEnum, string> toDbValue, IReadOnlyList<string> allowed)
        where TEnum : struct, Enum
    {
        var members = Enum.GetValues<TEnum>();
        var produced = members.Select(toDbValue).ToArray();

        produced.Should().BeEquivalentTo(allowed, because: $"{typeof(TEnum).Name} must cover its CHECK constraint");

        foreach (var member in members)
        {
            EnumStrings.Parse<TEnum>(toDbValue(member)).Should().Be(member);
        }
    }
}
