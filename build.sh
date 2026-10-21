#!/usr/bin/env bash
# Builds self-contained release packages into dist/ (Sintoniza Emi, Pro, Rec and Import in each):
#   dist/RadioPlayout-<version>-win-x64.zip
#   dist/RadioPlayout-<version>-win-x64-setup.exe   (needs NSIS: apt install nsis)
#   dist/RadioPlayout-<version>-linux-x64.tar.gz
#   dist/radio-playout_<version>_amd64.deb          (needs dpkg-deb and fakeroot)
#   dist/radio-playout-<version>-1.x86_64.rpm       (needs rpmbuild: apt install rpm)
#   dist/Sintoniza-<version>-macos-arm64.dmg / -x64.dmg   (on a Mac; a .zip when built on Linux)
# Usage: ./build.sh [win|linux|mac|all] [version]   (defaults: all, version from git; all = win and linux, and mac on a Mac)
# Fetches the BASS libraries for each platform first if they are missing.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/src/Playout.App"
DIST="$ROOT/dist"
NAME=RadioPlayout # the packages' names (the in-app updater looks for these)
EXE=SintonizaEmi  # the on-air program (Sintoniza Emi); RadioPlayout before

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
  # Sintoniza Emi, Sintoniza Pro, Sintoniza Rec and radio-import (library scanner) share one folder and one .NET runtime.
  for project in "$PROJECT" "$ROOT/src/Playout.Schedule" "$ROOT/src/Playout.Rec" "$ROOT/src/Playout.Import"; do
    dotnet publish "$project" -c Release -r "$rid" --self-contained \
      -p:DebugType=none -p:DebugSymbols=false -p:Version="$appver" -p:InformationalVersion="${version#v}" -o "$out"
  done

  cp "$ROOT/README.md" "$out/"
  cp "$ROOT"/docs/manual/Sintoniza-Manual*.pdf "$out/" 2>/dev/null || echo "⚠ No user manual in docs/manual (make it with docs/manual/make-manual.sh)" >&2

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
      chmod +x "$out/$EXE" "$out/SintonizaPro" "$out/SintonizaRec" "$out/radio-import"
      # Its name before, for what still starts it so (autostart entries, the updater of an older version).
      ln -sf "$EXE" "$out/RadioPlayout"
      tar -C "$DIST" -czf "$DIST/$pkg.tar.gz" "$pkg"
      echo "→ $DIST/$pkg.tar.gz"
      deb "$out"
      rpm_package "$out"
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

# The files a Linux package installs: the app in /opt/radio-playout, commands in /usr/bin, menu entries and icons.
linux_tree() { # $1 = publish dir, $2 = root to fill
  local pkg=radio-playout root=$2
  mkdir -p "$root/opt/$pkg" "$root/usr/bin" "$root/usr/share/applications" \
    "$root/usr/share/icons/hicolor/256x256/apps" "$root/usr/share/icons/hicolor/scalable/apps"
  cp -r "$1"/. "$root/opt/$pkg/"
  cat > "$root/usr/bin/$pkg" <<SH
#!/bin/sh
exec /opt/$pkg/$EXE "\$@"
SH
  ln -sf $pkg "$root/usr/bin/sintoniza-emi"
  for tool in radio-import; do
    cat > "$root/usr/bin/$tool" <<SH
#!/bin/sh
exec /opt/$pkg/$tool "\$@"
SH
  done
  # The user manual: in the package's documentation folder, and a menu entry that opens it in the desktop's language.
  mkdir -p "$root/usr/share/doc/$pkg"
  cp "$1"/Sintoniza-Manual*.pdf "$root/usr/share/doc/$pkg/" 2>/dev/null || true
  cat > "$root/usr/bin/sintoniza-manual" <<'SH'
#!/bin/sh
# Opens the Sintoniza user manual, in Portuguese when the desktop is in Portuguese.
doc=/usr/share/doc/radio-playout
case "${LANGUAGE:-${LC_ALL:-${LC_MESSAGES:-$LANG}}}" in
  pt*) [ -f "$doc/Sintoniza-Manual-PT.pdf" ] && exec xdg-open "$doc/Sintoniza-Manual-PT.pdf" ;;
