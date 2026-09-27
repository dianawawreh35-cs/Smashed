using CallCenter.Shared.Contracts.AgentLogs;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>The laptop's machine name, made into a folder the server accepts (N-12).</summary>
public class AgentLogNamesTests
{
    [Theory]
    [InlineData("LAPTOP-7F3K2", "LAPTOP-7F3K2")]
    [InlineData("front desk", "front_desk")]
    [InlineData("..\\..\\x", "x")]
    [InlineData("مكتب", "unknown")]
    [InlineData("", "unknown")]
    [InlineData("...", "unknown")]
    public void A_machine_name_becomes_a_laptop_folder(string machine, string expected)
    {
        var laptop = AgentLogNames.Laptop(machine);

        laptop.Should().Be(expected);
        AgentLogNames.IsLaptop(laptop).Should().BeTrue();
    }

    [Fact]
    public void A_long_machine_name_is_cut_to_fit()
    {
        AgentLogNames.IsLaptop(AgentLogNames.Laptop(new string('A', 100))).Should().BeTrue();
    }

    [Theory]
    [InlineData("agent-20260927.log", true)]
    [InlineData("agent-20260927_001.log", true)]
    [InlineData("agent-20260927.log.bak", false)]
    [InlineData("agent-20260927.log\n", false)]
    [InlineData("callcenter-20260927.log", false)]
    public void Only_the_apps_daily_files_are_log_files(string name, bool expected)
    {
        AgentLogNames.IsFile(name).Should().Be(expected);
    }
}
