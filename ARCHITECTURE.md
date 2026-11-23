# NexaDock architecture

## Projects

```
NexaDock.slnx
├─ src/NexaDock.Core            net10.0                    Models, interfaces, widget catalog, pure logic
├─ src/NexaDock.Infrastructure  net10.0                    SQLite settings store, file logger, data paths
├─ src/NexaDock.Windows         net10.0-windows10.0.19041  Win32/DWM/shell/WinRT integration, tray icon
├─ src/NexaDock.UI              net10.0-windows10.0.19041  WPF views, widgets, view models (CommunityToolkit.Mvvm)
├─ src/NexaDock.App             net10.0-windows10.0.19041  Composition root and lifetime (Microsoft.Extensions.Hosting)
└─ tests/NexaDock.Tests         net10.0                    xUnit tests for Core and Infrastructure
```

Dependencies point inward: `App → UI, Windows, Infrastructure → Core`. UI and Windows never
reference each other; they meet only through Core interfaces, which App wires up. The Windows 10
SDK target framework gives access to WinRT APIs such as media sessions. No Windows App SDK or UI
framework is used.

## Core interfaces (Core/Abstractions)

| Interface | Implementation | Purpose |
| --- | --- | --- |
| `ISettingsStore` / `ISettingsService` | `SqliteSettingsStore` / `SettingsService` | Persisted settings, change events, debounced saving |
| `IMonitorProvider`, `IDockPositioner` | `Win32MonitorProvider`, `DockPositionCalculator` | Displays with per-monitor DPI; pure placement math |
| `IBackdropService` | `DwmBackdropService` | DWM acrylic (build 22621+) or a fallback |
| `IDockWindowHost` | `Win32DockWindowHost` | Non-activating windows, app-bar reservation, display/DPI change events, outside-click hook |
| `IWindowTracker` | `Win32WindowTracker` | Windows eligible for the taskbar (WinEvent hooks), activate/minimize/close, full-screen detection |
| `IShellCommands` | `ShellCommands` | Start, Quick Settings, notifications, Show Desktop, Task Manager |
| `IIconProvider`, `IAppLauncher`, `IShortcutCatalog` | Shell implementations | Real icons, launching, default pins, pin ↔ window matching |
| `IThumbnailService` | `DwmThumbnailService` | Live window previews |
| `ISystemMonitor` | `SystemMonitor` | CPU, memory, top processes, battery, network |
| `IWeatherService` | `OpenMeteoWeatherService` | Geocoding and forecast; 15-minute cache |
| `IMediaService` | `WinRtMediaService` | `GlobalSystemMediaTransportControlsSessionManager` |
| `ITaskbarController`, `IStartupRegistration` | `TaskbarController`, `RegistryStartupRegistration` | Taskbar hide/restore; HKCU Run key |
| `IAppShell` | `AppShell` (App) | Bar, Settings, widget picker, notifications, exit |

## Widgets

- `Core/Widgets/WidgetCatalog` describes each widget type: ID, name, category, glyph, and whether
  it may appear more than once.
- `DockSettings.Widgets` is the ordered list of instances (`WidgetPlacement(Id, TypeId)`).
  `SettingsNormalizer` drops unknown types, removes duplicate single-instance widgets, fixes IDs
  and caps the list.
- In the UI, each type is a `WidgetViewModel` subclass, created by `WidgetFactory` with its
  services resolved from DI. Its lifecycle methods are:
  - `Start` and `ApplySettings`
  - `SetDpiScale`
  - `OnClick`, `OnCardOpened` and `OnCardClosed`
  - `GetMenuItems`
- Looks live in `Views/WidgetTemplates.xaml`. `WidgetTemplateSelector` picks `"{Type}.Tile"` for the
  bar and `"{Type}.Card"` for the pop-up.
- `DockViewModel` reconciles the live widget instances with the settings (reusing instances,
  disposing removed ones), so the picker, drag-reorder and Settings all go through one path.

To add a widget:

1. Add its descriptor to the catalog.
2. Write its view model.
3. Map the type in `WidgetFactory`.
4. Add its `.Tile` template, and a `.Card` template if it has a card.

## Rendering the glass

The bar, the cards and the preview pop-up share one look:

- `GlassBrushes` builds four layers from a `ThemePalette`: the tint gradient, a glow rising from
  the bottom, a specular sheen and a light rim. It also builds the tile plates and text brushes.
- `ThemeResources` publishes these as `Bar.*` dynamic resources, so every template updates
  instantly when the theme changes.

On build 22621 and newer:

- The window is extended into its frame and given `DWMSBT_TRANSIENTWINDOW` (acrylic) with rounded
  corners.
