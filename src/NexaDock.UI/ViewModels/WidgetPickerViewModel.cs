using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.UI.ViewModels;

public sealed partial class WidgetOptionViewModel(WidgetDescriptor descriptor, Action<WidgetOptionViewModel> toggle, Action<WidgetOptionViewModel> addAnother)
    : ObservableObject
{
    public WidgetDescriptor Descriptor { get; } = descriptor;
    public string Name => Descriptor.Name;
    public string Description => Descriptor.Description;
    public string Glyph => Descriptor.Glyph;
    public string Category => Descriptor.Category.ToString();
    public bool AllowMultiple => Descriptor.AllowMultiple;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdded), nameof(ActionText))]
    private int _count;

    public bool IsAdded => Count > 0;

    public string ActionText => IsAdded ? (AllowMultiple ? $"Remove one ({Count})" : "Remove") : "Add";

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
        Categories = ["All", .. WidgetCatalog.All.Select(d => d.Category.ToString()).Distinct()];
        _selectedCategory = "All";
        Sync(_settings.Current);
        Filter();
        _settings.Changed += OnSettingsChanged;
    }

    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<WidgetOptionViewModel> Visible { get; } = [];

    [ObservableProperty] private string _selectedCategory;
    [ObservableProperty] private string _search = string.Empty;

    partial void OnSelectedCategoryChanged(string value) => Filter();

    partial void OnSearchChanged(string value) => Filter();

    private void Filter()
    {
        var term = Search.Trim();
        Visible.Clear();
        foreach (var o in _all.Where(o =>
                     (SelectedCategory == "All" || o.Category == SelectedCategory)
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
            var list = s.Widgets.ToList();
            var existing = list.FindLastIndex(w => w.TypeId == type);
            if (existing >= 0)
            {
                list.RemoveAt(existing);
            }
            else
            {
                list.Add(WidgetPlacement.Create(type));
            }

            return s with { Widgets = list, WidgetsInitialized = true };
        });
    }

    private void AddAnother(WidgetOptionViewModel option) =>
        _settings.Update(s => s with
        {
            Widgets = s.Widgets.Append(WidgetPlacement.Create(option.Descriptor.TypeId)).ToList(),
            WidgetsInitialized = true,
        });

    [RelayCommand]
    private void RestoreDefaultLayout() =>
        _settings.Update(s => s with { Widgets = WidgetCatalog.DefaultLayout(), WidgetsInitialized = true });

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => Sync(e.Current);

    private void Sync(DockSettings s)
    {
        foreach (var o in _all)
        {
            o.Count = s.Widgets.Count(w => w.TypeId == o.Descriptor.TypeId);
        }
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}
