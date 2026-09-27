using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Settings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// <c>/api/settings</c> (S-47): who may use it, all or nothing, the audit, and
/// one rule for a key's case (M-S07). There was no test of it before 27 Sep.
/// </summary>
/// <remarks>
/// Two settings nothing in the tests reads, <c>sla.answer_seconds</c> (R-21 is
/// not built) and <c>agent.idle_logout_minutes</c>, are changed and put back
/// row for row afterwards.
/// </remarks>
[Collection(ApiCollection.Name)]
public class SettingsApiTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private static readonly string[] Keys = ["sla.answer_seconds", "agent.idle_logout_minutes"];

    [DatabaseFact]
    public async Task Only_a_supervisor_reads_or_changes_the_settings()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await factory.CreateClient().GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await agent.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PutAsJsonAsync("/api/settings", Values(("sla.answer_seconds", "30"))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [DatabaseFact]
    public async Task One_bad_value_saves_nothing()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        await KeepingTheSettingsAsync(async () =>
        {
            var before = await ValuesAsync();

            var response = await supervisor.PutAsJsonAsync("/api/settings",
                Values(("sla.answer_seconds", "33"), ("agent.idle_logout_minutes", "not a number"), ("no.such.key", "1")));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("invalid_settings").And.Contain("agent.idle_logout_minutes").And.Contain("no.such.key");
            (await ValuesAsync()).Should().Equal(before, "the good value in the same request was not saved either");
        });
    }

    [DatabaseFact]
    public async Task A_change_is_audited_with_before_and_after_and_an_unchanged_value_is_not()
    {
        var supervisorUser = await data.CreateUserAsync(UserRoles.Supervisor);
        var (supervisor, _) = await data.SignInAsync(supervisorUser);

        await KeepingTheSettingsAsync(async () =>
        {
            (await supervisor.PutAsJsonAsync("/api/settings", Values(("sla.answer_seconds", "41"))))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await supervisor.PutAsJsonAsync("/api/settings", Values(("sla.answer_seconds", "41"))))
                .StatusCode.Should().Be(HttpStatusCode.OK);

            var audit = await data.QueryAsync(db => db.AuditLog.AsNoTracking()
                .Where(a => a.UserId == supervisorUser.Id && a.Entity == "settings")
                .ToListAsync());

            audit.Should().ContainSingle("saving the same value again changes nothing");
            audit[0].After!.RootElement.GetProperty("sla.answer_seconds").GetString().Should().Be("41");

            var list = await supervisor.GetFromJsonAsync<List<SettingDto>>("/api/settings");
            list!.Single(s => s.Key == "sla.answer_seconds").UpdatedByDisplayName.Should().Be(supervisorUser.DisplayName);
        });
    }

    [DatabaseFact]
    public async Task A_key_in_another_case_changes_the_one_row_rather_than_adding_a_second()
    {
        // M-S07: "SLA.ANSWER_SECONDS" found no row and inserted a duplicate, a 500.
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        await KeepingTheSettingsAsync(async () =>
        {
            (await supervisor.PutAsJsonAsync("/api/settings", Values(("sla.answer_seconds", "42")))).EnsureSuccessStatusCode();

            var response = await supervisor.PutAsJsonAsync("/api/settings", Values(("SLA.ANSWER_SECONDS", "43")));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var rows = await data.QueryAsync(db => db.Settings.AsNoTracking()
                .Where(s => s.Key.ToLower() == "sla.answer_seconds").ToListAsync());
            rows.Should().ContainSingle().Which.Should().Match<Setting>(s => s.Key == "sla.answer_seconds" && s.Value == "43");
        });
    }

    private static UpdateSettingsRequest Values(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value));

    private Task<List<string>> ValuesAsync() =>
        data.QueryAsync(db => db.Settings.AsNoTracking()
            .Where(s => Keys.Contains(s.Key))
            .OrderBy(s => s.Key)
            .Select(s => s.Key + "=" + s.Value)
            .ToListAsync());

    /// <summary>Runs <paramref name="test"/>, then puts the two settings' rows back as they were.</summary>
    private async Task KeepingTheSettingsAsync(Func<Task> test)
    {
        var before = await data.QueryAsync(db => db.Settings.AsNoTracking()
            .Where(s => s.Key.ToLower() == Keys[0] || s.Key.ToLower() == Keys[1]).ToListAsync());
        try
        {
            await test();
        }
        finally
        {
            await data.QueryAsync(async db =>
            {
                await db.Settings.Where(s => s.Key.ToLower() == Keys[0] || s.Key.ToLower() == Keys[1]).ExecuteDeleteAsync();
                db.Settings.AddRange(before.Select(s => new Setting
                {
                    Key = s.Key, Value = s.Value, UpdatedBy = s.UpdatedBy, UpdatedAt = s.UpdatedAt,
                }));
                return await db.SaveChangesAsync();
            });
        }
    }
}
