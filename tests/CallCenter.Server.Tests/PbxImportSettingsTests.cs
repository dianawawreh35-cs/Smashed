using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Pbx;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The PBX import's address and what a failed check leaves behind (S-55,
/// M-S06 and M-S03 of the 27 Sep review), against a real database.
/// </summary>
/// <remarks>
/// These change the one set of <c>pbx.calls.*</c> settings every test shares,
/// so each puts back exactly the rows it found, whatever happens. The
/// addresses are TEST-NET ones (192.0.2.x), which lead nowhere.
/// </remarks>
[Collection(ApiCollection.Name)]
public class PbxImportSettingsTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task A_new_address_needs_the_password_again_and_http_is_refused()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        await KeepingTheSettingsAsync(async () =>
        {
            (await SaveAsync(supervisor, "https://192.0.2.10", "secret")).PasswordSet.Should().BeTrue();
            (await SaveAsync(supervisor, "https://192.0.2.10/index.php", null)).PasswordSet
                .Should().BeTrue("the same host, pasted with a page on the end, keeps it");

            (await SaveAsync(supervisor, "https://192.0.2.11", null)).PasswordSet
                .Should().BeFalse("a password is never posted to a host it was not typed for");

            var http = await supervisor.PutAsJsonAsync("/api/pbx/abandoned-import",
                new UpdateAbandonedImportRequest("http://192.0.2.11", "someone", "secret", 60));
            http.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await http.Content.ReadAsStringAsync()).Should().Contain("https://");
        });
    }

    [DatabaseFact]
    public async Task An_unexpected_failure_is_recorded_like_any_other()
    {
        // M-S03: only PbxImportException used to be written down, so anything
        // else left the card blank and the day was downloaded again every tick.
        await using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPbxCallsDetailSource>();
            services.AddSingleton<IPbxCallsDetailSource, BrokenPbx>();
        }));
        var supervisorUser = await data.CreateUserAsync(UserRoles.Supervisor);
        var supervisor = host.CreateClient();
        var login = await supervisor.PostAsJsonAsync("/api/auth/login",
            new CallCenter.Shared.Contracts.Auth.LoginRequest(supervisorUser.Login, TestData.Password));
        var token = (await login.Content.ReadFromJsonAsync<CallCenter.Shared.Contracts.Auth.LoginResponse>())!.AccessToken;
        supervisor.DefaultRequestHeaders.Authorization = new("Bearer", token);

        await KeepingTheSettingsAsync(async () =>
        {
            await SaveAsync(supervisor, "https://192.0.2.12", "secret");

            var fetch = await supervisor.PostAsync("/api/pbx/abandoned-import/fetch", null);
            fetch.StatusCode.Should().Be(HttpStatusCode.OK, "a failed check is an answer, not a 500");
            var result = (await fetch.Content.ReadFromJsonAsync<AbandonedFetchResultDto>())!;

            result.Ok.Should().BeFalse();
            result.Error.Should().Contain("unexpectedly").And.Contain("InvalidOperationException");
            result.Status.LastCheckedAt.Should().NotBeNull("so the next check waits its interval");
            result.Status.LastError.Should().Be(result.Error);
        });
    }

    private static async Task<AbandonedImportDto> SaveAsync(HttpClient supervisor, string url, string? password)
    {
        var response = await supervisor.PutAsJsonAsync("/api/pbx/abandoned-import",
            new UpdateAbandonedImportRequest(url, "someone", password, 60));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AbandonedImportDto>())!;
    }

    /// <summary>Runs <paramref name="test"/>, then puts every <c>pbx.calls.*</c> row back as it was.</summary>
    private async Task KeepingTheSettingsAsync(Func<Task> test)
    {
        var before = await data.QueryAsync(db => db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(AbandonedCallImport.Keys.Prefix)).ToListAsync());
        try
        {
            await test();
        }
        finally
        {
            await data.QueryAsync(async db =>
            {
                await db.Settings.Where(s => s.Key.StartsWith(AbandonedCallImport.Keys.Prefix)).ExecuteDeleteAsync();
                db.Settings.AddRange(before.Select(s => new Setting
                {
                    Key = s.Key, Value = s.Value, UpdatedBy = s.UpdatedBy, UpdatedAt = s.UpdatedAt,
                }));
                return await db.SaveChangesAsync();
            });
        }
    }

    private sealed class BrokenPbx : IPbxCallsDetailSource
    {
        public Task<string> DownloadCsvAsync(PbxConnection pbx, DateOnly from, DateOnly to, CancellationToken ct) =>
            throw new InvalidOperationException("something the import did not expect");
    }
}
