using CallCenter.Server.Data;
using CallCenter.Server.Data.Seed;
using CallCenter.Shared.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Running <c>seed</c> again on a server that already has a menu adds the items
/// added to the seed since, and touches nothing else (A-66).
/// </summary>
/// <remarks>
/// The crispy burgers, 27 Sep 2026, were the first items added after the live
/// server was seeded. The seed skipped the whole menu once any item existed,
/// so they could not have reached it.
/// </remarks>
[Collection(ApiCollection.Name)]
public class MenuSeedTests(CallCenterApiFactory factory)
{
    private const string Before = "برغر كلاسيك كرسبي";
    private const string NewCategory = "برغر دجاج كرسبي";
    private const string NewInOldCategory = "دبل كرسبي برغر";
    private const string Repriced = "سنجل سماشد برغر";
    private const string Hidden = "ماء";

    [DatabaseFact]
    public async Task Seeding_again_adds_what_is_missing_and_changes_nothing_else()
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting(
            "MenuImages:Path", Path.Combine(CallCenterApiFactory.RecordingsPath, "menu-images")));

        await SeedMenuAsync(host);

        decimal? originalPrice = null;

        try
        {
            // A database seeded before the crispy burgers, where a supervisor has
            // since repriced one item and hidden another.
            await WithDbAsync(host, async db =>
            {
                var category = await db.MenuCategories.Include(c => c.Items)
                    .SingleAsync(c => c.NameNormalised == NameNormalizer.Normalize(NewCategory));
                db.MenuItems.RemoveRange(category.Items);
                db.MenuCategories.Remove(category);

                db.MenuItems.Remove(await ItemAsync(db, NewInOldCategory));

                var repriced = await ItemAsync(db, Repriced);
                originalPrice = repriced.Price;
                repriced.Price = 99m;

                (await ItemAsync(db, Hidden)).IsActive = false;

                await db.SaveChangesAsync();
            });

            (await SeedMenuAsync(host)).Should().Be(3, "the new category's two burgers, and the one beside them");

            await WithDbAsync(host, async db =>
            {
                var categories = await db.MenuCategories.Include(c => c.Items).AsNoTracking().ToListAsync();

                categories
                    .Where(c => SeedData.MenuCategories.Contains(c.Name))
                    .OrderBy(c => c.SortOrder)
                    .Select(c => c.Name)
                    .Should().Equal(SeedData.MenuCategories, "a new category goes after the one before it in the seed");

                var before = categories.Single(c => c.Name == Before);
                before.Items.MaxBy(i => i.SortOrder)!.Name.Should().Be(NewInOldCategory,
                    "an item added to an existing category goes at its end");

                var added = categories.Single(c => c.Name == NewCategory).Items;
                added.Should().HaveCount(2);
                added.Should().OnlyContain(i => i.ImageFileName != null, "the photographs come with them");

                (await ItemAsync(db, Repriced)).Price.Should().Be(99m, "a supervisor's price is not put back");

                var water = await db.MenuItems
                    .Where(i => i.NameNormalised == NameNormalizer.Normalize(Hidden))
                    .ToListAsync();
                water.Should().ContainSingle().Which.IsActive.Should().BeFalse("a hidden item exists, so it is not added again");
            });

            (await SeedMenuAsync(host)).Should().Be(0, "a second run finds nothing missing");
        }
        finally
        {
            await WithDbAsync(host, async db =>
            {
                var repriced = await ItemAsync(db, Repriced);
                repriced.Price = originalPrice ?? repriced.Price;
                (await ItemAsync(db, Hidden)).IsActive = true;
                await db.SaveChangesAsync();
            });
        }
    }

    private static Task<Data.Entities.MenuItem> ItemAsync(CallCenterDbContext db, string name) =>
        db.MenuItems.SingleAsync(i => i.NameNormalised == NameNormalizer.Normalize(name));

    private static async Task<int> SeedMenuAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        var added = await seeder.SeedMenuAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<CallCenterDbContext>().SaveChangesAsync();
        return added;
    }

    private static async Task WithDbAsync(WebApplicationFactory<Program> host, Func<CallCenterDbContext, Task> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<CallCenterDbContext>());
    }
}
