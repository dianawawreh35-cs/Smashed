using CallCenter.Shared.Phone;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>
/// Which callers the Agent App must reject without ringing (A-17).
/// </summary>
/// <remarks>
/// This is the rule with the sharpest consequences in the app: a false positive
/// silently drops a customer's call, and a false negative lets through the
/// caller a supervisor deliberately blocked. It is in Shared precisely so it can
/// be tested here, with no PBX and no laptop.
/// </remarks>
public class BlockListTests
{
    [Theory]
    [InlineData("970599123456")]
    [InlineData("0599123456")]
    [InlineData("+970599123456")]
    [InlineData("00970599123456")]
    [InlineData("0599 123 456")]
    [InlineData("059-912-3456")]
    public void A_blocked_number_is_recognised_however_the_PBX_presents_it(string caller)
    {
        // A-13: one number, many spellings. Without this a blocked nuisance
        // caller gets through simply because the PBX formats their number
        // differently from the way the supervisor typed it.
        var list = new BlockList(["970599123456"]);

        list.IsBlocked(caller).Should().BeTrue();
    }

    [Fact]
    public void A_number_that_is_not_on_the_list_is_not_blocked()
    {
        var list = new BlockList(["970599123456"]);

        list.IsBlocked("970599999999").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("anonymous")]
    [InlineData("withheld")]
    public void A_caller_id_that_says_nothing_is_not_blocked(string? caller)
    {
        // Rejecting what cannot be identified would silently drop every
        // withheld-number call. A-17 is about numbers a supervisor named.
        var list = new BlockList(["970599123456"]);

        list.IsBlocked(caller).Should().BeFalse();
    }

    [Fact]
    public void An_empty_list_blocks_nobody()
    {
        // What the app holds before its first refresh, and after a refresh that
        // legitimately returned nothing.
        BlockList.Empty.IsBlocked("970599123456").Should().BeFalse();
    }

    [Fact]
    public void A_short_extension_does_not_block_every_number_ending_the_same_way()
    {
        // The last-nine-digits fallback must not apply to extensions: indexing
        // "2001" by its tail would block any number ending 2001, and blocking a
        // branch by accident is expensive.
        var list = new BlockList(["2001"]);

        list.IsBlocked("2001").Should().BeTrue();
        list.IsBlocked("970599902001").Should().BeFalse();
    }

    [Fact]
    public void Entries_that_are_not_numbers_are_dropped_rather_than_trusted()
    {
        // A malformed cache file on a laptop must not be able to block calls.
        var list = new BlockList(["", "   ", "not-a-number", "970599123456"]);

        list.Count.Should().Be(1);
        list.IsBlocked("0599123456").Should().BeTrue();
    }

    [Fact]
    public void The_same_number_twice_is_counted_once()
    {
        var list = new BlockList(["970599123456", "0599123456", "+970599123456"]);

        list.Count.Should().Be(1);
    }
}
