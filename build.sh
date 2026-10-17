#!/usr/bin/env bash
# Builds self-contained release packages into dist/:
#   dist/RadioPlayout-<version>-win-x64.zip
#   dist/RadioPlayout-<version>-win-x64-setup.exe   (needs NSIS: apt install nsis)
#   dist/RadioPlayout-<version>-linux-x64.tar.gz
#   dist/radio-playout_<version>_amd64.deb          (needs dpkg-deb and fakeroot)
#   dist/Sintoniza-<version>-macos-arm64.dmg / -x64.dmg   (on a Mac; a .zip when built on Linux)
# Usage: ./build.sh [win|linux|mac|all] [version]   (defaults: all, version from git; all = win and linux, and mac on a Mac)
# Fetches the BASS libraries for each platform first if they are missing.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/src/Playout.App"
DIST="$ROOT/dist"
NAME=RadioPlayout

target=${1:-all}
version=${2:-$(git -C "$ROOT" describe --tags --always --dirty 2>/dev/null || echo dev)}
# The app compares this with GitHub releases to offer updates; 0.0.0 (untagged build) never offers one.
appver=$(grep -oE '^v?[0-9]+(\.[0-9]+){1,3}' <<<"$version" | tr -d v || true)
appver=${appver:-0.0.0}

command -v dotnet >/dev/null || { echo "dotnet not found. Install the .NET 10 SDK (see README)." >&2; exit 1; }

build() { # $1 = win|linux
  local plat=$1 rid native flac out pkg
  case $plat in
    win)   rid=win-x64;   native="$ROOT/native/win-x64";   flac=bassflac.dll ;;
    linux) rid=linux-x64; native="$ROOT/native/linux-x64"; flac=libbassflac.so ;;
    *) echo "Unknown platform: $plat (use win, linux or all)" >&2; exit 1 ;;
  esac

  # Also when an older checkout lacks an add-on added since (FLAC).
  if [[ -z "$(ls -A "$native" 2>/dev/null)" || ! -e "$native/$flac" ]]; then
    "$ROOT/scripts/fetch-bass.sh" "$plat"
  fi

  pkg="$NAME-$version-$rid"
  out="$DIST/$pkg"
  rm -rf "$out"

  echo "▶ Publishing $rid"
  # The app and radio-import (library scanner) share one folder and one .NET runtime.
  for project in "$PROJECT" "$ROOT/src/Playout.Import"; do
    dotnet publish "$project" -c Release -r "$rid" --self-contained \
      -p:DebugType=none -p:DebugSymbols=false -p:Version="$appver" -p:InformationalVersion="${version#v}" -o "$out"
  done

  cp "$ROOT/README.md" "$out/"

  echo "▶ Packaging $pkg"
  case $plat in
    win)
      rm -f "$DIST/$pkg.zip"
      if command -v zip >/dev/null; then
        (cd "$DIST" && zip -qr "$pkg.zip" "$pkg")
      elif command -v 7z >/dev/null; then
        (cd "$DIST" && 7z a -tzip -bd "$pkg.zip" "$pkg" >/dev/null)
      else
        echo "Neither zip nor 7z found; cannot create the Windows package." >&2; exit 1
      fi
      echo "→ $DIST/$pkg.zip"
      installer "$out" "$DIST/$pkg-setup.exe"
      ;;
    linux)
      chmod +x "$out/$NAME" "$out/radio-import"
      tar -C "$DIST" -czf "$DIST/$pkg.tar.gz" "$pkg"
      echo "→ $DIST/$pkg.tar.gz"
      deb "$out"
      ;;
  esac
}

installer() { # $1 = publish dir, $2 = setup.exe path
  if ! command -v makensis >/dev/null; then
    echo "⚠ makensis not found, skipping the Windows installer (install NSIS: sudo apt install nsis)" >&2
    return
  fi
  echo "▶ Building Windows installer"
  makensis -V2 -INPUTCHARSET UTF8 -DVERSION="$version" -DSRCDIR="$1" -DOUTFILE="$2" "$ROOT/packaging/windows/installer.nsi"
  echo "→ $2"
}

