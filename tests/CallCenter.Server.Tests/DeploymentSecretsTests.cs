using CallCenter.Server.Features.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The server will not start on a key anyone can read (F-13): the placeholders
/// in <c>.env.example</c> anywhere, the development keys outside Development.
/// </summary>
public class DeploymentSecretsTests
{
    private const string Good = "q3S8v0yJcH2mZr7Xb1nT5kW9pL4dF6gA0eR2uY8iO3=";

    [Theory]
    [InlineData("Jwt:SigningKey", "JWT_SECRET")]
    [InlineData("Security:SipSecretKey", "SIP_SECRET_KEY")]
    public void The_example_value_is_refused_and_the_message_names_the_setting(string path, string envName)
    {
        var configuration = Config((path, "change-me-to-a-long-random-string"));

        var act = () => DeploymentSecrets.Check(configuration, isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{path}'*example value*{envName}*");
    }

    [Fact]
    public void The_development_keys_are_refused_outside_development_only()
    {
        foreach (var key in DeploymentSecrets.DevelopmentKeys)
        {
            DeploymentSecrets.Problem(key, isDevelopment: false).Should().Contain("development key");
            DeploymentSecrets.Problem(key, isDevelopment: true).Should().BeNull("the dev server runs on them");
        }
    }

    [Fact]
    public void A_key_of_the_installer_s_own_starts()
    {
        var act = () => DeploymentSecrets.Check(Config(("Jwt:SigningKey", Good), ("Security:SipSecretKey", Good)), isDevelopment: false);

        act.Should().NotThrow();
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}
