using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Delivery;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The branch price-list paste (S-58), posted for real against the database.
/// </summary>
/// <remarks>
/// F-04 of the 27 Sep review: the import opened its own transaction outside
/// the retrying execution strategy, which EF refuses, so it answered 500 every
/// time. The only test of the route checked who may call it, which stops at
/// the door and never reached the transaction.
/// </remarks>
[Collection(ApiCollection.Name)]
public class DeliveryImportTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task A_pasted_list_is_added_and_a_second_paste_updates_it()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var branchId = await BranchAsync(suffix);

        var first = await supervisor.PostAsJsonAsync("/api/delivery-areas/import", new ImportDeliveryAreasRequest(
            branchId, $"Area one {suffix}\t10\r\nArea two {suffix},12.5\n\nno price here"));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var result = (await first.Content.ReadFromJsonAsync<ImportDeliveryAreasResult>())!;
        result.Added.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Problems.Should().ContainSingle(p => p.Reason == "no_price" && p.Line == 4);

        var second = await supervisor.PostAsJsonAsync("/api/delivery-areas/import", new ImportDeliveryAreasRequest(
            branchId, $"Area one {suffix}\t11"));

        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        var again = (await second.Content.ReadFromJsonAsync<ImportDeliveryAreasResult>())!;
        again.Added.Should().Be(0);
        again.Updated.Should().Be(1);

        var prices = await data.QueryAsync(db => db.DeliveryAreas
            .Where(a => a.BranchId == branchId)
            .OrderBy(a => a.Name)
            .Select(a => a.Price)
            .ToListAsync());

        prices.Should().Equal(11m, 12.5m);
    }

    [DatabaseFact]
    public async Task Replace_empties_the_branch_first_and_another_branchs_area_is_refused_not_moved()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var mine = await BranchAsync(suffix);
        var theirs = await BranchAsync(Guid.NewGuid().ToString("N")[..8]);

        (await supervisor.PostAsJsonAsync("/api/delivery-areas/import", new ImportDeliveryAreasRequest(
            mine, $"Old area {suffix}\t5"))).EnsureSuccessStatusCode();
        (await supervisor.PostAsJsonAsync("/api/delivery-areas/import", new ImportDeliveryAreasRequest(
            theirs, $"Held elsewhere {suffix}\t7"))).EnsureSuccessStatusCode();

        var response = await supervisor.PostAsJsonAsync("/api/delivery-areas/import", new ImportDeliveryAreasRequest(
            mine, $"New area {suffix}\t6\nHeld elsewhere {suffix}\t8", Replace: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<ImportDeliveryAreasResult>())!;
        result.Removed.Should().Be(1);
        result.Added.Should().Be(1);
        result.Problems.Should().ContainSingle(p => p.Reason == "other_branch");

        var rows = await data.QueryAsync(db => db.DeliveryAreas
            .Where(a => a.BranchId == mine || a.BranchId == theirs)
            .Select(a => new { a.Name, a.BranchId, a.Price })
            .ToListAsync());

        rows.Should().BeEquivalentTo(new[]
        {
            new { Name = $"New area {suffix}", BranchId = mine, Price = 6m },
            new { Name = $"Held elsewhere {suffix}", BranchId = theirs, Price = 7m },
        });
    }

    private Task<Guid> BranchAsync(string suffix) => data.QueryAsync(async db =>
    {
        var branch = new Branch { Name = $"Test branch {suffix}" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        return branch.Id;
    });
}