deb() { # $1 = publish dir
  local pkg=radio-playout debver root file
  # Debian versions must start with a digit; fall back to 0.0.0~git<hash> for untagged builds.
  debver=${version#v}
  [[ $debver =~ ^[0-9] ]] || debver="0.0.0~git$debver"
  debver="$debver-1"
  file="$DIST/${pkg}_${debver}_amd64.deb"
  root="$(mktemp -d)"

  echo "▶ Building $pkg $debver .deb"
  mkdir -p "$root/DEBIAN" "$root/opt/$pkg" "$root/usr/bin" "$root/usr/share/applications" \
    "$root/usr/share/icons/hicolor/256x256/apps" "$root/usr/share/icons/hicolor/scalable/apps"
  cp -r "$1"/. "$root/opt/$pkg/"
  cat > "$root/usr/bin/$pkg" <<SH
#!/bin/sh
exec /opt/$pkg/$NAME "\$@"
SH
  for tool in radio-import; do
    cat > "$root/usr/bin/$tool" <<SH
#!/bin/sh
exec /opt/$pkg/$tool "\$@"
SH
  done
  cp "$ROOT/packaging/linux/$pkg.desktop" "$ROOT/packaging/linux/$pkg-schedule.desktop" "$root/usr/share/applications/"
  cp "$ROOT/packaging/linux/$pkg.png" "$ROOT/packaging/linux/$pkg-schedule.png" "$root/usr/share/icons/hicolor/256x256/apps/"
  cp "$ROOT/packaging/icon.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg.svg"
  cp "$ROOT/packaging/icon-schedule.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg-schedule.svg"

  find "$root" -type d -exec chmod 755 {} +
  find "$root" -type f -exec chmod 644 {} +
  chmod 755 "$root/opt/$pkg/$NAME" "$root/opt/$pkg/radio-import" "$root/usr/bin/$pkg" "$root/usr/bin/radio-import"

  cat > "$root/DEBIAN/control" <<CTL
Package: $pkg
Version: $debver
Architecture: amd64
Maintainer: César Araújo <cesarsilvaaraujo@gmail.com>
Homepage: https://cesararaujo.net
Installed-Size: $(du -sk "$root" | cut -f1)
Depends: libasound2t64 | libasound2, libfontconfig1, libx11-6, libice6, libsm6, libicu76 | libicu74 | libicu72 | libicu70 | libicu67 | libicu66
Section: sound
Priority: optional
Description: Sintoniza Radio Broadcast system - radio automation and playout
 Broadcast radio playout with crossfades, cartwall, cue points and
 Icecast/Shoutcast streaming.
CTL

  fakeroot dpkg-deb --build "$root" "$file" >/dev/null
  rm -rf "$root"
  echo "→ $file"
}

# macOS: two apps per Mac type (Apple Silicon arm64, Intel x64), Sintoniza Radio Broadcast system.app and
# Sintoniza Schedule.app (a launcher that starts the first with --schedule). On a Mac they are signed (ad hoc, or with
# the Developer ID in MAC_SIGN_IDENTITY) and put in a .dmg; on Linux they go in a .zip, signed ad hoc only if rcodesign
# is installed: an Apple Silicon Mac won't start an unsigned app (sign it there with codesign, see the README).
mac() {
  local native="$ROOT/native/osx" rid out pkg app sched stage file appname="Sintoniza Radio Broadcast system"
  if [[ -z "$(ls -A "$native" 2>/dev/null)" || ! -e "$native/libbassflac.dylib" ]]; then
    "$ROOT/scripts/fetch-bass.sh" osx
  fi
  for rid in osx-arm64 osx-x64; do
    pkg="Sintoniza-$version-macos-${rid#osx-}"
    out="$DIST/$pkg"
    rm -rf "$out"
    app="$out/$appname.app"
    sched="$out/Sintoniza Schedule.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$sched/Contents/MacOS" "$sched/Contents/Resources"

    echo "▶ Publishing $rid"
    for project in "$PROJECT" "$ROOT/src/Playout.Import"; do
      dotnet publish "$project" -c Release -r "$rid" --self-contained \
        -p:DebugType=none -p:DebugSymbols=false -p:Version="$appver" -p:InformationalVersion="${version#v}" -o "$app/Contents/MacOS"
    done
    cp "$ROOT/README.md" "$out/"
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
    cp "$ROOT/packaging/macos/icon.icns" "$app/Contents/Resources/"
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Schedule-Info.plist" > "$sched/Contents/Info.plist"
    cp "$ROOT/packaging/macos/schedule.icns" "$sched/Contents/Resources/"
    cp "$ROOT/packaging/macos/sintoniza-schedule" "$sched/Contents/MacOS/"
    chmod +x "$app/Contents/MacOS/$NAME" "$app/Contents/MacOS/radio-import" "$sched/Contents/MacOS/sintoniza-schedule"

    echo "▶ Signing $pkg"
    if command -v codesign >/dev/null; then
      local identity=${MAC_SIGN_IDENTITY:--}
      for bundle in "$app" "$sched"; do
        if [[ $identity == - ]]; then
          codesign --force --deep --sign - "$bundle"
        else
          codesign --force --deep --options runtime --timestamp --entitlements "$ROOT/packaging/macos/entitlements.plist" \
            --sign "$identity" "$bundle"
        fi
      done
    elif command -v rcodesign >/dev/null; then
      rcodesign sign "$app" >/dev/null && rcodesign sign "$sched" >/dev/null
    elif [[ $rid == osx-arm64 ]]; then
      echo "⚠ Not signed (no codesign or rcodesign): on an Apple Silicon Mac run once" >&2
      echo "    codesign --force --deep --sign - \"/Applications/$appname.app\" \"/Applications/Sintoniza Schedule.app\"" >&2
    fi

    echo "▶ Packaging $pkg"
    if command -v hdiutil >/dev/null; then
      # The disk image: both apps and a link to Applications to drag them onto.
      stage="$(mktemp -d)"
      cp -R "$app" "$sched" "$out/README.md" "$stage/"
      ln -s /Applications "$stage/Applications"
      file="$DIST/$pkg.dmg"
      rm -f "$file"
      hdiutil create -quiet -volname "Sintoniza $version" -srcfolder "$stage" -fs HFS+ -format UDZO "$file"
      rm -rf "$stage"
    else
      file="$DIST/$pkg.zip"
      rm -f "$file"
      (cd "$out" && zip -qry "$file" "$appname.app" "Sintoniza Schedule.app" README.md)
    fi
    echo "→ $file"
  done
}

case $target in
  all)
    build win
    build linux
    if [[ "$(uname -s)" == Darwin ]]; then mac; fi
    ;;
  mac) mac ;;
  *)   build "$target" ;;
esac
