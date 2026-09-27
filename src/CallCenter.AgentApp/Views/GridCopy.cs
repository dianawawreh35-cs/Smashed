using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// Right-click a list's cell to copy it, or its whole row (A-84).
/// </summary>
/// <remarks>
/// The lists (call log, App logs, contacts) keep their TextBlocks rather than
/// taking <see cref="SelectableText"/>: a row is clicked to choose it and
/// double-clicked to open it, and a text box in every cell would take both
/// clicks for itself. So the copy is a menu instead. Switched on for every
/// DataGrid by the theme.
///
/// What a cell copies is its column's clipboard value: the bound text for a
/// text column, <c>ClipboardContentBinding</c> for a template column, and
/// nothing for a column of icons or buttons. Ctrl+C on a chosen row still does
/// what WPF does, the whole row.
/// </remarks>
public static class GridCopy
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridCopy), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>The cell last right-clicked, for the menu that opens from it.</summary>
    private static readonly DependencyProperty ClickedCellProperty = DependencyProperty.RegisterAttached(
        "ClickedCell", typeof(DataGridCell), typeof(GridCopy));

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid)
        {
            return;
        }

        grid.PreviewMouseRightButtonDown -= OnRightButtonDown;
        grid.ContextMenuOpening -= OnMenuOpening;

        if (e.NewValue is true)
        {
            grid.PreviewMouseRightButtonDown += OnRightButtonDown;
            grid.ContextMenuOpening += OnMenuOpening;
            grid.ContextMenu = BuildMenu(grid);
        }
        else
        {
            grid.ContextMenu = null;
        }
    }

    private static ContextMenu BuildMenu(DataGrid grid)
    {
        var copyCell = new MenuItem { Tag = CopyMenu.CopyKey };
        copyCell.Click += (_, _) => Copy(CellText(Clicked(grid)));

        var copyRow = new MenuItem { Tag = CopyMenu.CopyRowKey };
        copyRow.Click += (_, _) => Copy(RowText(Clicked(grid)));

        var menu = new ContextMenu();
        menu.Items.Add(copyCell);
        menu.Items.Add(copyRow);
        menu.Opened += (_, _) => CopyMenu.Label(menu);
        return menu;
    }

    /// <summary>
    /// Remembers the cell, and chooses its row, so the agent can see which one
    /// the menu is about.
    /// </summary>
    private static void OnRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        var cell = Ancestor<DataGridCell>(e.OriginalSource as DependencyObject);
        grid.SetValue(ClickedCellProperty, cell);

        if (cell is not null && DataGridRow.GetRowContainingElement(cell) is { } row)
        {
            grid.SelectedItem = row.Item;
        }
    }

    /// <summary>
    /// No menu off the rows (a header, the empty space under the last row),
    /// and no Copy on a cell with nothing to copy.
    /// </summary>
    private static void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var grid = (DataGrid)sender;
        var cell = Clicked(grid);

        if (cell is null || grid.ContextMenu is not { } menu)
        {
            e.Handled = true;
            return;
        }

        var hasCell = !string.IsNullOrWhiteSpace(CellText(cell));
        ((MenuItem)menu.Items[0]).Visibility = hasCell ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DataGridCell? Clicked(DataGrid grid) => (DataGridCell?)grid.GetValue(ClickedCellProperty);

    private static string? CellText(DataGridCell? cell) =>
        cell?.Column is { } column && DataGridRow.GetRowContainingElement(cell) is { } row
            ? column.OnCopyingCellClipboardContent(row.Item)?.ToString()
            : null;

    /// <summary>Every cell of the row that has text, tab-separated as a spreadsheet pastes it.</summary>
    private static string? RowText(DataGridCell? cell)
    {
        if (cell is null || DataGridRow.GetRowContainingElement(cell) is not { } row
            || ItemsControl.ItemsControlFromItemContainer(row) is not DataGrid grid)
        {
            return null;
        }

        var values = grid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => c.OnCopyingCellClipboardContent(row.Item)?.ToString())
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join('\t', values);
    }

    private static void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Another program is holding the clipboard open. Rare, gone in a
            // moment, and not worth an error in the middle of a call: the
            // agent sees nothing pasted and copies again.
        }
    }

    /// <summary>
    /// The nearest <typeparamref name="T"/> above an element. The click may land
    /// on a Run inside a TextBlock, which is not in the visual tree.
    /// </summary>
    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }
}
