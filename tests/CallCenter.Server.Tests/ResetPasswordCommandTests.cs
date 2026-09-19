using CallCenter.Server.Data.Seed;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The command that gets everybody back in when the supervisor password is lost.
/// </summary>
/// <remarks>
/// Argument parsing only — what it then does needs a database. That is the right
/// half to test anyway: this is run once, under pressure, by somebody who is
/// locked out, and a command that silently does the wrong thing because an
/// option was mistyped is worse than one that refuses.
/// </remarks>
public class ResetPasswordCommandTests
{
    [Theory]
    [InlineData("reset-password")]
    [InlineData("RESET-PASSWORD")]
    [InlineData("Reset-Password")]
    public void The_verb_is_recognised_whatever_the_case(string verb) =>
        ResetPasswordCommand.IsRequested([verb]).Should().BeTrue();

    [Theory]
    [InlineData("seed")]
    [InlineData("")]
    public void Another_verb_is_not_this_command(string verb) =>
        ResetPasswordCommand.IsRequested([verb]).Should().BeFalse();

    [Fact]
    public void No_arguments_at_all_is_not_this_command() =>
        ResetPasswordCommand.IsRequested([]).Should().BeFalse();

    [Fact]
    public void A_complete_command_parses()
    {
        var options = ResetPasswordCommand.ParseArguments(
            ["reset-password", "--user", "supervisor", "--password", "NewPass!2026"]);

        options.Error.Should().BeNull();
        options.Login.Should().Be("supervisor");
        options.Password.Should().Be("NewPass!2026");
        options.MakeSupervisor.Should().BeFalse();
    }

    [Fact]
    public void Promotion_is_opt_in()
    {
        // Resetting a password must not quietly change what an account can do.
        var options = ResetPasswordCommand.ParseArguments(
            ["reset-password", "--user", "dia20", "--password", "NewPass!2026", "--make-supervisor"]);

        options.Error.Should().BeNull();
        options.MakeSupervisor.Should().BeTrue();
    }

    [Fact]
    public void Without_a_user_it_refuses()
    {
        var options = ResetPasswordCommand.ParseArguments(
            ["reset-password", "--password", "NewPass!2026"]);

        options.Error.Should().Contain("--user");
    }

    [Fact]
    public void Without_a_password_it_refuses()
    {
        var options = ResetPasswordCommand.ParseArguments(["reset-password", "--user", "supervisor"]);

        options.Error.Should().Contain("--password");
    }

    [Fact]
    public void A_password_shorter_than_the_minimum_is_refused()
    {
        // Refused here rather than accepted and then rejected at login, which
        // would look like the reset had not worked.
        var options = ResetPasswordCommand.ParseArguments(
            ["reset-password", "--user", "supervisor", "--password", "short"]);

        options.Error.Should().Contain("8");
    }

    [Fact]
    public void An_option_with_no_value_is_refused_rather_than_guessed()
    {
        var options = ResetPasswordCommand.ParseArguments(["reset-password", "--user"]);

        options.Error.Should().NotBeNull();
    }

    [Fact]
    public void An_unknown_option_is_refused()
    {
        // Somebody reaching for --admin-user from the seed command should be
        // told, not have it ignored while the reset silently does nothing.
        var options = ResetPasswordCommand.ParseArguments(
            ["reset-password", "--admin-user", "supervisor", "--password", "NewPass!2026"]);

        options.Error.Should().Contain("--admin-user");
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_is_asked_for_before_anything_else_is_validated(string flag)
    {
        // --help must work without --user, or somebody who cannot remember the
        // arguments cannot find out what they are.
        var options = ResetPasswordCommand.ParseArguments(["reset-password", flag]);

        options.ShowHelp.Should().BeTrue();
        options.Error.Should().BeNull();
    }
}
