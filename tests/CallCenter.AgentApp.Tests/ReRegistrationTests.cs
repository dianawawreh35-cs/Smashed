using CallCenter.AgentApp.Services.Sip;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Logging in again after the PBX refused a password that had already worked
/// on this sign-in (A-02, 1 Oct 20:01 on extension 2010).
/// </summary>
public class ReRegistrationTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 300)]
    [InlineData(50, 300)]
    public void The_waits_grow_then_settle_at_five_minutes(int refusals, int seconds)
    {
        ReRegistration.Wait(refusals).Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void The_first_try_comes_soon_enough_to_miss_few_calls()
    {
        ReRegistration.Wait(1).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(10, true)]
    public void Only_refusals_that_go_on_send_the_agent_to_the_supervisor(int refusals, bool looksWrong)
    {
        ReRegistration.LooksWrong(refusals).Should().Be(looksWrong);
    }
}
