using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace Downloader.Desktop.Views;

public partial class SettingView : UserControl
{
    public SettingView()
    {
        InitializeComponent();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

    /// <summary>
    /// Settings search (#15): shows only the option rows whose visible text contains the term, hides
    /// sections with no match, and auto-expands sections that have one. Works on the rendered
    /// (localized) text, so it searches whatever language the UI is in. Empty term restores everything.
    /// Internal so the headless UI test can drive it directly.
    /// </summary>
    internal void ApplyFilter(string term)
    {
        term = term?.Trim();
        var showAll = string.IsNullOrEmpty(term);

        foreach (var child in SectionsPanel.Children)
        {
            switch (child)
            {
                case Expander section:
                {
                    // The section header itself matching (e.g. "plugins") shows the whole section.
                    var headerMatch = !showAll && Contains(section.Header?.ToString(), term);
                    var any = FilterRows(section, term, showAll || headerMatch);
                    section.IsVisible = showAll || headerMatch || any;
                    if (!showAll && section.IsVisible)
                        section.IsExpanded = true; // a hit inside a collapsed section must become visible
                    break;
                }
                case Border footer: // the About card — no option rows, match on its own text
                    footer.IsVisible = showAll || footer.GetLogicalDescendants().OfType<TextBlock>()
                        .Any(tb => Contains(tb.Text, term));
                    break;
            }
        }
    }

    /// <summary>Sets each option row's (Grid.field) visibility; returns true when any row matches.
    /// Non-row content (e.g. the Logging buttons) follows the section as a whole.</summary>
    private static bool FilterRows(Expander section, string term, bool showAll)
    {
        var any = false;
        foreach (var row in section.GetLogicalDescendants().OfType<Grid>()
                     .Where(g => g.Classes.Contains("field")))
        {
            var match = showAll || row.GetLogicalDescendants().OfType<TextBlock>()
                .Any(tb => Contains(tb.Text, term));
            row.IsVisible = match;
            any |= match;
        }
        return any;
    }

    private static bool Contains(string text, string term) =>
        !string.IsNullOrEmpty(text) && text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
