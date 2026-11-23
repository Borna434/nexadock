using System.Collections.ObjectModel;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.UI.Widgets;

/// <summary>
/// Not a catalog widget: the card behind the bar's "»" button, showing the tiles that did not fit.
/// The tiles are the same live view models, so their buttons keep working inside the card.
/// </summary>
public sealed class OverflowWidget()
    : WidgetViewModel(
        new WidgetPlacement("overflow", "overflow"),
        new WidgetDescriptor("overflow", "More", WidgetCategory.Layout, string.Empty, WidgetCatalog.Glyph(0xE76C)))
{
    public override bool HasCard => true;

    public ObservableCollection<WidgetViewModel> Hidden { get; } = [];

    public void SetHidden(IEnumerable<WidgetViewModel> widgets)
    {
        var list = widgets.ToList();
        if (list.SequenceEqual(Hidden))
        {
            return;
        }

        Hidden.Clear();
        foreach (var w in list)
        {
            Hidden.Add(w);
        }
    }
}
