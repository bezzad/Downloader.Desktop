using System.Collections.Generic;

namespace Downloader.Desktop.Views;

/// <summary>
/// Which of the downloads grid's optional columns to drop when the columns do not fit (e.g. once the
/// category sidebar takes its share of the window). Without this the grid crushes its fixed columns into
/// each other ("8.01 M3m") and cuts off the row buttons.
/// </summary>
public static class ColumnFit
{
    /// <summary>
    /// How many optional columns to hide, taken in the order given (the first is dropped first), so that
    /// <paramref name="required"/> — the width every column would take — fits <paramref name="available"/>.
    /// </summary>
    public static int CountToHide(double available, double required, IReadOnlyList<double> optionalWidths)
    {
        int hidden = 0;
        while (hidden < optionalWidths.Count && required > available)
            required -= optionalWidths[hidden++];
        return hidden;
    }
}
