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

## Tests

`tests/NexaDock.Tests` has 62 tests covering:

- **Placement:** full width and compact, edges, DPI, monitor bounds vs work area, padding, the
  reserved strip.
- **Monitors:** selection and fallback, monitor from a point.
- **Settings:** normalization, tolerant JSON, debounced and failure-safe saving, SQLite persistence
  and corruption recovery.
- **Widget layout:** validation, duplicates, caps.
- **Weather codes:** mapping to icons.
