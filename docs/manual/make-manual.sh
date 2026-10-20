#!/usr/bin/env bash
# Makes the user manual, docs/manual/Sintoniza-Manual.pdf: a demo station in /tmp/sintoniza (its own settings, an SQLite
# library and generated audio; your own settings are never touched), every window photographed headless with no sound
# (tools/ManualShots), then the pages put together and printed to PDF with Chromium (build.py).
# Needs: the .NET SDK, ffmpeg, python3 with Pillow, chromium (or google-chrome).
# Usage: docs/manual/make-manual.sh [en|pt|all]   (default: all — the English and the Portuguese manual)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

manual() { # $1 = en | pt
  # The demo station lives at a neutral path (it shows in the screenshots: no user or computer names in the manual).
  local lang=$1 work="${SINTONIZA_DEMO:-/tmp/sintoniza}/$1" shots="$ROOT/build/manual/shots-$1" pdf
  pdf="$ROOT/docs/manual/Sintoniza-Manual$([[ $lang == en ]] || echo "-${lang^^}").pdf"
  mkdir -p "$work/config" "$shots"
  rm -rf "$work/config/RadioPlayout" # a fresh station each time (the generated audio is kept)
  echo "▶ Screenshots ($lang)"
  # No user or computer names in the screenshots.
  SINTONIZA_COMPUTER=studio SINTONIZA_USER=presenter XDG_CONFIG_HOME="$work/config" \
    dotnet run --project "$ROOT/tools/ManualShots" -c Release -- "$work" "$shots" "$lang" --device=0 </dev/null
  # The web remote on a phone: its page with the demo station's status, at a phone's size.
  local browser; browser=$(command -v chromium || command -v chromium-browser || command -v google-chrome || true)
  if [[ -n $browser && -f "$shots/remote-mock.html" ]]; then
    "$browser" --headless --disable-gpu --no-sandbox --hide-scrollbars --window-size=420,1100 \
      --screenshot="$shots/remote-phone.png" "file://$shots/remote-mock.html" >/dev/null 2>&1 || true
  fi
  echo "▶ PDF ($lang)"
  python3 "$ROOT/docs/manual/build.py" "$shots" "$pdf" "$lang"
}

case ${1:-all} in
  all) manual en; manual pt ;;
  *)   manual "$1" ;;
esac
