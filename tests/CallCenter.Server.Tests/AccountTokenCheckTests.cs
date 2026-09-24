using System.Security.Claims;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The parts of <see cref="AccountTokenCheck"/> that need no database. What it
/// does to real tokens after a password reset, a disable or a role change is in
/// <see cref="LoginTests"/>.
/// </summary>
public class AccountTokenCheckTests
{
    private const string Hash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";

    [Fact]
    public void The_stamp_is_the_same_for_the_same_password_and_role() =>
        AccountTokenCheck.Stamp(Hash, UserRoles.Agent).Should().Be(AccountTokenCheck.Stamp(Hash, UserRoles.Agent));

    [Fact]
    public void A_new_password_changes_the_stamp() =>
        AccountTokenCheck.Stamp(Hash + "x", UserRoles.Agent).Should().NotBe(AccountTokenCheck.Stamp(Hash, UserRoles.Agent));

    [Fact]
    public void A_new_role_changes_the_stamp() =>
        AccountTokenCheck.Stamp(Hash, UserRoles.Supervisor).Should().NotBe(AccountTokenCheck.Stamp(Hash, UserRoles.Agent));

    [Fact]
    public void The_stamp_does_not_contain_the_hash()
    {
        var stamp = AccountTokenCheck.Stamp(Hash, UserRoles.Agent);

        stamp.Should().NotContain(Hash[7..20], "the token is readable by anyone holding it");
        stamp.Length.Should().BeLessThan(20);
    }

    [Fact]
    public async Task A_token_with_no_stamp_is_refused_before_the_database_is_asked()
    {
        // Every token issued before this check existed. Refusing them costs one
        // extra sign-in after the upgrade; accepting them would leave a gap for
        // twelve hours. The null context proves no query runs.
        var check = new AccountTokenCheck(null!, NullLogger<AccountTokenCheck>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));

        (await check.IsCurrentAsync(principal, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task A_token_with_no_account_id_is_refused()
    {
        var check = new AccountTokenCheck(null!, NullLogger<AccountTokenCheck>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AppClaims.Stamp, "x")], "test"));

        (await check.IsCurrentAsync(principal, CancellationToken.None)).Should().BeFalse();
    }
}
