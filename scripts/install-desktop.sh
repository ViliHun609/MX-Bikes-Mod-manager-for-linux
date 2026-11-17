#!/usr/bin/env bash
# Installs MX Bikes Mod Manager as a per-user desktop application.
# No root is required; everything lands in ~/.local.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR"
# Repo layout: scripts/install-desktop.sh -> repo root. Release bundle: script at root.
if [[ ! -f "$ROOT/appsettings.json" && -f "$SCRIPT_DIR/../appsettings.json" ]]; then
  ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
fi

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
CLEANUP_DIR=""

cleanup() {
  if [[ -n "$CLEANUP_DIR" ]]; then
    rm -rf "$CLEANUP_DIR"
  fi
}
trap cleanup EXIT

usage() {
  cat <<EOF
Install MX Bikes Mod Manager for the current user.

Usage:
  scripts/install-desktop.sh [options]

Options:
  --appimage <path>    AppImage to install (default: next to this script, then dist/)
  --install-dir <dir>  Target folder (default: $INSTALL_DIR)
  --uninstall          Remove the launcher/icon/AppImage, keep config and data
  --purge              Like --uninstall, but delete config and data too
  -h, --help           Show this help

The installer can be run from the repo or from a downloaded release bundle.
It copies appsettings.json and data/ when they are available on a fresh install.
Re-running it only replaces the AppImage, preserving config/data.
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
  for candidate in "$SCRIPT_DIR/$APPIMAGE_NAME" "$PWD/$APPIMAGE_NAME" "$ROOT/dist/$APPIMAGE_NAME"; do
    if [[ -f "$candidate" ]]; then
      SOURCE_APPIMAGE="$candidate"
      break
    fi
  done
fi

if [[ -z "$SOURCE_APPIMAGE" || ! -f "$SOURCE_APPIMAGE" ]]; then
  echo "error: AppImage not found." >&2
  echo "Put $APPIMAGE_NAME next to this script, or pass --appimage <path>." >&2
  echo "From the repo you can build it first with scripts/build-appimage.sh." >&2
  exit 1
fi

if command -v realpath >/dev/null 2>&1; then
  SOURCE_APPIMAGE="$(realpath "$SOURCE_APPIMAGE")"
fi

mkdir -p "$INSTALL_DIR" "$APPLICATIONS_DIR" "$ICON_DIR"

install -m 0755 "$SOURCE_APPIMAGE" "$INSTALL_DIR/$APPIMAGE_NAME"

CONFIG_SOURCE=""
for candidate in "$SCRIPT_DIR/appsettings.json" "$ROOT/appsettings.json"; do
  if [[ -f "$candidate" ]]; then
    CONFIG_SOURCE="$candidate"
    break
  fi
done

# Preserve an existing profile on reinstall/update.
if [[ ! -f "$INSTALL_DIR/appsettings.json" && -n "$CONFIG_SOURCE" ]]; then
  install -m 0644 "$CONFIG_SOURCE" "$INSTALL_DIR/appsettings.json"
fi

if [[ ! -e "$INSTALL_DIR/data" ]]; then
  if [[ -d "$ROOT/data" ]]; then
    cp -a "$ROOT/data" "$INSTALL_DIR/data"
    rm -rf "$INSTALL_DIR/data/tools"
  else
    mkdir -p "$INSTALL_DIR/data"
  fi
fi

ICON_SOURCE=""
for candidate in "$SCRIPT_DIR/icon.png" "$ROOT/src/MXBikesModManager/Assets/icon.png"; do
  if [[ -f "$candidate" ]]; then
    ICON_SOURCE="$candidate"
    break
  fi
done

# Release bundles include icon.png; if it is missing, pull the icon out of the AppImage.
if [[ -z "$ICON_SOURCE" ]]; then
  CLEANUP_DIR="$(mktemp -d)"
  if (cd "$CLEANUP_DIR" && "$SOURCE_APPIMAGE" --appimage-extract 'usr/share/icons/hicolor/512x512/apps/*.png' >/dev/null 2>&1); then
    ICON_SOURCE="$(find "$CLEANUP_DIR/squashfs-root" -type f -name '*.png' | head -1)"
  fi
fi

if [[ -n "$ICON_SOURCE" && -f "$ICON_SOURCE" ]]; then
  install -m 0644 "$ICON_SOURCE" "$ICON_DIR/$ICON_NAME.png"
else
  echo "warning: icon not found; the launcher will use a generic icon." >&2
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
if [[ -f "$INSTALL_DIR/appsettings.json" ]]; then
  echo "  Config:   $INSTALL_DIR/appsettings.json"
else
  echo "  Config:   created on first launch"
fi
echo "  Data:     $INSTALL_DIR/data"
echo "  Launcher: $DESKTOP_FILE"
echo
echo "It should now appear in your app launcher."
echo "Run 'scripts/install-desktop.sh --uninstall' to remove it (config/data are kept)."
