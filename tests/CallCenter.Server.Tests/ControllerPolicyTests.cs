using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Every controller action says who may call it (27 Sep review).
/// </summary>
/// <remarks>
/// The server has a fallback policy since 27 Sep, so an action that names none
/// needs a signed-in user rather than nobody. That is a safety net, not a
/// decision: an agent-only or supervisor-only rule has to be written down, and
/// this finds the action where it was forgotten.
/// </remarks>
public class ControllerPolicyTests
{
    [Fact]
    public void Every_action_names_a_policy_or_is_marked_anonymous()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        controllers.Should().NotBeEmpty();

        var unmarked = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
                .Where(m => !Marked(c) && !Marked(m))
                .Select(m => $"{c.Name}.{m.Name}"))
            .ToList();

        unmarked.Should().BeEmpty("each action, or its controller, carries [Authorize(policy)] or [AllowAnonymous]");
    }

    [Fact]
    public void Only_sign_in_is_anonymous()
    {
        var anonymous = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => c.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                            || m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(m => $"{c.Name}.{m.Name}"))
            .ToList();

        anonymous.Should().BeEquivalentTo(["AuthController.Login"]);
    }

    private static bool Marked(MemberInfo member) =>
        member.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || member.GetCustomAttributes<AuthorizeAttribute>().Any(a => !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.Roles));
}
