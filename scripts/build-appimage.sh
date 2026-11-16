#!/usr/bin/env bash
# Builds a self-contained AppImage of MX Bikes Mod Manager.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/src/MXBikesModManager/MXBikesModManager.csproj" | head -1)"
VERSION="${VERSION:-0.1.0}"
APP_NAME="MXBikesModManager"
DIST="$ROOT/dist"
BUILD="$ROOT/build"
APPDIR="$BUILD/AppDir"
TOOLS="$ROOT/data/tools"

echo "==> Publishing $APP_NAME $VERSION (linux-x64, self-contained)"
rm -rf "$BUILD"
mkdir -p "$DIST" "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/512x512/apps" "$TOOLS"

dotnet publish "$ROOT/src/MXBikesModManager/MXBikesModManager.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$BUILD/publish"

cp -a "$BUILD/publish/." "$APPDIR/usr/bin/"

echo "==> Adding desktop entry and icon"
cat > "$APPDIR/$APP_NAME.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=MX Bikes Mod Manager
Comment=Manage MX Bikes mods from your Proton prefix
Exec=$APP_NAME
Icon=$APP_NAME
Terminal=false
Categories=Game;
StartupWMClass=MXBikesModManager
EOF
cp "$APPDIR/$APP_NAME.desktop" "$APPDIR/usr/share/applications/"
cp "$ROOT/src/MXBikesModManager/Assets/icon.png" \
   "$APPDIR/usr/share/icons/hicolor/512x512/apps/$APP_NAME.png"
cp "$ROOT/src/MXBikesModManager/Assets/icon.png" "$APPDIR/$APP_NAME.png"

cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
HERE="$(dirname "$(readlink -f "${0}")")"
exec "$HERE/usr/bin/MXBikesModManager" "$@"
EOF
chmod +x "$APPDIR/AppRun"

find_appimagetool() {
  if [[ -n "${APPIMAGETOOL:-}" && -x "${APPIMAGETOOL}" ]]; then
    echo "$APPIMAGETOOL"
    return
  fi
  if command -v appimagetool >/dev/null 2>&1; then
    command -v appimagetool
    return
  fi
  local cached="$TOOLS/appimagetool.AppImage"
  if [[ ! -x "$cached" ]]; then
    echo "==> Downloading appimagetool" >&2
    curl -fsSL -o "$cached" \
      "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$cached"
  fi
  echo "$cached"
}

TOOL="$(find_appimagetool)"
echo "==> Packaging AppImage with $TOOL"
export ARCH=x86_64
export APPIMAGE_EXTRACT_AND_RUN="${APPIMAGE_EXTRACT_AND_RUN:-1}"

# Package next to the final file, then move it into place. This keeps a running
# AppImage usable while a new build is produced.
OUTPUT="$DIST/$APP_NAME-x86_64.AppImage"
TEMP_OUTPUT="$OUTPUT.new"
"$TOOL" --no-appstream "$APPDIR" "$TEMP_OUTPUT"

chmod +x "$TEMP_OUTPUT"
mv -f "$TEMP_OUTPUT" "$OUTPUT"
echo "==> Done: $OUTPUT"
