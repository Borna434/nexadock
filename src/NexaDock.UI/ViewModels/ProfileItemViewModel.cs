using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NexaDock.Core.Models;
using NexaDock.UI.Localization;

namespace NexaDock.UI.ViewModels;

public sealed record LanguageOption(AppLanguage Value, string LabelKey)
{
    public string Label => Loc.T(LabelKey);
}

public sealed record TemplateOption(Core.Profiles.ProfileTemplate Template, string LabelKey)
{
    public string Label => Loc.T(LabelKey);
}

/// <summary>Editable view of one profile; every change is written straight back to settings.</summary>
public sealed partial class ProfileItemViewModel : ObservableObject
{
    private readonly Action<DockProfile> _update;
    private DockProfile _profile;
    private bool _loading;

    public ProfileItemViewModel(DockProfile profile, bool isActive, Action<DockProfile> update)
    {
        _update = update;
        _profile = profile;
        Load(profile, isActive);
    }

    public static IReadOnlyList<string> TimeOptions { get; } =
        Enumerable.Range(0, 48).Select(i => $"{i / 2:00}:{(i % 2) * 30:00}").ToList();

    public string Id => _profile.Id;

    public DockProfile Profile => _profile;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _scheduleEnabled;
    [ObservableProperty] private string _from = "09:00";
    [ObservableProperty] private string _to = "17:00";
    [ObservableProperty] private bool _monday;
    [ObservableProperty] private bool _tuesday;
    [ObservableProperty] private bool _wednesday;
    [ObservableProperty] private bool _thursday;
    [ObservableProperty] private bool _friday;
    [ObservableProperty] private bool _saturday;
    [ObservableProperty] private bool _sunday;
    [ObservableProperty] private string _appsText = string.Empty;

    public string Summary => _profile.Trigger switch
    {
        { ScheduleEnabled: true } t => $"{Format(t.FromMinutes)}–{Format(t.ToMinutes)}{(t.Apps.Count > 0 ? " · " + string.Join(", ", t.Apps) : string.Empty)}",
        { Apps.Count: > 0 } t => string.Join(", ", t.Apps),
        _ => Loc.T("profiles.manual"),
    };

    public string WidgetCountText => Loc.F("profiles.widgetCount", _profile.Widgets.Count);

    public void Load(DockProfile profile, bool isActive)
    {
        _loading = true;
        try
        {
            _profile = profile;
            Name = profile.Name;
            IsActive = isActive;
            var t = profile.Trigger;
            ScheduleEnabled = t.ScheduleEnabled;
            From = Format(t.FromMinutes);
            To = Format(t.ToMinutes);
            Monday = t.Days.Contains(DayOfWeek.Monday);
            Tuesday = t.Days.Contains(DayOfWeek.Tuesday);
            Wednesday = t.Days.Contains(DayOfWeek.Wednesday);
            Thursday = t.Days.Contains(DayOfWeek.Thursday);
            Friday = t.Days.Contains(DayOfWeek.Friday);
            Saturday = t.Days.Contains(DayOfWeek.Saturday);
            Sunday = t.Days.Contains(DayOfWeek.Sunday);
            AppsText = string.Join(", ", t.Apps);
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(WidgetCountText));
        }
        finally
        {
            _loading = false;
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(IsActive) or nameof(Summary) or nameof(WidgetCountText))
        {
            return;
        }

        var days = new List<DayOfWeek>();
        void Add(bool on, DayOfWeek d)
        {
            if (on)
            {
                days.Add(d);
            }
        }

        Add(Monday, DayOfWeek.Monday);
        Add(Tuesday, DayOfWeek.Tuesday);
        Add(Wednesday, DayOfWeek.Wednesday);
        Add(Thursday, DayOfWeek.Thursday);
        Add(Friday, DayOfWeek.Friday);
        Add(Saturday, DayOfWeek.Saturday);
        Add(Sunday, DayOfWeek.Sunday);

        var apps = AppsText.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _profile = _profile with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? _profile.Name : Name.Trim(),
            Trigger = new ProfileTrigger(ScheduleEnabled, Parse(From), Parse(To), days, apps),
        };
        _update(_profile);
    }

    private static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private static int Parse(string? hhmm) =>
        TimeOnly.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.Hour * 60 + t.Minute : 0;
}
