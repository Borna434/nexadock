using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.UI.Widgets;

/// <summary>
/// Base for everything that lives on the bar. A widget has a compact tile (DataTemplate keyed
/// "{TypeName}.Tile") and optionally a card that opens on click ("{TypeName}.Card").
/// </summary>
public abstract partial class WidgetViewModel : ObservableObject, IDisposable
{
    protected WidgetViewModel(WidgetPlacement placement)
        : this(placement, WidgetCatalog.Get(placement.TypeId)
            ?? throw new ArgumentException($"Unknown widget type '{placement.TypeId}'.", nameof(placement)))
    {
    }

    /// <summary>For built-in helpers that are not in the catalog (e.g. the overflow card).</summary>
    protected WidgetViewModel(WidgetPlacement placement, WidgetDescriptor descriptor)
    {
        Placement = placement;
        Descriptor = descriptor;
    }

    public WidgetPlacement Placement { get; }

    public WidgetDescriptor Descriptor { get; }

    public string InstanceId => Placement.Id;

    public string Name => Descriptor.Name;

    /// <summary>Whether clicking the tile opens a card.</summary>
    public virtual bool HasCard => false;

    /// <summary>Whether the tile shares leftover bar width (flexible space).</summary>
    public virtual bool IsFlexible => false;

    /// <summary>
    /// Whether the tile stays in the middle of the bar, with the tiles before it packed from the
    /// left edge and the tiles after it from the right edge (the Apps widget).
    /// </summary>
    public virtual bool IsCentered => false;

    /// <summary>Whether the tile is drawn on its own glass plate.</summary>
    public virtual bool HasPlate => true;

    /// <summary>Whether the card contains text input (the card may then take keyboard focus).</summary>
    public virtual bool WantsKeyboard => false;

    /// <summary>Whether the tile reacts to the mouse wheel (<see cref="OnWheel"/>).</summary>
    public virtual bool HandlesWheel => false;

    /// <summary>Set while this widget's card is open (tiles can highlight themselves).</summary>
    [ObservableProperty] private bool _isCardOpen;

    /// <summary>False hides the tile entirely (e.g. Pulse with nothing to report).</summary>
    [ObservableProperty] private bool _isVisibleOnBar = true;

    /// <summary>True while a Pulse alert belongs to this widget: the tile glows.</summary>
    [ObservableProperty] private bool _isPulsing;

    public string TypeId => Descriptor.TypeId;

    /// <summary>Called after the UI language changed; refresh any text the view model cached.</summary>
    public virtual void OnLanguageChanged()
    {
    }

    /// <summary>Mouse wheel over the tile; positive = up.</summary>
    public virtual void OnWheel(int delta)
    {
    }

    /// <summary>Called once after the widget is placed on the bar.</summary>
    public virtual void Start()
    {
    }

    /// <summary>Called on every settings change (and once before <see cref="Start"/>).</summary>
    public virtual void ApplySettings(DockSettings settings)
    {
    }

    /// <summary>Called when the bar moves to a monitor with another DPI.</summary>
    public virtual void SetDpiScale(double scale)
    {
    }

    /// <summary>Click on a tile without a card.</summary>
    public virtual void OnClick()
    {
    }

    public virtual void OnCardOpened()
    {
    }

    public virtual void OnCardClosed()
    {
    }

    /// <summary>Extra entries for the tile's right-click menu.</summary>
    public virtual IEnumerable<WidgetMenuItem> GetMenuItems() => [];

    public virtual void Dispose() => GC.SuppressFinalize(this);
}

public sealed record WidgetMenuItem(string Header, Action Execute, bool IsEnabled = true);

/// <summary>Creates widget view models with their services resolved from DI.</summary>
public sealed class WidgetFactory(IServiceProvider services)
{
    private static readonly Dictionary<string, Type> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [WidgetTypes.Apps] = typeof(AppsWidget),
        [WidgetTypes.Clock] = typeof(ClockWidget),
        [WidgetTypes.Calendar] = typeof(CalendarWidget),
        [WidgetTypes.DayProgress] = typeof(DayProgressWidget),
        [WidgetTypes.System] = typeof(SystemWidget),
        [WidgetTypes.Weather] = typeof(WeatherWidget),
        [WidgetTypes.NowPlaying] = typeof(NowPlayingWidget),
        [WidgetTypes.ControlCenter] = typeof(ControlCenterWidget),
        [WidgetTypes.ShowDesktop] = typeof(ShowDesktopWidget),
        [WidgetTypes.Spacer] = typeof(SpacerWidget),
        [WidgetTypes.Pulse] = typeof(PulseWidget),
        [WidgetTypes.Focus] = typeof(FocusWidget),
        [WidgetTypes.Timer] = typeof(TimerWidget),
        [WidgetTypes.Stopwatch] = typeof(StopwatchWidget),
        [WidgetTypes.Todos] = typeof(TodosWidget),
        [WidgetTypes.Notes] = typeof(NotesWidget),
        [WidgetTypes.Volume] = typeof(VolumeWidget),
        [WidgetTypes.Microphone] = typeof(MicrophoneWidget),
        [WidgetTypes.Network] = typeof(NetworkWidget),
        [WidgetTypes.Storage] = typeof(StorageWidget),
        [WidgetTypes.RecycleBin] = typeof(RecycleBinWidget),
        [WidgetTypes.Keyboard] = typeof(KeyboardWidget),
        [WidgetTypes.QuickToggles] = typeof(QuickTogglesWidget),
        [WidgetTypes.Brightness] = typeof(BrightnessWidget),
        [WidgetTypes.Claude] = typeof(ClaudeWidget),
        [WidgetTypes.Agenda] = typeof(AgendaWidget),
    };

    public static bool IsSupported(string typeId) => Types.ContainsKey(typeId);

    public WidgetViewModel? Create(WidgetPlacement placement) =>
        Types.TryGetValue(placement.TypeId, out var type)
            ? (WidgetViewModel)ActivatorUtilities.CreateInstance(services, type, placement)
            : null;
}
