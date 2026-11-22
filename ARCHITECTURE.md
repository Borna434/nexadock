# NexaDock architecture

## Projects

```
NexaDock.slnx
├─ src/NexaDock.Core            net10.0          Models, interfaces, pure logic (no UI, no Win32)
├─ src/NexaDock.Infrastructure  net10.0          SQLite settings store, file logger, data paths
├─ src/NexaDock.Windows         net10.0-windows  Win32/DWM/shell interop, tray icon
├─ src/NexaDock.UI              net10.0-windows  WPF views, view models (CommunityToolkit.Mvvm)
├─ src/NexaDock.App             net10.0-windows  Composition root, lifetime (Microsoft.Extensions.Hosting)
└─ tests/NexaDock.Tests         net10.0          xUnit tests for Core + Infrastructure
```

Dependencies point inward: `App → UI, Windows, Infrastructure → Core`. UI and Windows do not
reference each other. They meet only through Core interfaces, which App wires up.

All projects target x64 (`Directory.Build.props`). Product naming is centralized in
`NexaDock.Core.ProductInfo`.

## Key interfaces (Core/Abstractions)

| Interface | Implementation | Purpose |
| --- | --- | --- |
| `ISettingsStore` | `SqliteSettingsStore` | Load/save `DockSettings`; quarantines corrupt databases |
| `ISettingsService` | `SettingsService` | Live settings, `Changed` event, 400 ms debounced save, `Flush()` |
| `IMonitorProvider` | `Win32MonitorProvider` | `EnumDisplayMonitors` + `GetDpiForMonitor` (per-monitor effective DPI) |
| `IDockPositioner` | `DockPositionCalculator` | Pure math: monitor work area + settings → window rectangle in physical pixels |
| `IBackdropService` | `DwmBackdropService` | Capability detection (build ≥ 22621) and DWM acrylic |
| `IDockWindowHost` | `Win32DockWindowHost` | Non-activating tool window, `SetWindowPos`, display/DPI/work-area change events |
| `IIconProvider` | `ShellIconProvider` | Real shell icons through `IShellItemImageFactory` (files, `.lnk`, `shell:AppsFolder\AUMID`) |
| `IAppLauncher` | `ShellAppLauncher` | `ShellExecute` + `AllowSetForegroundWindow` |
| `IShortcutCatalog` | `ShortcutCatalog` | First-run defaults (only apps that exist) and file → shortcut |
| `IAppShell` | `AppShell` (App) | Show/hide dock, open Settings, notify, exit |

## Rendering the glass

`DockWindow` decides its surface **before** the window handle exists, because a layered window
(`AllowsTransparency`) cannot host a DWM system backdrop.

**Native acrylic** (default on 22621+):
1. Set the WPF composition background to transparent and call `DwmExtendFrameIntoClientArea(-1)`.
2. Set `DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW` (acrylic),
   `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`, `DWMWA_BORDER_COLOR = NONE`, and
   `DWMWA_USE_IMMERSIVE_DARK_MODE` according to the theme.
3. The window is `WS_EX_NOACTIVATE`, so it is always inactive, and DWM would draw an inactive
   acrylic window as flat. `WM_NCACTIVATE` is answered with `DefWindowProc(…, TRUE, …)` so the frame
   (and backdrop) stays in its active state.

**Translucent fallback** (older builds, `NEXADOCK_BACKDROP=fallback`, or DWM refused): a
per-pixel-transparent window, a stronger tint, a pill-shaped corner radius and a WPF drop shadow in
a 16 DIP transparent margin.

On top of either surface, `GlassBrushes` (UI/Theming) paints four layers from a `ThemePalette`:
the tint gradient (alpha = Opacity setting), an inner glow rising from the bottom edge, a specular
sheen on the upper half and a light-catching 1 px rim.

## Placement and DPI

The process is Per-Monitor-V2 DPI aware (`app.manifest`). `DockPositionCalculator` works in
physical pixels: DIP sizes × monitor scale, centered in the **work area** (so it never overlaps the
taskbar), offset from the chosen edge, and widened to fit its content. The width is then clamped to
the monitor.

`DockWindow.Reposition()` runs on startup, on every settings change, on `DpiChanged`, and when
`Win32DockWindowHost` reports `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE(SPI_SETWORKAREA)`
or `SystemEvents.DisplaySettingsChanged` (debounced 250 ms).

`MonitorSelector` falls back to the primary display when the preferred monitor is missing, but the
stored preference is kept, so the dock returns once that display is reconnected.

## Settings flow

```
SettingsWindow ─binds→ SettingsViewModel ─Update(s => s with {…})→ SettingsService
                                                                     │  Normalize (clamp/repair)
                                                                     ├─ Changed → DockViewModel (brushes, metrics, shortcuts)
                                                                     │          → DockWindow.Reposition
                                                                     └─ debounce 400 ms → SqliteSettingsStore.Save
```

`DockSettings` is an immutable record. `SettingsSerializer` reads JSON one field at a time, so a
bad value only resets that field. `SettingsNormalizer` clamps ranges, rejects undefined enums,
de-duplicates shortcut IDs and caps the list at 32 entries.

## Lifetime

`App.OnStartup` takes a single-instance mutex. A second launch signals a named event and exits, and
the first instance then shows its dock. Startup then builds the generic host, loads settings, seeds
defaults on first run, shows the dock and creates the tray icon. `ShutdownMode=OnExplicitShutdown`
keeps the app running when Settings closes. Exit hides and disposes the tray icon (so no ghost icon
is left behind), flushes settings, then stops and disposes the host.

## Tests

`tests/NexaDock.Tests` (43 tests) covers:

- placement (edges, DPI scaling, clamping, negative monitor origins, padding, size presets)
- monitor fallback
- settings normalization and tolerant deserialization
- debounced saving, save-failure retry, and SQLite persistence across store instances
- recovery from a corrupt database and from invalid JSON
