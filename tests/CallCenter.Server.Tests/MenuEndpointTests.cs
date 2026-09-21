using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Data.Seed;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Who may read the menu and who may change it (A-66, S-59).
/// </summary>
/// <remarks>
/// Agents quote prices from this mid-call. A price an agent can edit is a price
/// the restaurant does not control, so the door matters as much as the content.
/// These run without PostgreSQL and stop at authorization and validation.
/// </remarks>
[Collection(ApiCollection.Name)]
public class MenuEndpointTests(CallCenterApiFactory factory)
{
    private const string SomeItem = "/api/menu/11111111-1111-1111-1111-111111111111";

    public static TheoryData<string, string> SupervisorOnlyEndpoints => new()
    {
        { "POST", "/api/menu" },
        { "PUT", SomeItem },
        { "DELETE", SomeItem },
        { "POST", "/api/menu/categories" },
        { "DELETE", "/api/menu/categories/11111111-1111-1111-1111-111111111111" },
    };

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task Without_a_token_every_write_is_401(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task An_agent_cannot_change_the_menu(string method, string path)
    {
        var response = await ClientFor(UserRoles.Agent).SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/menu")]
    [InlineData("/api/menu?q=برغر")]
    [InlineData("/api/menu/categories")]
    public async Task An_agent_may_read_the_menu(string path)
    {
        // A-66: this is the whole feature for an agent mid-call.
        var response = await ClientFor(UserRoles.Agent).GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_item_with_no_name_is_refused_before_the_database()
    {
        var response = await ClientFor(UserRoles.Supervisor).PostAsJsonAsync(
            "/api/menu", new { categoryId = Guid.NewGuid(), name = "", price = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_negative_price_is_refused_before_the_database()
    {
        // Zero is meant - a free extra - so only negatives are wrong.
        var response = await ClientFor(UserRoles.Supervisor).PostAsJsonAsync(
            "/api/menu", new { categoryId = Guid.NewGuid(), name = "Something", price = -5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_missing_picture_is_a_404_rather_than_an_empty_body()
    {
        // The Agent App asks for one per row; an empty 200 would be cached as a
        // valid picture and the row would show a broken image for a day.
        var response = await ClientFor(UserRoles.Agent).GetAsync($"{SomeItem}/image");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- the seed data, which is where the menu actually comes from ----------

    [Fact]
    public void Every_menu_item_names_a_seeded_category()
    {
        // A mismatch is skipped with a warning at install time, which nobody
        // reads. The item would simply not be on the menu.
        var categories = SeedData.MenuCategories.ToHashSet();

        SeedData.MenuItems
            .Where(i => !categories.Contains(i.Category))
            .Select(i => i.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public void No_item_is_listed_twice_within_a_category()
    {
        // The unique index would refuse the second, losing it silently.
        SeedData.MenuItems
            .GroupBy(i => (i.Category, Shared.Text.NameNormalizer.Normalize(i.Name)))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Item2)
            .Should().BeEmpty();
    }

    [Fact]
    public void No_seeded_price_is_negative()
    {
        SeedData.MenuItems
            .Where(i => i.Price < 0 || i.MealPrice < 0)
            .Should().BeEmpty();
    }

    [Fact]
    public void A_meal_costs_more_than_the_sandwich_alone()
    {
        // The meal adds fries and a drink. A meal price at or below the sandwich
        // is a transcription error, and an agent would quote it without blinking.
        SeedData.MenuItems
            .Where(i => i.MealPrice is not null && i.Price is not null && i.MealPrice <= i.Price)
            .Select(i => i.Name)
            .Should().BeEmpty();
    }

    [Fact]
    public void Only_add_ons_are_marked_as_surcharges()
    {
        // A surcharge is an amount added to another item. One marked wrongly
        // would be quoted as "+26" for a burger.
        SeedData.MenuItems
            .Where(i => i.IsSurcharge && i.MealPrice is not null)
            .Select(i => i.Name)
            .Should().BeEmpty();
    }

    private HttpClient ClientFor(string role)
    {
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = CallCenterApiFactory.SigningKey }),
            TimeProvider.System);

        var (token, _) = tokens.Issue(
            new User
            {
                Id = Guid.NewGuid(),
                Login = role.ToLowerInvariant(),
                DisplayName = role,
                Role = role,
                PasswordHash = "unused",
            },
            sessionId: null);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
