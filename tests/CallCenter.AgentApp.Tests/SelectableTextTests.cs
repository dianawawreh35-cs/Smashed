using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CallCenter.AgentApp.Views;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The text that can be selected and copied (A-84) stays a read-out: it binds
/// as the TextBlock it replaced did, and cannot be typed into.
/// </summary>
public class SelectableTextTests
{
    public class Shown(string name, int orders)
    {
        public string Name { get; } = name;

        public int Orders { get; } = orders;
    }

    [Fact]
    public void Value_binds_to_a_get_only_property_and_shows_it() => UiThread.Run(_ =>
    {
        // Text would bind two ways, as a TextBox's does, and a two-way
        // binding to a get-only property throws when the screen opens.
        var name = new SelectableText { DataContext = new Shown("Sara", 12) };
        var orders = new SelectableText { DataContext = name.DataContext };

        name.SetBinding(SelectableText.ValueProperty, new Binding(nameof(Shown.Name)));
        orders.SetBinding(SelectableText.ValueProperty, new Binding(nameof(Shown.Orders)));

        name.Text.Should().Be("Sara");
        orders.Text.Should().Be("12", "a count is shown as a TextBlock would show it");

        name.DataContext = null;
        name.Text.Should().BeEmpty("nothing to show is an empty box, not a stale name");
        return Task.CompletedTask;
    });

    [Fact]
    public void Is_read_only_and_out_of_the_tab_order() => UiThread.Run(_ =>
    {
        var text = new SelectableText();

        text.IsReadOnly.Should().BeTrue();
        text.IsTabStop.Should().BeFalse();
        return Task.CompletedTask;
    });

    [Fact]
    public void Takes_the_theme_template_and_lines_up_with_a_TextBlock() => UiThread.Run(_ =>
    {
        var theme = (ResourceDictionary)Application.LoadComponent(
            new Uri("/CallCenter.AgentApp;component/Theme.xaml", UriKind.Relative));

        var panel = new StackPanel { Resources = theme };
        var block = new TextBlock { Text = "+970 599 123 456" };
        var text = new SelectableText { Value = "+970 599 123 456" };
        panel.Children.Add(block);
        panel.Children.Add(text);

        // Throws if the Decorator host is refused.
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        text.Template.FindName("PART_ContentHost", text).Should().BeOfType<Decorator>();
        text.DesiredSize.Width.Should().BeApproximately(block.DesiredSize.Width, 0.5);
        text.DesiredSize.Height.Should().BeApproximately(block.DesiredSize.Height, 0.5);
        return Task.CompletedTask;
    });
}
