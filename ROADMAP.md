# NexaDock roadmap

## Phase 1: Working Liquid Glass dock ✅

- [x] Layered solution (Core, Infrastructure, Windows, UI, App) plus a test project, .NET 10, x64
- [x] Native DWM acrylic backdrop on build 22621+, kept vivid while inactive; translucent fallback
- [x] Bottom-centered, always-on-top, non-activating dock with layered highlights and hover motion
- [x] Real shell icons, working launches (with foreground hand-off), live clock and date, Settings, Exit
- [x] Top/bottom edge, monitor selection, Per-Monitor-V2 DPI placement, fallback when a monitor is missing
- [x] Five themes, three sizes, width, spacing, opacity, edge distance, reduced motion
- [x] Shortcut management (add, remove, reorder, restore defaults)
- [x] Tray icon (Show Dock, Settings, Exit), single instance, clean shutdown
- [x] SQLite persistence with corruption recovery; Windows taskbar untouched
- [x] Unit tests for placement, settings recovery and persistence

## Phase 2: Dock behavior

- Auto-hide with reveal on edge hover, plus intelligent hide when a window overlaps the dock
- Running-app indicators and clicking to activate an existing window
- Drag-and-drop reordering on the dock; dropping files or shortcuts onto it to pin them
- Packaged app picker (Apps folder browser)
- Start with Windows (per-user Run key or startup task)
- Global hotkey to show or focus the dock
- Hands-on testing on mixed-DPI, high-DPI and hot-plug monitor setups

## Phase 3: Widget framework

- Widget host slots (left, center, right) with a small plugin contract
- Built-in widgets: media controls, calendar peek, system stats, weather
- Per-widget settings persisted alongside dock settings

## Phase 4: Advanced glass

- Refractive and "liquid" lensing via a GPU shader path (Direct3D or Windows.UI.Composition)
- Edge lighting that follows the cursor; adaptive tint from the wallpaper's average colour
- Larger native corner radius using a composition-based surface instead of DWM clipping

## Phase 5: Integrations and taskbar options

- Optional taskbar replacement modes (opt-in only, fully reversible)
- Notifications badge integration, jump lists, recent files
- Import and export of settings; installer (MSIX) and auto-update