esac
exec xdg-open "$doc/Sintoniza-Manual.pdf"
SH
  cat > "$root/usr/bin/sintoniza-pro" <<SH
#!/bin/sh
exec /opt/$pkg/SintonizaPro "\$@"
SH
  cat > "$root/usr/bin/sintoniza-rec" <<SH
#!/bin/sh
exec /opt/$pkg/SintonizaRec "\$@"
SH
  # Its names before: Sintoniza Schedule, Sintoniza Production.
  ln -s sintoniza-pro "$root/usr/bin/sintoniza-schedule"
  ln -s sintoniza-pro "$root/usr/bin/sintoniza-production"
  cp "$ROOT/packaging/linux/$pkg.desktop" "$ROOT/packaging/linux/$pkg-schedule.desktop" "$ROOT/packaging/linux/$pkg-import.desktop" \
    "$ROOT/packaging/linux/$pkg-rec.desktop" "$ROOT/packaging/linux/$pkg-manual.desktop" "$root/usr/share/applications/"
  cp "$ROOT/packaging/linux/$pkg.png" "$ROOT/packaging/linux/$pkg-schedule.png" "$ROOT/packaging/linux/$pkg-import.png" \
    "$ROOT/packaging/linux/$pkg-rec.png" "$root/usr/share/icons/hicolor/256x256/apps/"
  cp "$ROOT/packaging/icon.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg.svg"
  cp "$ROOT/packaging/icon-schedule.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg-schedule.svg"
  cp "$ROOT/packaging/icon-import.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg-import.svg"
  cp "$ROOT/packaging/icon-rec.svg" "$root/usr/share/icons/hicolor/scalable/apps/$pkg-rec.svg"

  find "$root" -type d -exec chmod 755 {} +
  find "$root" -type f -exec chmod 644 {} +
  chmod 755 "$root/opt/$pkg/$EXE" "$root/opt/$pkg/SintonizaPro" "$root/opt/$pkg/SintonizaRec" "$root/opt/$pkg/radio-import" \
    "$root/usr/bin/$pkg" "$root/usr/bin/radio-import" "$root/usr/bin/sintoniza-pro" "$root/usr/bin/sintoniza-rec" \
    "$root/usr/bin/sintoniza-manual"
}

