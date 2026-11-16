# TODO / Work Log

Status of the current batch of work. Update the checkboxes as things change.

## Bikes/tracks icon polish (done)

- [x] Trace the user-supplied dirt-bike reference into a right-facing vector silhouette
      (clean wheels/spokes, forks, fenders; no rider)
- [x] Keep the Font Awesome checkered flag for Tracks
- [x] Tab icons at 17 px
- [x] Accurate credits (Bootstrap MIT, Font Awesome CC BY, custom bike silhouette)
- [x] Build + 108 tests pass
- [ ] User visual check in the tab bar
- [x] Commit (`599f986`)
- [x] AppImage SHA-256:
      `d78fdcc555bede9187c54c52ebabf7c832fc553dd203e358296045b9ab646ca2`

## Cleanup pass (done)

- [x] Remove unused config keys (`version`, `layout`, tile sizes, `defaultSort`,
      `rememberWindow`, `scanDepth`, `disableStrategy`)
- [x] Stop serialising computed `displayTitle`, `subfoldersToScan`, `iconGeometry`
- [x] Remove dead view-model members (`CounterText`, `EnabledCount`, unused commands,
      `PathText`, `ShortName`, `WithMetadata`, `IsScanning`)
- [x] Remove unused usings and run `dotnet format` / analyzer checks
- [x] Config regression test for removed/computed keys (108 tests total)
- [x] Commit the cleanup (`226e924`)
- [x] Rebuild the AppImage and record the new SHA-256:
      `9e002f61a1a8374f83d648ca6c35fe440a1294efb313a1da30991f4375369acb`
- [x] Delete `build/`, `bin/` and `obj/` generated files from the working tree
- [x] Clear the app trash of the two broken Fasthouse installs and the old duplicate boot copy

## Current: source-to-Trash + nested packaging

- [x] Install dialog checkbox “Move the downloaded file to the system Trash” (checked by default)
- [x] Settings default checkbox + `ui.trashSourceAfterInstall`
- [x] `SystemTrashService` using `gio trash`, `trash-put` fallback, success/failure result
- [x] Trash only after a successful install; cancel/failure keeps the source
- [x] Guards: mods root, app data/config, home, filesystem roots, folders containing app data
- [x] Detect wrappers several levels deep (`mods/…`, `MX Bikes/mods/…`, custom pack folder)
- [x] Detect/retain type subfolders (`riders`, `boots`, `Mods`, …) and collapse duplicates
- [x] Retargeting to another type/subfolder strips the old one (no `boots/riders/…`)
- [x] Dropped folders with a nested game layout install per-file into the right place
- [x] Normal dropped folders still copy as one folder (regression test)
- [x] Tests for all variants + trash guards (107 total)
- [x] Rider gear packs (`rider/riders/.../paints/*.pnt`) are no longer sent to the bike-paint picker
- [x] Rider paint packs without a `rider/` type folder no longer duplicate `riders/`
- [x] README + config docs
- [ ] User re-tests “2024 GEAR FASTHOUSE.rar” (should land in `rider/riders/default_mx/paints`)
- [ ] User checks a folder drop and the Trash toggle
- [x] Commit this batch (`0fd5835`, rider paint fix `bb14332`)
- [x] Rebuild the AppImage and record the new SHA-256:
      `e7700651b6e3b3c210957cd8e9c88e03f8bf0a8a5e25446ddb2d1bbc0a1e265e`
- [x] Build script now packages to a `.new` file and moves it into place, so a running
      AppImage no longer blocks rebuilds

## Smooth scrolling (done)

- [x] `SmoothScroll` attached behaviour for `ScrollViewer` (eased wheel animation)
- [x] Clamp to the scrollable range, Shift+wheel horizontal support
- [x] Cancel immediately on mouse press / scrollbar drag
- [x] Enable native touch/touchpad inertia on the grid
- [x] `ui.smoothScrolling` setting + Settings checkbox (default on, applies live)
- [x] Unit tests for the scroll maths and config default
- [x] README + config docs
- [ ] User checks the feel (tune ease/time if needed)
- [x] Commit (`cd7f1a3`)
- [x] AppImage SHA-256: `7b0bc69642205ce64b5c452df2bf8259571c5d6898828fe5e058b0ff83824aab`
- [x] Decided against extra blur effects for now

## Drag & drop / Avalonia 12 (done)

- [x] Diagnose Avalonia 11.3's missing X11 XDND support
- [x] Upgrade Avalonia 11.3.23 → 12.1.6
- [x] Native `Avalonia.Wayland` backend for Wayland sessions; `MXB_FORCE_X11=1` escape hatch
- [x] Drag diagnostics in `data/logs/app.log`
- [x] File/folder picker error handling
- [ ] User performs one real drag from Nautilus (still outstanding)
- [x] Commit (`ed228b3`)
- [x] AppImage SHA-256: `378d9fa6bfce6f281237ce48e58cbf7f5419064ea0400ad1af1530f0e194ab39`

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

## Icons (done)

- [x] Add Bootstrap Icons (MIT) as vector geometries in `AppIcons`
- [x] Replace emoji in mod type config with icon keys (`bike`, `track`, `tyre`, `rider`, `misc`)
- [x] Swap Bikes to the UIX solid motocross bike and Tracks to the UIX motocross track
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
