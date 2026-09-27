using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CallCenter.AgentApp.Services.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// Text the agent can select and copy: a number, a name, an address, a price,
/// an error to pass on (A-84).
/// </summary>
/// <remarks>
/// A WPF <see cref="TextBlock"/> only draws its text; it cannot be selected,
/// and before 27 Sep nothing on screen could be copied except what was typed
/// into a box. This is a read-only <see cref="TextBox"/> styled in
/// <c>Theme.xaml</c> to look like the TextBlock it replaces: drag or
/// double-click to select, Ctrl+C or right-click to copy.
///
/// <b>Bind <see cref="Value"/>, not <c>Text</c>.</b> A TextBox's Text binds
/// two ways by default, and a two-way binding to a get-only property - most
/// of what is shown here - throws when the screen opens. WPF will not let a
/// subclass switch that default off (the flag merges with the base's), so
/// the text arrives through a property of its own, one way like a
/// TextBlock's. <b>Read-only and out of the Tab order</b>, so it is never
/// mistaken for a box to type in and Tab still goes from field to field.
///
/// Headings, labels and buttons stay TextBlocks: nobody copies them, and a
/// selectable caption only gets in the way of clicking what it names.
/// </remarks>
public class SelectableText : TextBox
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(SelectableText),
        new PropertyMetadata(null, (element, e) => ((SelectableText)element).Text = (string?)e.NewValue ?? string.Empty));

    static SelectableText()
    {
        IsReadOnlyProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(SelectableText), new FrameworkPropertyMetadata(false));
    }

    /// <summary>The text shown. Bound one way, as a TextBlock's Text is.</summary>
    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public SelectableText()
    {
        // Copy and Select all, in the agent's language, instead of the
        // TextBox's own menu with a Cut and a Paste that can never work here.
        ContextMenu = CopyMenu.Create(
            (CopyMenu.CopyKey, ApplicationCommands.Copy, this),
            (CopyMenu.SelectAllKey, ApplicationCommands.SelectAll, this));
    }
}

/// <summary>
/// The right-click menus that copy (A-84): on <see cref="SelectableText"/>,
/// and on the lists through <see cref="GridCopy"/>.
/// </summary>
public static class CopyMenu
{
    public const string CopyKey = "copy.copy";
    public const string SelectAllKey = "copy.selectAll";
    public const string CopyRowKey = "copy.row";

    /// <summary>A menu of command items, labelled when it opens.</summary>
    /// <remarks>
    /// The labels are read each time the menu opens rather than once, so a
    /// change of language mid-shift reaches menus built before it.
    /// </remarks>
    public static ContextMenu Create(params (string Key, ICommand Command, IInputElement Target)[] items)
    {
        var menu = new ContextMenu();

        foreach (var (key, command, target) in items)
        {
            menu.Items.Add(new MenuItem { Command = command, CommandTarget = target, Tag = key });
        }

        menu.Opened += (_, _) => Label(menu);
        return menu;
    }

    /// <summary>Sets every item's header from its key.</summary>
    /// <remarks>
    /// The direction is the language's, not the text's: a phone number reads
    /// left to right, but its menu in Arabic still opens right to left.
    /// </remarks>
    public static void Label(ContextMenu menu)
    {
        var localizer = Localizer();

        if (localizer is not null)
        {
            menu.FlowDirection = localizer.FlowDirection;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string key)
            {
                item.Header = localizer?[key] ?? key;
            }
        }
    }

    /// <summary>The app's localizer, or none while the host is not running (the tests).</summary>
    private static Localizer? Localizer() =>
        Application.Current is App ? App.Services.GetService<Localizer>() : null;
}
