#!/usr/bin/env bash
# Downloads the BASS libraries from un4seen.com into native/<platform>/.
# Usage: scripts/fetch-bass.sh [linux|osx|win|all]   (default: current OS)
# BASS is free for non-commercial use; commercial use needs a license from un4seen.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LIBS=(bass bassmix bassenc bassenc_mp3 bassenc_ogg)

fetch() { # $1 = linux|osx|win
  local plat=$1 suffix dest tmp
  case $plat in
    linux) suffix="-linux"; dest="$ROOT/native/linux-x64" ;;
    osx)   suffix="-osx";   dest="$ROOT/native/osx" ;;
    win)   suffix="";       dest="$ROOT/native/win-x64" ;;
  esac
  local libs=("${LIBS[@]}")
  [[ $plat == linux ]] && libs+=(bass_aac)   # AAC/M4A: Windows and macOS use the OS decoder
  mkdir -p "$dest"
  tmp="$(mktemp -d)"
  for lib in "${libs[@]}"; do
    echo "↓ ${lib}24${suffix}.zip"
    # Core libraries live in /files/, some add-ons (e.g. bass_aac) in /files/z/2/.
    curl -sfL "https://www.un4seen.com/files/${lib}24${suffix}.zip" -o "$tmp/$lib.zip" \
      || curl -sfL "https://www.un4seen.com/files/z/2/${lib}24${suffix}.zip" -o "$tmp/$lib.zip"
    mkdir -p "$tmp/$lib" && unzip -oq "$tmp/$lib.zip" -d "$tmp/$lib"
    case $plat in
      linux) cp "$tmp/$lib"/libs/x86_64/*.so "$dest/" ;;
      osx)   cp "$tmp/$lib"/*.dylib "$dest/" ;;
      win)   cp "$tmp/$lib"/x64/*.dll "$dest/" ;;
    esac
  done
  rm -rf "$tmp"
  echo "→ $dest"
}

target=${1:-}
if [[ -z $target ]]; then
  case "$(uname -s)" in
    Linux) target=linux ;; Darwin) target=osx ;; *) target=win ;;
  esac
fi
if [[ $target == all ]]; then fetch linux; fetch osx; fetch win; else fetch "$target"; fi
