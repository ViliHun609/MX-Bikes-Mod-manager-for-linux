# TODO / Work Log

Status of the current batch of work. Update the checkboxes as things change.

## Themes

- [x] `ThemeService` with five dark palettes (Ember, Midnight, Forest, Violet, Mono)
- [x] Persist the choice as `ui.theme` in `appsettings.json`
- [x] Apply the saved theme at startup, before windows are created
- [x] `--theme <name>` CLI override for screenshots/testing
- [x] Settings → Theme cards with live preview
- [x] Cancel / window close restores the previous theme; Save persists
- [x] Move all hardcoded view colours to theme resources
- [x] Unit tests for palettes, fallback, config round-trip
- [x] Verify the theme engine + cards visually (Midnight/Violet); no further GUI runs per request
- [ ] Commit the theme work

## Icons

- [x] Add Bootstrap Icons (MIT) as vector geometries in `AppIcons`
- [x] Replace emoji in mod type config with icon keys (`bike`, `track`, `tyre`, `rider`, `misc`)
- [x] Backwards-compatible aliases for old emoji config values
- [x] Icons in the main tabs and empty states
- [x] Icons in the "Select mod type" install dialog
- [x] Icons on header and status-bar buttons
- [x] Icons on the theme cards
- [x] Unit tests for icon resolution/fallback
- [x] Verify icons visually in the tabs and theme cards
- [ ] Commit the icon work

## Release

- [x] Run the full test suite (76 passing)
- [ ] Rebuild `dist/MXBikesModManager-x86_64.AppImage`
- [ ] Record the new SHA-256
- [x] Update README (themes + icons + test count)

## Known leftovers / notes

- The live drag-and-drop test from the previous batch is still only unit-tested.
- The final look of all five themes should be eyeballed once by the user; the engine was
  verified with Midnight/Violet screenshots before GUI testing was stopped.
- `dist/` is git-ignored; the AppImage is produced by `./scripts/build-appimage.sh`.