# Package versions must start with a digit; untagged builds are 0.0.0~git<hash>. $1 = what may not appear (- for rpm).
package_version() {
  local v=${version#v}
  [[ $v =~ ^[0-9] ]] || v="0.0.0~git$v"
  echo "${v//$1/\~}"
}

deb() { # $1 = publish dir
  local pkg=radio-playout debver root file
  debver="$(package_version _)-1"
  file="$DIST/${pkg}_${debver}_amd64.deb"
  root="$(mktemp -d)"

  echo "▶ Building $pkg $debver .deb"
  linux_tree "$1" "$root"
  mkdir -p "$root/DEBIAN"
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
Description: Sintoniza - radio automation, production and recording
 Sintoniza Emi (on-air playout with crossfades, cartwall, cue points and
 Icecast/Shoutcast streaming), Sintoniza Pro (library, schedule and
 advertising) and Sintoniza Rec (the broadcast recorder).
CTL

  fakeroot dpkg-deb --build "$root" "$file" >/dev/null
  rm -rf "$root"
  echo "→ $file"
}

# Fedora, RHEL, openSUSE…: the same files as the .deb. The app is self-contained (its own .NET), so rpm isn't left
# to work out dependencies from its libraries: only the system ones it needs are listed. A marker file tells the
# in-app updater it was installed from an .rpm.
rpm_package() { # $1 = publish dir
  local pkg=radio-playout rpmver top stage file
  if ! command -v rpmbuild >/dev/null; then
    echo "⚠ rpmbuild not found, skipping the .rpm (install it: sudo apt install rpm, or dnf install rpm-build)" >&2
    return
  fi
  rpmver="$(package_version -)"
  top="$(mktemp -d)"
  stage="$top/stage"
  file="$DIST/$pkg-$rpmver-1.x86_64.rpm"

  echo "▶ Building $pkg $rpmver .rpm"
  linux_tree "$1" "$stage"
  echo "rpm" > "$stage/opt/$pkg/package-rpm"
  chmod 644 "$stage/opt/$pkg/package-rpm"
  mkdir -p "$top/SPECS" "$top/RPMS"
  cat > "$top/SPECS/$pkg.spec" <<SPEC
Name:           $pkg
Version:        $rpmver
Release:        1
Summary:        Sintoniza - radio automation, production and recording
License:        Proprietary
URL:            https://cesararaujo.net
Packager:       César Araújo <cesarsilvaaraujo@gmail.com>
BuildArch:      x86_64
AutoReqProv:    no
Requires:       alsa-lib, fontconfig, libX11, libICE, libSM, libicu

%global debug_package %{nil}
%global __os_install_post %{nil}
%define _build_id_links none

%description
Broadcast radio playout with crossfades, cartwall, cue points and
Icecast/Shoutcast streaming.

%install
cp -a "$stage"/. %{buildroot}/

%files
/opt/$pkg
/usr/bin/$pkg
/usr/bin/radio-import
/usr/bin/sintoniza-emi
/usr/bin/sintoniza-rec
/usr/bin/sintoniza-pro
/usr/bin/sintoniza-production
/usr/bin/sintoniza-schedule
/usr/bin/sintoniza-manual
/usr/share/doc/$pkg
/usr/share/applications/$pkg-manual.desktop
/usr/share/applications/$pkg.desktop
/usr/share/applications/$pkg-schedule.desktop
/usr/share/applications/$pkg-import.desktop
/usr/share/applications/$pkg-rec.desktop
/usr/share/icons/hicolor/256x256/apps/$pkg.png
/usr/share/icons/hicolor/256x256/apps/$pkg-schedule.png
/usr/share/icons/hicolor/256x256/apps/$pkg-import.png
/usr/share/icons/hicolor/256x256/apps/$pkg-rec.png
/usr/share/icons/hicolor/scalable/apps/$pkg.svg
/usr/share/icons/hicolor/scalable/apps/$pkg-schedule.svg
/usr/share/icons/hicolor/scalable/apps/$pkg-import.svg
/usr/share/icons/hicolor/scalable/apps/$pkg-rec.svg
SPEC

  rpmbuild -bb --quiet --define "_topdir $top" "$top/SPECS/$pkg.spec" >/dev/null
  mv "$top"/RPMS/x86_64/*.rpm "$file"
  rm -rf "$top"
  echo "→ $file"
}

# macOS: four apps per Mac type (Apple Silicon arm64, Intel x64): Sintoniza Emi.app, and Sintoniza Pro.app,
# Sintoniza Rec.app and Sintoniza Import.app (launchers that start its SintonizaPro or SintonizaRec program, or it
# with --import). On a Mac they are signed (ad hoc, or with
# the Developer ID in MAC_SIGN_IDENTITY) and put in a .dmg; on Linux they go in a .zip, signed ad hoc only if rcodesign
# is installed: an Apple Silicon Mac won't start an unsigned app (sign it there with codesign, see the README).
mac() {
  local native="$ROOT/native/osx" rid out pkg app sched imp stage file appname="Sintoniza Emi" rec
  if [[ -z "$(ls -A "$native" 2>/dev/null)" || ! -e "$native/libbassflac.dylib" ]]; then
    "$ROOT/scripts/fetch-bass.sh" osx
  fi
  for rid in osx-arm64 osx-x64; do
    pkg="Sintoniza-$version-macos-${rid#osx-}"
    out="$DIST/$pkg"
    rm -rf "$out"
    app="$out/$appname.app"
    sched="$out/Sintoniza Pro.app"
    imp="$out/Sintoniza Import.app"
    rec="$out/Sintoniza Rec.app"
    mkdir -p "$rec/Contents/MacOS" "$rec/Contents/Resources"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$sched/Contents/MacOS" "$sched/Contents/Resources" \
      "$imp/Contents/MacOS" "$imp/Contents/Resources"

    echo "▶ Publishing $rid"
    for project in "$PROJECT" "$ROOT/src/Playout.Schedule" "$ROOT/src/Playout.Rec" "$ROOT/src/Playout.Import"; do
      dotnet publish "$project" -c Release -r "$rid" --self-contained \
        -p:DebugType=none -p:DebugSymbols=false -p:Version="$appver" -p:InformationalVersion="${version#v}" -o "$app/Contents/MacOS"
    done
    cp "$ROOT/README.md" "$out/"
    # The user manual: next to the apps on the disk image, and inside the main app (Settings → About opens it).
    cp "$ROOT"/docs/manual/Sintoniza-Manual*.pdf "$out/" 2>/dev/null || true
    cp "$ROOT"/docs/manual/Sintoniza-Manual*.pdf "$app/Contents/MacOS/" 2>/dev/null || true
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
    cp "$ROOT/packaging/macos/icon.icns" "$app/Contents/Resources/"
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Schedule-Info.plist" > "$sched/Contents/Info.plist"
    cp "$ROOT/packaging/macos/schedule.icns" "$sched/Contents/Resources/"
    cp "$ROOT/packaging/macos/sintoniza-schedule" "$sched/Contents/MacOS/"
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Import-Info.plist" > "$imp/Contents/Info.plist"
    cp "$ROOT/packaging/macos/import.icns" "$imp/Contents/Resources/"
    cp "$ROOT/packaging/macos/sintoniza-import" "$imp/Contents/MacOS/"
    sed "s/@VERSION@/$appver/g" "$ROOT/packaging/macos/Rec-Info.plist" > "$rec/Contents/Info.plist"
    cp "$ROOT/packaging/macos/rec.icns" "$rec/Contents/Resources/"
    cp "$ROOT/packaging/macos/sintoniza-rec" "$rec/Contents/MacOS/"
    chmod +x "$app/Contents/MacOS/$EXE" "$app/Contents/MacOS/SintonizaPro" "$app/Contents/MacOS/SintonizaRec" \
      "$app/Contents/MacOS/radio-import" "$sched/Contents/MacOS/sintoniza-schedule" "$imp/Contents/MacOS/sintoniza-import" \
      "$rec/Contents/MacOS/sintoniza-rec"

    echo "▶ Signing $pkg"
    if command -v codesign >/dev/null; then
      local identity=${MAC_SIGN_IDENTITY:--}
      for bundle in "$app" "$sched" "$rec" "$imp"; do
        if [[ $identity == - ]]; then
          codesign --force --deep --sign - "$bundle"
        else
          codesign --force --deep --options runtime --timestamp --entitlements "$ROOT/packaging/macos/entitlements.plist" \
            --sign "$identity" "$bundle"
        fi
      done
    elif command -v rcodesign >/dev/null; then
      rcodesign sign "$app" >/dev/null && rcodesign sign "$sched" >/dev/null && rcodesign sign "$rec" >/dev/null && rcodesign sign "$imp" >/dev/null
    elif [[ $rid == osx-arm64 ]]; then
      echo "⚠ Not signed (no codesign or rcodesign): on an Apple Silicon Mac run once" >&2
      echo "    codesign --force --deep --sign - \"/Applications/$appname.app\" \"/Applications/Sintoniza Pro.app\" \"/Applications/Sintoniza Rec.app\" \"/Applications/Sintoniza Import.app\"" >&2
    fi

    echo "▶ Packaging $pkg"
    if command -v hdiutil >/dev/null; then
      # The disk image: the apps and a link to Applications to drag them onto.
      stage="$(mktemp -d)"
      cp -R "$app" "$sched" "$rec" "$imp" "$out/README.md" "$stage/"
      cp "$out"/Sintoniza-Manual*.pdf "$stage/" 2>/dev/null || true
      ln -s /Applications "$stage/Applications"
      file="$DIST/$pkg.dmg"
      rm -f "$file"
      hdiutil create -quiet -volname "Sintoniza $version" -srcfolder "$stage" -fs HFS+ -format UDZO "$file"
      rm -rf "$stage"
    else
      file="$DIST/$pkg.zip"
      rm -f "$file"
      (cd "$out" && zip -qry "$file" "$appname.app" "Sintoniza Pro.app" "Sintoniza Rec.app" "Sintoniza Import.app" README.md Sintoniza-Manual*.pdf)
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
