using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Features.Websites;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Websites;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The websites inside the Agent App, against a real database (A-88): the
/// supervisor's list, the password that never comes back to the web app, the
/// tabs an Agent App gets, and the cart only one tab may open.
/// </summary>
/// <remarks>
/// Each test makes its own tabs and finds them by a name of its own, so the
/// POS the migration inserts and other tests' rows cannot leak in. A test that
/// sets a cart address puts the POS's aside first and gives it back after.
/// </remarks>
[Collection(ApiCollection.Name)]
public class WebsitesTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private static UpsertWebsiteRequest Shared(string name, string? password = "s3cret!") => new(
        $"موقع {name}", name, "https://example.com/login", WebsiteLogins.Shared, "orders@example.com",
        password, AlertsWithSound: true, CartUrl: null, null, null, null, SortOrder: 50, IsActive: true);

    private static UpsertWebsiteRequest Own(string name) => new(
        $"موقع {name}", name, "https://example.com", WebsiteLogins.Own, null, null,
        AlertsWithSound: false, CartUrl: null, null, null, null, SortOrder: 60, IsActive: true);

    private static string NewName() => $"Site {Guid.NewGuid():N}"[..17];

    [DatabaseFact]
    public async Task A_shared_login_is_stored_encrypted_and_never_sent_back_to_the_web_app()
    {
        var supervisor = await SupervisorAsync();
        var name = NewName();

        var created = await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(name)));

        created.HasPassword.Should().BeTrue();
        created.Username.Should().Be("orders@example.com");

        var listed = await supervisor.GetStringAsync("/api/websites");
        listed.Should().Contain(name).And.NotContain("s3cret!", "the web app is told only that there is one");

        var stored = await data.QueryAsync(db => db.Websites.SingleAsync(w => w.Id == created.Id));
        stored.PasswordSecret.Should().NotBeNull().And.NotContain("s3cret!");

        var audit = await data.QueryAsync(db => db.AuditLog
            .Where(a => a.Entity == WebsitesService.AuditEntity && a.EntityId == created.Id.ToString())
            .SingleAsync());
        audit.After!.RootElement.GetRawText().Should().NotContain("s3cret!").And.Contain("passwordChanged");
    }

    [DatabaseFact]
    public async Task The_agent_app_gets_the_tabs_on_show_with_the_password_and_no_hidden_ones()
    {
        var supervisor = await SupervisorAsync();
        var shown = NewName();
        var hidden = NewName();

        await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(shown)));
        await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(hidden) with { IsActive = false }));

        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var response = await agent.GetAsync("/api/websites/mine");

        response.Headers.CacheControl!.NoStore.Should().BeTrue("the body carries passwords");

        var tabs = (await response.Content.ReadFromJsonAsync<List<AgentWebsiteDto>>())!;
        var tab = tabs.Should().ContainSingle(t => t.NameEn == shown).Subject;
        tab.Password.Should().Be("s3cret!");
        tab.Username.Should().Be("orders@example.com");
        tab.AlertsWithSound.Should().BeTrue();
        tabs.Should().NotContain(t => t.NameEn == hidden);
    }

    [DatabaseFact]
    public async Task A_null_password_keeps_the_stored_one_and_an_empty_one_removes_it()
    {
        var supervisor = await SupervisorAsync();
        var name = NewName();
        var created = await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(name)));

        var renamed = await SaveAsync(supervisor.PutAsJsonAsync($"/api/websites/{created.Id}",
            Shared(name, password: null) with { NameAr = "اسم جديد" }));
        renamed.HasPassword.Should().BeTrue("a save that does not send the password keeps it");
        renamed.NameAr.Should().Be("اسم جديد");

        var cleared = await SaveAsync(supervisor.PutAsJsonAsync($"/api/websites/{created.Id}",
            Shared(name, password: "")));
        cleared.HasPassword.Should().BeFalse();
    }

    [DatabaseFact]
    public async Task An_agents_own_login_keeps_no_username_or_password()
    {
        var supervisor = await SupervisorAsync();
        var name = NewName();
        var created = await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(name)));

        var own = await SaveAsync(supervisor.PutAsJsonAsync($"/api/websites/{created.Id}", Own(name)));

        own.Login.Should().Be(WebsiteLogins.Own);
        own.Username.Should().BeNull();
        own.HasPassword.Should().BeFalse();
    }

    [DatabaseFact]
    public async Task Bad_names_addresses_and_logins_are_refused()
    {
        var supervisor = await SupervisorAsync();
        var good = Shared(NewName());

        (await supervisor.PostAsJsonAsync("/api/websites", good with { NameAr = " " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a blank name is refused by the model's [Required]");
        (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites", good with { Url = "example.com" })))
            .Should().Be((HttpStatusCode.BadRequest, "bad_url"), "an address without https:// is not one a browser can open");
        (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites", good with { Url = "javascript:alert(1)" })))
            .Should().Be((HttpStatusCode.BadRequest, "bad_url"));
        (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites", good with { Username = null })))
            .Should().Be((HttpStatusCode.BadRequest, "bad_login"), "the app cannot sign in with no username");
        (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites", good with { Login = "both" })))
            .Should().Be((HttpStatusCode.BadRequest, "bad_login"));
        (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites", good with { CartUrl = "https://pos.example.com/cart" })))
            .Should().Be((HttpStatusCode.BadRequest, "bad_cart_url"), "a cart address with no {number} opens the same page for every caller");
    }

    [DatabaseFact]
    public async Task Only_one_tab_opens_the_callers_cart()
    {
        var supervisor = await SupervisorAsync();

        // The migration's POS holds the cart; put it aside for the test.
        var others = await data.QueryAsync(db => db.Websites.Where(w => w.CartUrl != null)
            .Select(w => new { w.Id, w.CartUrl }).ToListAsync());
        await SetCartsAsync(others.Select(o => (o.Id, (string?)null)));

        try
        {
            var first = await SaveAsync(supervisor.PostAsJsonAsync("/api/websites",
                Own(NewName()) with { CartUrl = "https://pos.example.com/cart/{number}" }));
            first.CartUrl.Should().Be("https://pos.example.com/cart/{number}");

            (await CodeAsync(supervisor.PostAsJsonAsync("/api/websites",
                    Own(NewName()) with { CartUrl = "https://pos.example.com/cart/{number}" })))
                .Should().Be((HttpStatusCode.Conflict, "cart_taken"));

            (await supervisor.PutAsJsonAsync($"/api/websites/{first.Id}", Own("Kept") with
            {
                CartUrl = "https://pos.example.com/basket/{number}",
            })).IsSuccessStatusCode.Should().BeTrue("the tab that has the cart may change its own address");

            (await supervisor.DeleteAsync($"/api/websites/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        finally
        {
            await SetCartsAsync(others.Select(o => (o.Id, o.CartUrl)));
        }
    }

    [DatabaseFact]
    public async Task A_removed_tab_is_gone_and_its_removal_is_in_the_audit_log()
    {
        var supervisor = await SupervisorAsync();
        var created = await SaveAsync(supervisor.PostAsJsonAsync("/api/websites", Shared(NewName())));

        (await supervisor.DeleteAsync($"/api/websites/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CodeAsync(supervisor.DeleteAsync($"/api/websites/{created.Id}")))
            .Should().Be((HttpStatusCode.NotFound, "website_not_found"));

        (await data.QueryAsync(db => db.AuditLog.AnyAsync(a =>
                a.Entity == WebsitesService.AuditEntity && a.EntityId == created.Id.ToString() && a.Action == "delete")))
            .Should().BeTrue();
    }

    [DatabaseFact]
    public async Task Agents_cannot_list_change_or_remove_tabs()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await agent.GetAsync("/api/websites")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PostAsJsonAsync("/api/websites", Shared(NewName()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.DeleteAsync($"/api/websites/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> SupervisorAsync() =>
        (await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor))).Client;

    private async Task SetCartsAsync(IEnumerable<(Guid Id, string? CartUrl)> carts)
    {
        foreach (var (id, cart) in carts)
        {
            await data.QueryAsync(db => db.Websites.Where(w => w.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.CartUrl, cart)));
        }
    }

    private static async Task<WebsiteDto> SaveAsync(Task<HttpResponseMessage> sending)
    {
        var response = await sending;
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WebsiteDto>())!;
    }

    private static async Task<(HttpStatusCode, string?)> CodeAsync(Task<HttpResponseMessage> sending)
    {
        var response = await sending;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body.TryGetProperty("code", out var code) ? code.GetString() : null);
    }
}
