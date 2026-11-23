using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Models;
using NexaDock.UI.Services;

namespace NexaDock.UI.Widgets;

/// <summary>
/// Notification-area icons of background apps (Discord, Steam, OneDrive…): pinned icons sit on
/// the bar, the rest are one click away in the card, like the Windows 11 overflow.
/// </summary>
public sealed partial class TrayWidget : WidgetViewModel
{
    public TrayWidget(WidgetPlacement placement, TrayIconHost host)
        : base(placement) => Host = host;

    public override bool HasCard => true;

    public TrayIconHost Host { get; }

    /// <summary>While on, clicking an icon in the card pins or unpins it.</summary>
    [ObservableProperty] private bool _isEditing;

    public override void Start() => Host.Attach();

    public override void OnCardOpened() => IsEditing = false;

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;

    public override void Dispose()
    {
        Host.Detach();
        base.Dispose();
    }
}
