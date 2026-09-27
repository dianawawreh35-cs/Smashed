using CallCenter.Server.Features.Communications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The recordings folder's guard (27 Sep review): a path that climbs out of
/// the folder is refused, including into a sibling whose name starts the same.
/// </summary>
public class RecordingPathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "callcenter-guard", "recordings");

    private static RecordingStore Store() =>
        new(Options.Create(new RecordingOptions { Path = Root }), NullLogger<RecordingStore>.Instance);

    [Theory]
    [InlineData("../recordings-old/x.wav")]
    [InlineData("../x.wav")]
    [InlineData("2026/../../outside.wav")]
    public void A_path_out_of_the_folder_is_refused(string relative)
    {
        var act = () => Store().Open(relative);

        act.Should().Throw<InvalidOperationException>().WithMessage("*outside the recordings folder*");
    }

    [Fact]
    public void A_path_inside_it_is_looked_up()
    {
        Store().Open("2026/09/27/nothing-here.wav").Should().BeNull("it is inside the folder, and not there");
    }
}
