using NexaDock.Core.Models;
using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

/// <summary>Recent copies on the bar: the tile shows how many, the card lists them to paste.</summary>
public sealed class ClipboardWidget : WidgetViewModel
{
    public ClipboardWidget(WidgetPlacement placement, ClipboardService clipboard)
        : base(placement)
    {
        Clipboard = clipboard;
        Clipboard.PastedFromCard += OnPasted;
    }

    private void OnPasted(object? sender, EventArgs e)
    {
        if (IsCardOpen)
        {
            RequestCloseCard();
        }
    }

    public override void Dispose()
    {
        Clipboard.PastedFromCard -= OnPasted;
        base.Dispose();
    }

    public override bool HasCard => true;

    public override bool WantsKeyboard => true;

    public ClipboardService Clipboard { get; }

    public override void OnCardOpened() => Clipboard.Query = string.Empty;
}
