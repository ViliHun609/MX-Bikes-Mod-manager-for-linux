# TODO / Work Log

Status of the current batch of work. Update the checkboxes as things change.

## Current: motocross icons + drag & drop

- [x] Replace the Bikes icon with the UIX solid motocross bike (MIT)
- [x] Replace the Tracks icon with the UIX solid motocross track (MIT)
- [x] Keep icon keys (`bike`, `track`) and all old emoji aliases working
- [x] Add a `DragEnter` handler alongside DragOver / DragLeave / Drop
- [x] Detect drops with `TryGetFiles()` instead of only `Contains(DataFormat.File)`
- [x] Call `DragDrop.SetAllowDrop` in code as well as XAML
- [x] Log drag formats, item counts and resolved paths to `data/logs/app.log`
- [x] Report non-local drops and catch install errors in the status bar
- [x] Diagnose the real cause: Avalonia 11.3's X11 backend has no XDND support
- [x] Upgrade Avalonia 11.3.23 → 12.1.6 (full X11 XDND support)
- [x] Add the native `Avalonia.Wayland` backend for Wayland sessions
      (Wayland→XWayland drops are broken on Hyprland); `MXB_FORCE_X11=1` escape hatch
- [x] Catch file/folder picker failures (portal issues now show a clear message)
- [x] Remove `Avalonia.Diagnostics` (no 12.x package; debug inspector shortcut is gone)
- [ ] User performs one real drag from Nautilus (then we read the log)
- [x] Commit this batch (`ed228b3`)
- [x] Rebuild the AppImage and record the new SHA-256:
      `378d9fa6bfce6f281237ce48e58cbf7f5419064ea0400ad1af1530f0e194ab39`

## Themes (done)

- [x] `ThemeService` with five dark palettes (Ember, Midnight, Forest, Violet, Mono)
- [x] Persist the choice as `ui.theme` in `appsettings.json`
- [x] Apply the saved theme at startup, before windows are created
- [x] `--theme <name>` CLI override for screenshots/testing
- [x] Settings → Theme cards with live preview
- [x] Cancel / window close restores the previous theme; Save persists
- [x] Move all hardcoded view colours to theme resources
- [x] Unit tests for palettes, fallback, config round-trip
- [x] Verify the theme engine + cards visually (Midnight/Violet)
- [x] Commit the theme work (`5709daa`)

## Icons (done, minus the motocross swap above)

- [x] Add Bootstrap Icons (MIT) as vector geometries in `AppIcons`
- [x] Replace emoji in mod type config with icon keys (`bike`, `track`, `tyre`, `rider`, `misc`)
- [x] Backwards-compatible aliases for old emoji config values
- [x] Icons in the main tabs and empty states
- [x] Icons in the "Select mod type" install dialog
- [x] Icons on header and status-bar buttons
- [x] Icons on the theme cards
- [x] Unit tests for icon resolution/fallback
- [x] Verify icons visually in the tabs and theme cards
- [x] Commit the icon work (`5709daa`)

## Previous release

- [x] Run the full test suite (76 passing)
- [x] Rebuild `dist/MXBikesModManager-x86_64.AppImage`
- [x] Record SHA-256: `5345b5636923826ea8dddaffb3e72bef303273d18e7cd0b649331f8476bc7848`
- [x] Update README (themes + icons + test count)
- [x] Smoke-test the AppImage with `--scan` and `--help`

## Known leftovers / notes

- Drag & drop still needs one real file-manager test; the log now records enough to
  diagnose a failure.
- The final look of all five themes should be eyeballed once by the user.
- `docs/screenshot.png` still shows the old icons; refresh it after the visual check.
- `dist/` is git-ignored; the AppImage is produced by `./scripts/build-appimage.sh`.
