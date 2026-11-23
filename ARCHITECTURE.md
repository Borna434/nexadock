# NexaDock architecture

## Projects

```
NexaDock.slnx
├─ src/NexaDock.Core            net10.0                    Models, interfaces, widget catalog, pure logic
├─ src/NexaDock.Infrastructure  net10.0                    SQLite store, logger, calendars (iCal, Google), GitHub updates
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

## Phase 4 additions

### Layout around the centred apps

- **Placement math (`Core/Layout/BarLayout`):**
  - The Apps tile (`IsCentered`) is centred on the full bar width.
  - Tiles before it are packed from the left edge, tiles after it are packed to the right edge.
  - A flexible space takes its side's free width.
  - A long side moves the apps over just enough to fit.
  - When everything still doesn't fit, tiles are hidden from the outer ends (right side first). The apps are never hidden.
  - `BarPanel` only applies the result.
- **Widget order (`Core/Widgets/WidgetArranger`):**
  - Left of the apps: glance and productivity widgets, with Now Playing nearest the apps because its width changes.
  - Right of the apps: Pulse first (it comes and goes), then status, the clock and Show Desktop.
  - Used for the default layout, the profile templates and "Arrange widgets", and to place newly added widgets.
  - Existing layouts were arranged once on upgrade.
- **Edge to edge:** with full width and no gap the bar is "flush".
  - DWM corners are set to square.
  - The rim becomes a single line on the inner side.
  - The app bar reserves exactly the edge gap plus the glass, so maximised windows end where the glass begins.

### Several bars

- `BarTarget` says which bar a `DockWindow` and `DockViewModel` are: the main bar (null display) or one secondary display.
- **`BarManager` (App):**
  - Creates one extra bar per non-main display while `ShowOnAllDisplays` is on.
  - Adds and removes bars when displays change.
  - Hides and shows them with the main bar.
- **Layouts:** extra bars read and write their own layout through `Core/Layout/BarLayouts` (`DisplayLayouts` in settings). The default is the apps and the clock.
- **Apps on extra bars:** the Apps widget lists only windows on its display (`IWindowTracker.GetMonitorDeviceName`). It re-checks every 1.5 s because windows can move without an event.
- **App bars:** `Win32DockWindowHost` keeps one app bar registration per bar window.

### Notification icons

- **Hosting:** `TrayIconHost` hosts the notification area with ManagedShell (`TrayService`, `NotificationArea`, `ExplorerTrayService`).
  - Its own `Shell_TrayWnd` stays first in z-order so `Shell_NotifyIcon` reaches it.
  - Windows keeps its own copy of every icon, so nothing is lost if NexaDock stops.
- **Input:** `TrayIconButton` forwards hover, clicks and right-clicks to the owning app. It sets the icon's screen rectangle and allows the app to take the foreground.
- **App bar messages.** `SHAppBarMessage` talks to the first `Shell_TrayWnd`, which is ours while icons are hosted, and Windows ignores app bar messages that loop back into the sending process. So that the reservation still reaches Windows:
  - For each app bar call, our tray window steps below Explorer's and is raised again afterwards.
  - Positions are sent only when they change.
  - Icon hosting starts 1.5 s after the bars settle, so icons that apps re-add at startup are not missed.

### Calendars

- **Model:** `Core/Calendar` holds `CalendarEvent` (with calendar name and colour), the agenda logic, `ICalendarSource` (iCal links) and `IGoogleCalendarService`.
- **`IcsCalendarSource`:** Ical.Net expands recurring events and time zones. The calendar name comes from `X-WR-CALNAME`, and each link gets a colour.
- **`GoogleCalendarClient`:**
  - Signs in with OAuth 2.0 for installed apps, using PKCE and a loopback redirect in the user's browser, with the `calendar.readonly` scope.
  - The OAuth client is the user's own, created once in Google Cloud.
  - The refresh token and client secret are stored with DPAPI for the current Windows user. Sign-out revokes the token.
  - It reads the calendar list (names, colours, Google's own visibility) and events with `singleEvents=true`.
  - An expired or revoked sign-in raises `GoogleSignInExpiredException`. The feed remembers that and raises a Pulse until the user signs in again.
- **`CalendarFeed` (UI):** combines both sources and refreshes every 10 minutes. It drives the agenda in the Clock and Day Number cards, the Up Next widget, the month dots, the meeting Pulse and Settings › Calendars.

### Liquid glass

- **Static layers:** these are plain WPF brushes over the DWM blur, so nothing renders while the bar is idle:
  - a specular hairline
  - lens depth (a darker lower inner edge)
  - diagonal edge light on the floating bar and on tiles
- **Pointer light:** a radial light that follows the pointer while it is over the bar.
- **Refraction (opt-in, `LiquidRefraction`):**
  - Ten times a second `ScreenStripCapture` BitBlts the 16-pixel strip just beyond the inner edge. It never captures the bar itself, so the bar stays visible in screenshots.
  - The strip is shown mirrored and squeezed into the rim.
  - The bitmap is updated only when the strip changed, and the timer runs only while the bar is visible.
  - Measured: about 0.16 % CPU and 0.01 % GPU.

### Backup, restart and updates

- **Backup:** `Core/Settings/SettingsBackup` writes a JSON file with the settings and widget data.
  - Google sign-ins are left out.
  - On restore, machine-specific values (startup, saved taskbar state) are kept from this PC.
  - NexaDock restarts after a restore (`IAppShell.Restart`, which starts `NexaDock.exe --wait-for <pid>`).
- **Updates:**
  - `GitHubUpdateSource` reads the latest release of `ProductInfo.UpdateRepository` and offers it only with `NexaDock-win-x64.zip` and its SHA-256 (GitHub's asset digest or a `.sha256` file).
  - `UpdateService` checks 2 minutes after start and then every 12 hours, and tells the user through Pulse.
  - To install, it downloads the package, checks the hash and unpacks it. A small PowerShell script waits for NexaDock to exit, copies the files over `%LOCALAPPDATA%\Programs\NexaDock` and starts it again. Data in `%LOCALAPPDATA%\NexaDock` is untouched.
  - `tools/package.ps1` builds the package and the hash file.

## Tests

`tests/NexaDock.Tests` has 165 tests covering:

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
- **Bar layout:** centred apps for any width, both edges, a long side, overflow from the outer ends.
- **Widget arrangement:** the old layout, idempotence, insert positions, the default layout.
- **Displays:** default and own layouts per display, saving, old settings.
- **Calendars:** iCal recurrence, time zones, exclusions, all-day and cancelled events, feed names; Google event mapping; agenda, next event and day dots.
- **Backup:** round trip without sign-ins, machine values kept, invalid files rejected.
- **Updates:** release tags, pre-releases, version comparison.
