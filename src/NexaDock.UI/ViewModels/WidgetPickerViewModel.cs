using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;
using NexaDock.UI.Localization;

namespace NexaDock.UI.ViewModels;

public sealed partial class WidgetOptionViewModel(WidgetDescriptor descriptor, Action<WidgetOptionViewModel> toggle, Action<WidgetOptionViewModel> addAnother)
    : ObservableObject
{
    public WidgetDescriptor Descriptor { get; } = descriptor;
    public string Name => Loc.T($"widget.{Descriptor.TypeId}.name");
    public string Description => Loc.T($"widget.{Descriptor.TypeId}.desc");
    public string Glyph => Descriptor.Glyph;
    public string Category => Descriptor.Category.ToString();
    public string CategoryLabel => Loc.T($"category.{Category}");
    public bool AllowMultiple => Descriptor.AllowMultiple;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded), nameof(ActionText))]
    private int _count;

    public bool IsAdded => Count > 0;

    public string ActionText => IsAdded ? (AllowMultiple ? Loc.F("picker.removeOne", Count) : Loc.T("picker.remove")) : Loc.T("picker.add");

    [RelayCommand]
    private void Toggle() => toggle(this);

    [RelayCommand]
    private void AddAnother() => addAnother(this);
}

/// <summary>Browse widgets by category or search; adding or removing applies to the bar immediately.</summary>
public sealed partial class WidgetPickerViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly List<WidgetOptionViewModel> _all;

    public WidgetPickerViewModel(ISettingsService settings)
    {
        _settings = settings;
        _all = WidgetCatalog.All.Select(d => new WidgetOptionViewModel(d, Toggle, AddAnother)).ToList();
        Categories = [new CategoryOption("All"), .. WidgetCatalog.All.Select(d => d.Category.ToString()).Distinct().Select(c => new CategoryOption(c))];
        _selectedCategory = Categories[0];
        Sync(_settings.Current);
        Filter();
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>The secondary display whose bar is edited; null edits the main bar.</summary>
    public string? Display { get; private set; }

    /// <summary>Shown under the title when the picker edits a secondary display's bar.</summary>
    public string TargetText => Display is null ? string.Empty : Loc.F("picker.forDisplay", DisplayLabel(Display));

    public void SetDisplay(string? display)
    {
        Display = display;
        OnPropertyChanged(nameof(TargetText));
        Sync(_settings.Current);
    }

    private static string DisplayLabel(string device)
    {
        var digits = new string(device.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : device;
    }

    private IReadOnlyList<WidgetPlacement> Layout(DockSettings s) => Core.Layout.BarLayouts.Get(s, Display);

    private DockSettings With(DockSettings s, IReadOnlyList<WidgetPlacement> list) => Core.Layout.BarLayouts.With(s, Display, list);

    public IReadOnlyList<CategoryOption> Categories { get; }

    public ObservableCollection<WidgetOptionViewModel> Visible { get; } = [];

    [ObservableProperty] private CategoryOption _selectedCategory;
    [ObservableProperty] private string _search = string.Empty;

    partial void OnSelectedCategoryChanged(CategoryOption value) => Filter();

    partial void OnSearchChanged(string value) => Filter();

    private void Filter()
    {
        var term = Search.Trim();
        Visible.Clear();
        foreach (var o in _all.Where(o =>
                     (SelectedCategory is null || SelectedCategory.Id == "All" || o.Category == SelectedCategory.Id)
                     && (term.Length == 0
                         || o.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || o.Description.Contains(term, StringComparison.OrdinalIgnoreCase))))
        {
            Visible.Add(o);
        }
    }

    private void Toggle(WidgetOptionViewModel option)
    {
        var type = option.Descriptor.TypeId;
        _settings.Update(s =>
        {
            var list = Layout(s).ToList();
            var existing = list.FindLastIndex(w => w.TypeId == type);
            if (existing >= 0)
            {
                list.RemoveAt(existing);
            }
            else
            {
                // A new widget lands where it belongs in the arranged layout, not at the far end.
                list.Insert(WidgetArranger.InsertIndex(list, type), WidgetPlacement.Create(type));
            }

            return With(s, list);
        });
    }

    private void AddAnother(WidgetOptionViewModel option) =>
        _settings.Update(s =>
        {
            var type = option.Descriptor.TypeId;
            var list = Layout(s).ToList();
            list.Insert(WidgetArranger.InsertIndex(list, type), WidgetPlacement.Create(type));
            return With(s, list);
        });

    [RelayCommand]
    private void RestoreDefaultLayout() =>
        _settings.Update(s => With(s, Display is null ? WidgetCatalog.DefaultLayout() : WidgetCatalog.SecondaryLayout(Display)));

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Sync(e.Current);

    private void Sync(DockSettings s)
    {
        foreach (var o in _all)
        {
            o.Count = Layout(s).Count(w => w.TypeId == o.Descriptor.TypeId);
        }
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}

public sealed record CategoryOption(string Id)
{
    public string Label => Loc.T($"category.{Id}");
}