- `WM_NCACTIVATE` is answered as "active", so the blur stays vivid even though these windows never
  take focus (`WS_EX_NOACTIVATE`).

The fallback is a per-pixel-transparent window with a stronger tint and a painted shadow.

## Interaction model (DockWindow)

- **Tiles** sit in `BarPanel`, a horizontal panel where flexible spaces share the leftover width.
- **Click a tile:** opens its card in `CardWindow`, positioned against the tile and the bar edge.
  The card closes on a click anywhere else, detected by a `WH_MOUSE_LL` hook that is installed only
  while a card is open.
- **Drag a tile horizontally:** reorders the widgets; neighbouring tiles slide to preview the drop.
- **Drag the bar by empty space:** the bar follows the cursor. On release, the display under the
  cursor and the top or bottom half of that display decide the new placement.
- **Hover a running app for 480 ms:** `PreviewWindow` registers DWM thumbnails for the app's
  windows, up to five.
- **Full-screen hiding:** the window tracker's foreground events hide the bar while a non-maximized
  window covers the whole display.

## Taskbar replacement

`TaskbarCoordinator` applies the setting:

1. It saves the taskbar's app-bar state into `DockSettings.TaskbarStateBeforeReplace` and flushes
   it to disk.
2. Only then does it set the taskbar to auto-hide and hide its windows, re-asserting this every 3
   seconds in case Explorer restarts.
3. The bar registers as an app bar for the strip it occupies, and is placed against the monitor
   bounds instead of the work area.

The taskbar is restored in each of these cases:

- the user turns the option off
- the user quits NexaDock
- an unhandled exception occurs
- the next start after a crash (`TaskbarStateBeforeReplace` is still set)

## Lifetime

- **Startup:** `App.OnStartup` takes the single-instance mutex. A second launch signals
  `ShowDock`, or `ShowSettings` when started with `--settings`, then exits.
- **First run:** the default pins (apps that exist on the machine) and the default widget layout
  are seeded, and the 12/24-hour clock is chosen from the culture.
- **Exit:**
  1. Dispose the tray icon.
  2. Restore the taskbar.
  3. Close the bar, which removes the app bar.
  4. Flush the settings.
  5. Dispose the host.

## Phase 3 additions

### New services

| Interface | Implementation | Purpose |
| --- | --- | --- |
| `IKeyValueStore` | `SqliteSettingsStore` | Local widget content (to-dos, notes) under `data.*` keys in the same database |
| `IAudioService` | `CoreAudioService` | Default speaker volume and mute, microphone mute (Core Audio COM) |
| `IStorageNetworkMonitor` | `StorageNetworkMonitor` | Network byte counters, fixed drives, Recycle Bin (`SHQueryRecycleBin`, `SHEmptyRecycleBin` with the Windows confirmation) |
| `IKeyboardLayoutService` | `KeyboardLayoutService` | Layout of the window in front; switching via `WM_INPUTLANGCHANGEREQUEST` |
| `IRadioService` | `WinRtRadioService` | Wi-Fi and Bluetooth through `Windows.Devices.Radios` |
| `IBrightnessService` | `BrightnessService` | WMI (laptop panels) and DDC/CI through dxva2 (external monitors), always off the UI thread |

`ISystemMonitor.Sample()` returns a cached snapshot for 1.5 s. Both Pulse and the CPU widget
sample, and a CPU delta measured over a few milliseconds would be meaningless.

### Localization

- **Strings:** `Core/Localization/Strings` holds the English and Croatian dictionaries. Lookups
  fall back to English, then to the key.
- **Runtime:** `UI/Localization/Loc` resolves the setting (English, Croatian, or follow Windows),
  sets the thread cultures, and raises an indexer change. Every `{l:Loc key}` binding is a live
  binding to `Loc.Instance[key]`, so the whole UI switches without a restart.
- **Cached text:** view models that cache text override `WidgetViewModel.OnLanguageChanged`.
- **Tray:** the tray lives in the Windows layer, so the App passes it the translated texts.
- **Tests:**
  - both languages have the same keys and the same `{n}` placeholders
  - every widget, category and weather kind has text
  - a source scan checks that every key used in XAML or C# exists

### Profiles

- **Storage:** `DockSettings.Profiles` holds named layouts with a `ProfileTrigger` (schedule and
  trigger apps). `DockSettings.Widgets` stays the single source for the bar.
  `SettingsNormalizer` mirrors it into the active profile, so editing the bar edits the active
  profile.
- **`ProfileEngine.Resolve`:** a trigger app in front wins, then a schedule (overnight windows count
  toward the previous day), then `DefaultProfileId`.
