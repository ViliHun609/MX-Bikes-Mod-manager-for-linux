# Desktop Installation Checklist

Everything is per-user and does **not** require root. You can install a published
release or build from source.

## Option A — install a release (recommended)

- [ ] Open the [latest release](https://github.com/ViliHun609/MX-Bikes-Mod-manager-for-linux/releases/latest).
- [ ] Download `MXBikesModManager-*-linux-x86_64.tar.gz`.
- [ ] Extract it.
- [ ] Run the installer from the extracted folder:

  ```bash
  ./install-desktop.sh
  ```

- [ ] Confirm the app was copied to `~/.local/opt/mxbikes-mod-manager/`.
- [ ] Confirm the icon and launcher exist:
  - `~/.local/share/icons/hicolor/512x512/apps/mxbikes-mod-manager.png`
  - `~/.local/share/applications/mxbikes-mod-manager.desktop`
- [ ] Launch **MX Bikes Mod Manager** from the app launcher.
- [ ] Verify your mods are detected and the tabs show the expected counts.
- [ ] Check `~/.local/opt/mxbikes-mod-manager/data/logs/app.log` for startup errors.

You can also download `MXBikesModManager-x86_64.AppImage` and `install-desktop.sh`
separately; place them in the same folder before running the installer.

## Option B — build from source

- [ ] Build the latest AppImage:

  ```bash
  scripts/build-appimage.sh
  ```

- [ ] Install it for your user:

  ```bash
  scripts/install-desktop.sh
  ```

- [ ] Confirm the app was copied to `~/.local/opt/mxbikes-mod-manager/`.
- [ ] Confirm `appsettings.json` and `data/` were carried over.
- [ ] Confirm the icon and launcher exist:
  - `~/.local/share/icons/hicolor/512x512/apps/mxbikes-mod-manager.png`
  - `~/.local/share/applications/mxbikes-mod-manager.desktop`
- [ ] Launch **MX Bikes Mod Manager** from the app launcher.
- [ ] Verify your mods are detected and the tabs show the expected counts.
- [ ] Check `~/.local/opt/mxbikes-mod-manager/data/logs/app.log` for startup errors.

## Reinstall / update

Live updates are intentionally not configured. To update:

- [ ] Download the newer release bundle (or AppImage).
- [ ] Run the installer again:

  ```bash
  ./install-desktop.sh
  ```

  or, from a checkout:

  ```bash
  scripts/install-desktop.sh --appimage /path/to/MXBikesModManager-x86_64.AppImage
  ```

- [ ] Confirm the AppImage was replaced but `appsettings.json` and `data/` were kept.
- [ ] Relaunch the app.

## Publishing a release

- [ ] Bump `<Version>` in `src/MXBikesModManager/MXBikesModManager.csproj`.
- [ ] Commit the version bump.
- [ ] Push a matching tag, for example `v0.2.0`.
- [ ] Confirm the `Release` workflow built the AppImage + bundle and attached them to the
      GitHub release.
- [ ] Download the release assets yourself and run the installer once as a smoke test.

## Uninstall

- [ ] Remove the launcher, icon and AppImage but keep config/data:

  ```bash
  scripts/install-desktop.sh --uninstall
  ```

- [ ] Note the backup path printed by the script, for example:
  `~/.local/share/mxbikes-mod-manager-backup-YYYYMMDD-HHMMSS/`
- [ ] Remove config/data permanently too (optional, destructive):

  ```bash
  scripts/install-desktop.sh --purge
  ```

## Manual install (without the script)

- [ ] Copy the AppImage somewhere stable, e.g. `~/.local/opt/mxbikes-mod-manager/`.
- [ ] Copy `appsettings.json` and `data/` beside it.
- [ ] Copy `src/MXBikesModManager/Assets/icon.png` to
      `~/.local/share/icons/hicolor/512x512/apps/mxbikes-mod-manager.png`.
- [ ] Create `~/.local/share/applications/mxbikes-mod-manager.desktop` with:

  ```ini
  [Desktop Entry]
  Type=Application
  Name=MX Bikes Mod Manager
  Comment=Manage MX Bikes mods from the Proton prefix
  Exec="/home/you/.local/opt/mxbikes-mod-manager/MXBikesModManager-x86_64.AppImage"
  Icon=mxbikes-mod-manager
  Terminal=false
  Categories=Game;
  StartupWMClass=MXBikesModManager
  ```

- [ ] Run:
  `update-desktop-database ~/.local/share/applications`
- [ ] Refresh icons if needed:
  `gtk-update-icon-cache -f -t ~/.local/share/icons/hicolor`

## Troubleshooting

- [ ] Launcher not visible: run `update-desktop-database ~/.local/share/applications`.
- [ ] Icon missing: refresh `~/.local/share/icons/hicolor` and log out/in if needed.
- [ ] AppImage won’t start: `chmod +x` the file and check that FUSE/AppImage support is available.
- [ ] Wrong mods folder: open Settings or launch with `--mods "/path/to/MX Bikes/mods"`.
- [ ] Wayland rendering issues: try `MXB_FORCE_X11=1` when launching.
- [ ] Configuration confusion: the app uses the folder next to the AppImage; check `~/.local/opt/mxbikes-mod-manager/appsettings.json`.
