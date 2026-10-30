#!/usr/bin/env bash
# Builds all packages locally with build.sh and publishes them as a GitHub Release.
# Usage: ./release.sh <version>   e.g. ./release.sh 0.1.0
# Needs: gh (logged in), NSIS, and the current commit pushed to GitHub.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist"
cd "$ROOT"

version=${1:?Usage: ./release.sh <version>   (e.g. 0.1.0)}
version=${version#v}
tag="v$version"

fail() { echo "$1" >&2; exit 1; }

command -v gh >/dev/null || fail "gh not found (sudo apt install gh), then run: gh auth login"
command -v makensis >/dev/null || fail "makensis not found; the Windows installer needs NSIS (sudo apt install nsis)"
[[ -z "$(git status --porcelain)" ]] || fail "You have uncommitted changes. Commit them first."
git rev-parse -q --verify "refs/tags/$tag" >/dev/null && fail "Tag $tag already exists."
git fetch -q origin
[[ -n "$(git branch -r --contains HEAD)" ]] || fail "This commit isn't on GitHub yet. Run: git push"

# Builds, translations and the headless checks first: nothing is published if one fails.
"$ROOT/scripts/check.sh"

"$ROOT/build.sh" all "$version"

files=(
  "$DIST/RadioPlayout-$version-win-x64-setup.exe"
  "$DIST/RadioPlayout-$version-win-x64.zip"
  "$DIST/radio-playout_$version-1_amd64.deb"
  "$DIST/radio-playout-$version-1.x86_64.rpm"
  "$DIST/RadioPlayout-$version-linux-x64.tar.gz"
)
for f in "${files[@]}"; do [[ -f $f ]] || fail "Missing $f"; done
# The packages as they will be downloaded: the programs inside can start (their compiled windows are there).
check=$(mktemp -d)
unzip -q "$DIST/RadioPlayout-$version-win-x64.zip" "*/SintonizaEmi.dll" -d "$check"
grep -aqr CompiledAvaloniaXaml "$check" || { rm -rf "${check:?}"; fail "The Windows package's programs would not start (no compiled XAML)"; }
tar -xzf "$DIST/RadioPlayout-$version-linux-x64.tar.gz" -C "$check" --wildcards "*/SintonizaEmi.dll"
[[ $(grep -alr CompiledAvaloniaXaml "$check" | wc -l) -ge 2 ]] || { rm -rf "${check:?}"; fail "The Linux package's programs would not start (no compiled XAML)"; }
rm -rf "${check:?}"

echo "▶ Publishing $tag"
git tag -a "$tag" -m "Sintoniza $tag"
git push -q origin "$tag"
gh release create "$tag" "${files[@]}" --verify-tag --title "Sintoniza $tag" --generate-notes
"$ROOT/scripts/release-downloads.sh" "$version"
