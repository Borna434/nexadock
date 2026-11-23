namespace NexaDock.UI.ViewModels;

/// <summary>
/// Which bar a window and its view model are: the main bar (<see cref="Display"/> null, follows
/// profiles and can be dragged to any display) or the extra bar on one secondary display.
/// </summary>
public sealed record BarTarget(string? Display)
{
    public static BarTarget Main { get; } = new((string?)null);

    public bool IsMain => Display is null;
}
