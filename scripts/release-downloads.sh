#!/usr/bin/env bash
# Puts a table of the release's downloads, by system (Windows, macOS, Linux), in its notes on GitHub, after what
# changed. Run by release.sh and release-mac.sh; run it again any time: the table is replaced, not added twice.
# Usage: scripts/release-downloads.sh <version> [--dry-run]   (--dry-run: print the notes, change nothing)
set -euo pipefail

version=${1:?Usage: scripts/release-downloads.sh <version> [--dry-run]}
version=${version#v}
tag="v$version"
dry=${2:-}

repo=$(gh repo view --json url --jq .url)
assets=()
while IFS= read -r a; do assets+=("$a"); done < <(gh release view "$tag" --json assets --jq '.assets[].name') # macOS's bash 3.2: no mapfile
body=$(gh release view "$tag" --json body --jq .body)

# What was there before the table (the table of an earlier run left out).
start='<!-- downloads -->'
end='<!-- /downloads -->'
notes=$(printf '%s\n' "$body" | awk -v s="$start" -v e="$end" '$0 == s { skip = 1 } !skip { print } $0 == e { skip = 0 }')

has() { local a; for a in "${assets[@]}"; do [[ $a == "$1" ]] && return 0; done; return 1; }
link() { echo "<a href=\"$repo/releases/download/$tag/$1\">$1</a>"; }

# One system: its name over all its rows, then a row per file that is there.
section() {
  local system=$1; shift
  local rows=() kind file
  while (($#)); do
    kind=$1 file=$2; shift 2
    has "$file" && rows+=("<td>$kind</td><td>$(link "$file")</td>")
  done
  ((${#rows[@]})) || return 0 # none yet (macOS: added by release-mac.sh)
  echo "<tr><td rowspan=\"${#rows[@]}\">$system</td>${rows[0]}</tr>"
  local r; for r in "${rows[@]:1}"; do echo "<tr>$r</tr>"; done
}

table=$(
  echo "$start"
  echo "## Downloads"
  echo
  echo "<table>"
  section Windows \
    "Installer" "RadioPlayout-$version-win-x64-setup.exe" \
    ".zip (no installer)" "RadioPlayout-$version-win-x64.zip"
  section macOS \
    "Apple Silicon (M1 and later)" "Sintoniza-$version-macos-arm64.dmg" \
    "Intel" "Sintoniza-$version-macos-x64.dmg"
  section Linux \
    ".deb (Debian, Ubuntu, Mint)" "radio-playout_$version-1_amd64.deb" \
    ".rpm (Fedora, openSUSE, RHEL)" "radio-playout-$version-1.x86_64.rpm" \
    ".tar.gz (any distribution)" "RadioPlayout-$version-linux-x64.tar.gz"
  echo "</table>"
  echo "$end"
)

new=$(printf '%s\n\n%s\n' "$(printf '%s' "$notes" | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}')" "$table")
if [[ $dry == --dry-run ]]; then printf '%s\n' "$new"; exit 0; fi
file=$(mktemp)
printf '%s\n' "$new" >"$file"
gh release edit "$tag" --notes-file "$file" >/dev/null
rm -f "$file"
echo "Downloads table in the notes of $tag"
