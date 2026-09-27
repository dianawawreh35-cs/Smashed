using System.Net;
using System.Net.Http.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Two supervisors publishing a form at once (S-40, M-S05 of the 27 Sep
/// review): both succeed, and the direction is never left without a form.
/// </summary>
/// <remarks>
/// It publishes the outbound form as it already is, twice, and puts the
/// previous current row back afterwards, so no other test sees a change.
/// </remarks>
[Collection(ApiCollection.Name)]
public class FormPublishTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task Publishing_at_the_same_moment_gives_two_versions_and_exactly_one_current()
    {
        var (one, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var (two, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var form = (await one.GetFromJsonAsync<ClassificationFormDto>($"/api/classifications/form?direction={Directions.Out}"))!;

        var (highest, previous) = await data.QueryAsync(async db => (
            await db.FormDefinitions.MaxAsync(f => (int?)f.Version) ?? 0,
            await db.FormDefinitions.Where(f => f.IsCurrent && f.Direction == Directions.Out).Select(f => (Guid?)f.Id).FirstOrDefaultAsync()));

        try
        {
            for (var round = 0; round < 3; round++)
            {
                var responses = await Task.WhenAll(new[] { one, two }.Select(client =>
                    client.PutAsJsonAsync("/api/classifications/form", new PublishFormRequest(form.Definition, Directions.Out))));

                foreach (var response in responses)
                {
                    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                }

                (await data.QueryAsync(db => db.FormDefinitions.CountAsync(f => f.IsCurrent && f.Direction == Directions.Out)))
                    .Should().Be(1, "a direction always has exactly one current form");
            }

            var published = await data.QueryAsync(db => db.FormDefinitions
                .Where(f => f.Version > highest && f.Direction == Directions.Out)
                .Select(f => f.Version)
                .ToListAsync());
            published.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        }
        finally
        {
            await data.QueryAsync(async db =>
            {
                await db.FormDefinitions.Where(f => f.Version > highest && f.Direction == Directions.Out).ExecuteDeleteAsync();
                return previous is { } id
                    ? await db.FormDefinitions.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.IsCurrent, true))
                    : 0;
            });
        }
    }
}
