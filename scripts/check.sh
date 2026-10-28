#!/usr/bin/env bash
# Sintoniza's checks before a release (release.sh runs this first; run it any time): everything builds with no
# warnings, the translations are complete, and the headless checks pass (tests/Sintoniza.Checks: the on-air engine,
# the on-air program and Sintoniza Pro, with no sound, on a library and settings of their own — yours are never
# touched). Ends with an error if anything isn't right.
# Usage: scripts/check.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "▶ Build"
log=$(mktemp)
if ! dotnet build RadioPlayout.slnx -nologo -v q >"$log" 2>&1; then
  grep -E "error" "$log" | sort -u | head -20 >&2
  rm -f "$log"
  echo "✗ The build failed." >&2
  exit 1
fi
warnings=$(grep -E ": warning " "$log" | sort -u || true)
rm -f "$log"
if [[ -n $warnings ]]; then
  echo "$warnings" >&2
  echo "✗ The build has warnings." >&2
  exit 1
fi

echo "▶ Translations"
python3 "$ROOT/scripts/i18n-check.py" | tail -1
python3 "$ROOT/scripts/i18n-check.py" | tail -1 | grep -q " 0 problems" || { echo "✗ Texts without a translation (above)." >&2; exit 1; }

echo "▶ Headless checks"
work=$(mktemp -d)
trap 'rm -rf "${work:?}"' EXIT
mkdir -p "$work/config"
# Its own settings folder and computer name: the checks write settings and a library there.
XDG_CONFIG_HOME="$work/config" HOME="$work" SINTONIZA_COMPUTER=checks \
  dotnet "$ROOT/tests/Sintoniza.Checks/bin/Debug/net10.0/Sintoniza.Checks.dll" "$work" --device=0 </dev/null
echo "✓ Ready to release"
