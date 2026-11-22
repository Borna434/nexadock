using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Models;

namespace NexaDock.UI.ViewModels;

public sealed partial class ShortcutItemViewModel : ObservableObject
{
    private readonly Action<DockShortcut> _launch;

    public ShortcutItemViewModel(DockShortcut shortcut, ImageSource? icon, Action<DockShortcut> launch)
    {
        Shortcut = shortcut;
        _icon = icon;
        _launch = launch;
    }

    public DockShortcut Shortcut { get; }

    public string Name => Shortcut.Name;

    public string Target => Shortcut.Target;

    /// <summary>Shown when the shell cannot supply an icon.</summary>
    public string Monogram => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _icon;

    public bool HasIcon => Icon is not null;

    [RelayCommand]
    private void Launch() => _launch(Shortcut);
}
