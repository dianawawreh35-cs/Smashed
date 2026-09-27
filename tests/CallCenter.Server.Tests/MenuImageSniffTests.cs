using System.Text;
using CallCenter.Server.Features.Menu;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A menu picture is taken for what its bytes are, not for the Content-Type
/// the browser sent (27 Sep review).
/// </summary>
public class MenuImageSniffTests
{
    [Fact]
    public void PNG_JPEG_and_WebP_are_recognised_by_their_first_bytes()
    {
        MenuImageStore.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]).Should().Be("image/png");
        MenuImageStore.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]).Should().Be("image/jpeg");
        MenuImageStore.Sniff([.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8]).Should().Be("image/webp");
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF89a")]
    [InlineData("RIFF....WAVE")]
    [InlineData("")]
    public void Anything_else_is_not_a_picture(string content)
    {
        MenuImageStore.Sniff(Encoding.ASCII.GetBytes(content)).Should().BeNull();
    }
}
