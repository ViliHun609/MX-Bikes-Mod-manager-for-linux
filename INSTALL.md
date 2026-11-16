# Desktop Installation Checklist

Use this after building `dist/MXBikesModManager-x86_64.AppImage`. Everything is
per-user and does **not** require root.

## Install

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

## Reinstall / update the AppImage

- [ ] Build or download the new AppImage.
- [ ] Run:

  ```bash
  scripts/install-desktop.sh --appimage /path/to/MXBikesModManager-x86_64.AppImage
  ```

- [ ] Confirm the AppImage was replaced but `appsettings.json` and `data/` were kept.
- [ ] Relaunch the app.

## GitHub auto-updates (once the repo exists)

- [ ] Create the GitHub repository and push `main`.
- [ ] Add the repository URL to the release/update configuration.
- [ ] Add a GitHub Actions workflow that runs tests and builds the AppImage on a tag.
- [ ] Upload the AppImage and `.zsync` to a GitHub Release.
- [ ] Rebuild the AppImage with `gh-releases-zsync` update information.
- [ ] Test `appimageupdatetool` (or AppImageUpdate) against the installed AppImage.
- [ ] Confirm an update preserves config and `data/`.

Until this is done there is no automatic GitHub update; use the reinstall step above.

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
