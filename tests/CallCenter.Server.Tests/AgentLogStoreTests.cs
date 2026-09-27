using System.Text;
using CallCenter.Server.Features.AgentLogs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The server's copy of the Agent Apps' logs (N-12): always a prefix of the
/// laptop's file, whatever arrives twice, and never outside its folder.
/// </summary>
public sealed class AgentLogStoreTests : IDisposable
{
    private const string Laptop = "LAPTOP-01";
    private const string File = "agent-20260927.log";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-logs-" + Guid.NewGuid().ToString("N"));

    private AgentLogStore Store(long maxFileBytes = 1024) => new(
        Options.Create(new AgentLogOptions { Path = _root, MaxFileBytes = maxFileBytes, RetentionDays = 30 }),
        NullLogger<AgentLogStore>.Instance);

    private static ReadOnlyMemory<byte> Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Appends_piece_after_piece()
    {
        var store = Store();

        (await store.AppendAsync(Laptop, File, 0, Bytes("one\n"), default))
            .Should().Be((AgentLogStore.Outcome.Appended, 4L));
        (await store.AppendAsync(Laptop, File, 4, Bytes("two\n"), default))
            .Should().Be((AgentLogStore.Outcome.Appended, 8L));

        System.IO.File.ReadAllText(Path.Combine(_root, Laptop, File)).Should().Be("one\ntwo\n");
        store.Lengths(Laptop).Should().Equal(new Dictionary<string, long> { [File] = 8 });
    }

    [Fact]
    public async Task A_piece_sent_twice_is_written_once()
    {
        var store = Store();
        await store.AppendAsync(Laptop, File, 0, Bytes("one\n"), default);

        // The answer to the first send was lost, and the laptop sends it again.
        (await store.AppendAsync(Laptop, File, 0, Bytes("one\n"), default))
            .Should().Be((AgentLogStore.Outcome.WrongOffset, 4L), "the copy already ends at 4");

        System.IO.File.ReadAllText(Path.Combine(_root, Laptop, File)).Should().Be("one\n");
    }

    [Fact]
    public async Task A_piece_past_the_end_is_refused_with_where_the_copy_ends()
    {
        var store = Store();

        // The server lost its copy; the laptop thought it had 100 bytes.
        (await store.AppendAsync(Laptop, File, 100, Bytes("late\n"), default))
            .Should().Be((AgentLogStore.Outcome.WrongOffset, 0L), "the laptop starts again from 0");
    }

    [Fact]
    public async Task A_full_day_takes_no_more()
    {
        var store = Store(maxFileBytes: 6);
        await store.AppendAsync(Laptop, File, 0, Bytes("one\n"), default);

        (await store.AppendAsync(Laptop, File, 4, Bytes("two\n"), default))
            .Should().Be((AgentLogStore.Outcome.TooLarge, 4L));
    }

    [Theory]
    [InlineData("..", File)]
    [InlineData("../etc", File)]
    [InlineData("a/b", File)]
    [InlineData("C:", File)]
    [InlineData("", File)]
    [InlineData(Laptop, "../agent-20260927.log")]
    [InlineData(Laptop, "passwd")]
    [InlineData(Laptop, "agent-20260927.log\n")]
    [InlineData(Laptop, "agent-2026092.log")]
    public async Task Names_that_are_not_a_laptop_and_a_day_are_refused(string laptop, string file)
    {
        var store = Store();

        (await store.AppendAsync(laptop, file, 0, Bytes("x\n"), default)).Outcome
            .Should().Be(AgentLogStore.Outcome.BadName);

        Directory.Exists(_root).Should().BeFalse("nothing was written anywhere");
    }

    [Fact]
    public async Task Retention_deletes_old_days_and_emptied_folders()
    {
        var store = Store();
        await store.AppendAsync(Laptop, File, 0, Bytes("old\n"), default);
        await store.AppendAsync("LAPTOP-02", "agent-20260926.log", 0, Bytes("old\n"), default);
        await store.AppendAsync("LAPTOP-02", "agent-20260927.log", 0, Bytes("new\n"), default);

        System.IO.File.SetLastWriteTimeUtc(Path.Combine(_root, Laptop, File), DateTime.UtcNow.AddDays(-31));
        System.IO.File.SetLastWriteTimeUtc(Path.Combine(_root, "LAPTOP-02", "agent-20260926.log"), DateTime.UtcNow.AddDays(-31));

        store.DeleteExpired(DateTime.UtcNow).Should().Be(2);

        Directory.Exists(Path.Combine(_root, Laptop)).Should().BeFalse();
        store.Lengths("LAPTOP-02").Keys.Should().Equal("agent-20260927.log");
    }

    [Fact]
    public async Task The_laptop_list_counts_each_day_and_keeps_up_as_it_grows()
    {
        var store = Store(maxFileBytes: 1024 * 1024);
        const string error = "2026-09-27 09:02:00.000 +03:00 [ERR] Broken\n";
        const string warning = "2026-09-27 09:03:00.000 +03:00 [WRN] Odd\n";

        await store.AppendAsync(Laptop, "agent-20260926.log", 0, Bytes(warning), default);
        await store.AppendAsync(Laptop, File, 0, Bytes(error), default);

        var laptop = store.Laptops().Should().ContainSingle().Subject;
        laptop.Laptop.Should().Be(Laptop);
        laptop.Days.Select(d => (d.Date, d.Errors, d.Warnings))
            .Should().Equal(("2026-09-27", 1, 0), ("2026-09-26", 0, 1));

        // Counted again from where it stopped, not from the start.
        await store.AppendAsync(Laptop, File, error.Length, Bytes(error + warning), default);
        store.Laptops()[0].Days[0].Should().BeEquivalentTo(new { Errors = 2, Warnings = 1 });
    }

    [Fact]
    public async Task A_day_reads_back_as_text_and_a_missing_one_as_nothing()
    {
        var store = Store();
        await store.AppendAsync(Laptop, File, 0, Bytes("مرحبا\n"), default);

        (await store.ReadAsync(Laptop, File, default)).Should().Be("مرحبا\n");
        (await store.ReadAsync(Laptop, "agent-20260101.log", default)).Should().BeNull();
        (await store.ReadAsync("..", File, default)).Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
