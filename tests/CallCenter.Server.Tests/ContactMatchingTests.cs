using CallCenter.Server.Features.Communications;
using CallCenter.Server.Features.Contacts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Two contacts whose numbers end in the same nine digits (A-13, M-S11 of the
/// 27 Sep review): the caller lands on the same one every time, the one whose
/// primary number matches, else the oldest.
/// </summary>
[Collection(ApiCollection.Name)]
public class ContactMatchingTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task A_primary_number_wins_over_an_older_contact_s_second_number()
    {
        var mobile = TestData.NewMobile();
        var tail = mobile[1..];
        var older = await data.CreateContactAsync("الأقدم", TestData.NewMobile(), $"+972{tail}");
        var newer = await data.CreateContactAsync("الأحدث", $"+44{tail}");
        await AgeAsync(older.Id, days: 30);

        (await MatchAsync(mobile)).Should().Be((newer.Id, newer.Id), "its matching number is its primary one");
    }

    [DatabaseFact]
    public async Task With_no_primary_match_the_oldest_contact_wins()
    {
        var mobile = TestData.NewMobile();
        var tail = mobile[1..];
        var newer = await data.CreateContactAsync("الأحدث", TestData.NewMobile(), $"+44{tail}");
        var older = await data.CreateContactAsync("الأقدم", TestData.NewMobile(), $"+972{tail}");
        await AgeAsync(older.Id, days: 30);

        for (var i = 0; i < 5; i++)
        {
            (await MatchAsync(mobile)).Should().Be((older.Id, older.Id), "every time, not whichever row came back first");
        }
    }

    /// <summary>What the call log (and the POS sync, the same rule) and the pop-up's lookup each pick.</summary>
    private async Task<(Guid? CallLog, Guid? Lookup)> MatchAsync(string number)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var callLog = await scope.ServiceProvider.GetRequiredService<CommunicationsService>().MatchContactAsync(number, default);
        var lookup = await scope.ServiceProvider.GetRequiredService<ContactsService>().FindByPhoneAsync(number);
        return (callLog, lookup?.Id);
    }

    private Task AgeAsync(Guid contactId, int days) =>
        data.QueryAsync(db => db.Contacts.Where(c => c.Id == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, c => c.CreatedAt.AddDays(-days))));
}
