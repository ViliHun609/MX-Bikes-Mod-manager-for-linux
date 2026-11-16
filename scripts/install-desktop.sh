#!/usr/bin/env bash
# Installs MX Bikes Mod Manager as a per-user desktop application.
# No root is required; everything lands in ~/.local.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_NAME="MXBikesModManager"
APPIMAGE_NAME="$APP_NAME-x86_64.AppImage"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$HOME/.local/opt/mxbikes-mod-manager"
APPLICATIONS_DIR="$DATA_HOME/applications"
ICON_DIR="$DATA_HOME/icons/hicolor/512x512/apps"
ICON_NAME="mxbikes-mod-manager"
DESKTOP_FILE="$APPLICATIONS_DIR/$ICON_NAME.desktop"

UNINSTALL=0
PURGE=0
SOURCE_APPIMAGE=""

usage() {
  cat <<EOF
Install MX Bikes Mod Manager for the current user.

Usage:
  scripts/install-desktop.sh [options]

Options:
  --appimage <path>    AppImage to install (default: dist/$APPIMAGE_NAME)
  --install-dir <dir>  Target folder (default: $INSTALL_DIR)
  --uninstall          Remove the launcher/icon/AppImage, keep config and data
  --purge              Like --uninstall, but delete config and data too
  -h, --help           Show this help

The installer copies appsettings.json and data/ into the target folder on a
fresh install. Re-running it only replaces the AppImage, preserving config/data.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --appimage)
      SOURCE_APPIMAGE="${2:-}"
      [[ -n "$SOURCE_APPIMAGE" ]] || { echo "error: --appimage needs a path" >&2; exit 2; }
      shift 2
      ;;
    --install-dir)
      INSTALL_DIR="${2:-}"
      [[ -n "$INSTALL_DIR" ]] || { echo "error: --install-dir needs a path" >&2; exit 2; }
      shift 2
      ;;
    --uninstall)
      UNINSTALL=1
      shift
      ;;
    --purge)
      UNINSTALL=1
      PURGE=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

refresh_caches() {
  if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$APPLICATIONS_DIR" >/dev/null 2>&1 || true
  fi
  if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
  fi
}

remove_launcher() {
  rm -f "$DESKTOP_FILE" "$ICON_DIR/$ICON_NAME.png"
  refresh_caches
}

if (( UNINSTALL )); then
  remove_launcher

  if [[ -d "$INSTALL_DIR" ]]; then
    if (( PURGE )); then
      rm -rf "$INSTALL_DIR"
      echo "Removed $INSTALL_DIR"
    else
      backup_dir="$DATA_HOME/mxbikes-mod-manager-backup-$(date +%Y%m%d-%H%M%S)"
      mkdir -p "$backup_dir"
      for item in appsettings.json data; do
        if [[ -e "$INSTALL_DIR/$item" ]]; then
          mv "$INSTALL_DIR/$item" "$backup_dir/"
        fi
      done
      rm -f "$INSTALL_DIR/$APPIMAGE_NAME"
      rmdir "$INSTALL_DIR" 2>/dev/null || true
      echo "Removed the launcher and AppImage."
      echo "Your config and data were moved to: $backup_dir"
    fi
  else
    echo "Launcher removed; no installed app folder was found."
  fi

  echo "Done."
  exit 0
fi

if [[ -z "$SOURCE_APPIMAGE" ]]; then
  SOURCE_APPIMAGE="$ROOT/dist/$APPIMAGE_NAME"
fi

if [[ ! -f "$SOURCE_APPIMAGE" ]]; then
  echo "error: AppImage not found: $SOURCE_APPIMAGE" >&2
  echo "Build it first with scripts/build-appimage.sh, or pass --appimage <path>." >&2
  exit 1
fi

if command -v realpath >/dev/null 2>&1; then
  SOURCE_APPIMAGE="$(realpath "$SOURCE_APPIMAGE")"
fi

mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR" "$ICON_DIR"

install -m 0755 "$SOURCE_APPIMAGE" "$INSTALL_DIR/$APPIMAGE_NAME"

# Preserve an existing profile on reinstall/update.
if [[ ! -f "$INSTALL_DIR/appsettings.json" && -f "$ROOT/appsettings.json" ]]; then
  install -m 0644 "$ROOT/appsettings.json" "$INSTALL_DIR/appsettings.json"
fi

if [[ ! -e "$INSTALL_DIR/data" ]]; then
  if [[ -d "$ROOT/data" ]]; then
    cp -a "$ROOT/data" "$INSTALL_DIR/data"
    rm -rf "$INSTALL_DIR/data/tools"
  else
    mkdir -p "$INSTALL_DIR/data"
  fi
fi

ICON_SOURCE="$ROOT/src/MXBikesModManager/Assets/icon.png"
if [[ -f "$ICON_SOURCE" ]]; then
  install -m 0644 "$ICON_SOURCE" "$ICON_DIR/$ICON_NAME.png"
fi

cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=MX Bikes Mod Manager
Comment=Manage MX Bikes mods from the Proton prefix
Exec="$INSTALL_DIR/$APPIMAGE_NAME"
TryExec=$INSTALL_DIR/$APPIMAGE_NAME
Icon=$ICON_NAME
Terminal=false
Categories=Game;
StartupWMClass=MXBikesModManager
EOF
chmod 0644 "$DESKTOP_FILE"

if command -v desktop-file-validate >/dev/null 2>&1; then
  desktop-file-validate "$DESKTOP_FILE" || true
fi

refresh_caches

echo "Installed MX Bikes Mod Manager"
echo "  AppImage: $INSTALL_DIR/$APPIMAGE_NAME"
echo "  Config:   $INSTALL_DIR/appsettings.json"
echo "  Data:     $INSTALL_DIR/data"
echo "  Launcher: $DESKTOP_FILE"
echo
echo "It should now appear in your app launcher."
echo "Run 'scripts/install-desktop.sh --uninstall' to remove it (config/data are kept)."