- **`ProfileEngine.Activate`:** swaps the layout.
- **`ProfileCoordinator` (App):** reacts only to changes in the resolved profile, and only after
  they are stable for 3 s. A manual pick therefore lasts until the next trigger edge.

### Pulse

- **Rules:** `Core/Pulse/PulseRules` turns readings into `PulseAlert`s (unit-tested thresholds).
- **`PulseCenter` (UI):**
  - Checks CPU every 2 s, and battery and disk every 30 s. Widgets raise their own alerts (a
    finished Focus round or timer).
  - Dismissed alerts stay suppressed until their condition clears.
  - Opening a widget's card acknowledges its informational alerts.
- **Display:**
  - `DockViewModel` sets `IsPulsing` on the owning widgets, and their tile shows an animated accent
    ring (static with Reduced motion).
  - The Pulse widget collapses itself (`IsVisibleOnBar`) when there is nothing to report.

### Cards with keyboard input

Cards are non-activating like the bar. When a widget sets `WantsKeyboard` and the user clicks a text
field:

1. `IDockWindowHost.SetKeyboardInput` removes `WS_EX_NOACTIVATE` and stops answering
   `WM_MOUSEACTIVATE` with no-activate.
2. The card is activated and the text box focused.
3. Closing the card restores the non-activating mode. Esc closes it.

### Overflow

When the widgets don't fit, `BarPanel` hides tiles that would be cut off at the right edge rather
than clipping them.

## Phase 3.5 additions

### Claude usage

- **Reading (`Infrastructure/Usage/ClaudeLogReader`):**
  - Reads Claude Code's session logs under `CLAUDE_CONFIG_DIR`, `~/.claude/projects` and
    `~/.config/claude/projects`.
  - Reads incrementally: only bytes appended since the last pass, and only complete lines.
  - Keeps only usage numbers, model and timestamp from lines that carry `message.usage`. Message
    text is never stored.
  - A response logged in several lines is deduplicated by message id and request id, keeping the
    last line.
  - Entries older than 35 days are dropped.
- **Calculating (`Core/Usage/ClaudeUsageCalculator`):**
  - Builds 5-hour windows: each starts at the full hour of its first message and lasts five hours.
    A message after the window, or after a 5-hour gap, starts a new one.
  - Also produces today, the last seven days (local time zone), models, and the busiest window of
    the last 30 days.
  - "Tokens" means input, output and cache writes; cache reads are shown separately.
- **Widget (`ClaudeWidget`):** refreshes every 30 s on a background thread.
  - The tile ring shows how far into the window you are.
  - The card compares the window with your own busiest window, because plan limits are not public.

### Overflow

`BarPanel` never asks for more width than it gets, so it can detect a shortage. It then reserves
room for the "›" button and hides whole tiles in order from the first one that doesn't fit, and
reports them in `HiddenItems`. `DockWindow` shows those same live view models in the overflow card.

### Lifetime and install

- `--exit` signals a named event that the running instance turns into a normal `Exit()`, so the
  taskbar is restored. With nothing running, it does nothing.
- `tools/install.ps1` publishes Release (framework-dependent, win-x64) to
  `%LOCALAPPDATA%\Programs\NexaDock` and creates a Start menu shortcut.
- `tools/uninstall.ps1` removes the program, the shortcut and the startup entry (only if it points
  to this install). Data is kept unless `-RemoveData` is given.

### Full-screen detection

A window counts as full screen only when two things hold:
- the foreground window covers the monitor and is not maximized
- `SHQueryUserNotificationState` reports busy, Direct3D full screen or presentation mode (the
  signal the Windows taskbar uses)

The bar is only hidden or shown after it has finished loading. A `Hide()` during the first `Show()`
used to leave an empty glass window.

## Tests

`tests/NexaDock.Tests` has 106 tests covering:

- **Placement:** full width and compact, edges, DPI, monitor bounds vs work area, padding, the
  reserved strip.
- **Monitors:** selection and fallback, monitor from a point.
- **Settings:** normalization, tolerant JSON, debounced and failure-safe saving, SQLite persistence
  and corruption recovery.
- **Widget layout:** validation, duplicates, caps.
- **Weather codes:** mapping to icons.
- **Profiles:** layout mirroring, activation, trigger priority, overnight schedules, trigger cleanup, JSON round trip.
- **Pulse rules:** battery, storage and CPU thresholds.
- **Timers:** Pomodoro cycle with long breaks, pause, countdown, stopwatch laps (with a fake TimeProvider).
- **Local data:** key/value persistence next to settings.
- **Claude usage:** 5-hour windows, summary, model names, compact numbers; log parsing, deduplication, incremental reads of partly written lines, retention.
- **Localization:** both languages complete, placeholders match, and every key used in the source exists.
