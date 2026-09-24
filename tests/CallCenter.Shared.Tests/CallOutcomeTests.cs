using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>
/// Which calls are classified, which take a note, and which take neither
/// (A-40, A-41).
/// </summary>
/// <remarks>
/// Only an answered call is classified — nobody spoke on the others, and a
/// classification on one would put an order or a complaint into the reports
/// that never happened. A missed, rejected or unanswered outbound call takes a
/// note saying why.
/// </remarks>
public class CallOutcomeTests
{
    private static CommunicationDto Call(string status, bool classified = false) => new(
        Guid.NewGuid(), CommunicationKinds.Call, Directions.In, status,
        null, null, "0599123456", null, DateTimeOffset.UtcNow, null, null, null,
        null, "2001", null, classified, null, HasRecording: false, RecordingExpired: false);

    [Fact]
    public void Only_an_answered_call_is_classified()
    {
        CommunicationStatuses.All
            .Where(s => Call(s).CanBeClassified)
            .Should().Equal(CommunicationStatuses.Answered);
    }

    /// <summary>
    /// A-30: the recorder runs from answer to hang-up, so nothing else has
    /// audio — and the call log must not tell an agent a missed call's
    /// recording "may still be uploading".
    /// </summary>
    [Fact]
    public void Only_an_answered_call_can_have_a_recording()
    {
        CommunicationStatuses.All
            .Where(s => Call(s).CanHaveRecording)
            .Should().Equal(CommunicationStatuses.Answered);
    }

    [Fact]
    public void Missed_rejected_and_unanswered_outbound_calls_take_a_note()
    {
        CommunicationStatuses.All
            .Where(s => Call(s).TakesNotes)
            .Should().Equal(
                CommunicationStatuses.Missed,
                CommunicationStatuses.Rejected,
                CommunicationStatuses.NoAnswer);
    }

    [Theory]
    [InlineData(CommunicationStatuses.Missed)]
    [InlineData(CommunicationStatuses.Rejected)]
    [InlineData(CommunicationStatuses.Blocked)]
    [InlineData(CommunicationStatuses.NoAnswer)]
    public void A_call_that_was_not_answered_never_owes_a_classification(string status)
    {
        Call(status).IsUnclassified.Should().BeFalse();
    }

    [Fact]
    public void An_answered_call_owes_one_until_it_is_classified()
    {
        Call(CommunicationStatuses.Answered).IsUnclassified.Should().BeTrue();
        Call(CommunicationStatuses.Answered, classified: true).IsUnclassified.Should().BeFalse();
    }
}
