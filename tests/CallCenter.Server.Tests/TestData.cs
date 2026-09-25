using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Server.Tests;

/// <summary>
/// Rows for the <see cref="DatabaseFactAttribute"/> tests, and a way to sign in
/// as them through the real login endpoint.
/// </summary>
/// <remarks>
/// <b>Every test makes its own rows, named so they cannot collide</b>: a random
/// login, a random mobile number, a random SIP Call-ID. Nothing depends on the
/// order tests run in, so a database that has seen a hundred runs gives the same
/// answers as a fresh one. <b>Everything is removed when the run ends</b>
/// (<see cref="TestSweeper"/>, since 25 Sep): the names are how the sweep finds
/// them, so keep to them, and make contacts through <see cref="CreateContactAsync"/>
/// rather than straight into the table, because a contact has no pattern to find. A test that needs "the
/// only contact with this number" gets it by making the number up, never by
/// assuming the table is empty.
/// </remarks>
public class TestData(CallCenterApiFactory factory)
{
    /// <summary>The password every test account is given.</summary>
    public const string Password = "correct horse battery";

    /// <summary>
    /// What the Agent App sends as its laptop id. Its presence is what makes a
    /// login an Agent App login (A-05).
    /// </summary>
    public const string LaptopId = "LAPTOP-TEST";

    /// <summary>
    /// A PBX address from configuration, for when the database's <c>pbx.host</c>
    /// is blank. Without one the server rightly withholds an agent's extension,
    /// and a test could not tell that apart from the rule it is checking.
    /// </summary>
    /// <remarks>
    /// The host runs the real <see cref="AccountTokenCheck"/>, so a token is
    /// refused here exactly when production would refuse it.
    /// </remarks>
    public HttpClient Client() => factory.RealAccounts.CreateClient();

    /// <summary>A mobile number nobody else in the database has, as a customer would type it.</summary>
    public static string NewMobile() => $"059{Random.Shared.Next(0, 10_000_000):D7}";

    /// <summary>A Call-ID no other test has used.</summary>
    public static string NewSipCallId() => $"{Guid.NewGuid():N}@test";

    public async Task<User> CreateUserAsync(
        string role = UserRoles.Agent, bool isActive = true, string password = Password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISipSecretProtector>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User
        {
            Login = $"test-{role.ToLowerInvariant()}-{suffix}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = $"Test {role} {suffix}",
            Role = role,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (role == UserRoles.Agent)
        {
            user.Extension = $"9{Random.Shared.Next(1000, 9999)}";
            user.SipSecret = secrets.Protect("sip-secret");
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>A contact with the given numbers, stored as typed and normalised as the server would.</summary>
    public async Task<Contact> CreateContactAsync(string? name, params string[] numbers)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var now = DateTimeOffset.UtcNow;
        var contact = new Contact { Name = name, CreatedAt = now, UpdatedAt = now };

        foreach (var (number, i) in numbers.Select((n, i) => (n, i)))
        {
            contact.Phones.Add(new ContactPhone
            {
                Raw = number,
                Normalised = PhoneNormalizer.Normalize(number),
                IsPrimary = i == 0,
                CreatedAt = now,
            });
        }

        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        // Contacts have no test-only pattern, so the sweep is told about them.
        TestSweeper.Contacts.Add(contact.Id);
        return contact;
    }

    /// <summary>
    /// The Phone channel every call is filed under. A migrated database has no
    /// rows until <c>seed</c> runs, and CI's database is only migrated.
    /// </summary>
    public async Task EnsurePhoneChannelAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        if (!await db.Channels.AnyAsync(c => c.Name == ChannelNames.Phone))
        {
            db.Channels.Add(new Channel { Name = ChannelNames.Phone, IsSystem = true });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Signs in through <c>POST /api/auth/login</c>, as the app for that role
    /// does, and returns a client carrying the token.
    /// </summary>
    public async Task<(HttpClient Client, LoginResponse Login)> SignInAsync(User user)
    {
        var client = Client();

        var request = user.Role == UserRoles.Agent
            ? new LoginRequest(user.Login, Password, LaptopId, "test")
            : new LoginRequest(user.Login, Password);

        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        response.IsSuccessStatusCode.Should().BeTrue($"{user.Login} should be able to sign in");

        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login);
    }

    /// <summary>Runs <paramref name="query"/> against a fresh context, so it reads what was saved.</summary>
    public async Task<T> QueryAsync<T>(Func<CallCenterDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<CallCenterDbContext>());
    }
}
