#!/usr/bin/env bash
# On a Mac, after ./release.sh <version> on Linux: builds the macOS apps (signed, in .dmg disk images) and adds them
# to that GitHub release.
# Usage: ./release-mac.sh <version>   e.g. ./release-mac.sh 1.2.0
# Needs: macOS with the .NET 10 SDK, gh (brew install gh; gh auth login), the release already on GitHub.
# Signed with the Developer ID in the keychain and notarized with the "sintoniza" profile when they are there (or
# MAC_SIGN_IDENTITY / MAC_NOTARY_PROFILE); else ad hoc (see the README, macOS).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist"
cd "$ROOT"

version=${1:?Usage: ./release-mac.sh <version>   (e.g. 1.2.0)}
version=${version#v}
tag="v$version"

fail() { echo "$1" >&2; exit 1; }

[[ "$(uname -s)" == Darwin ]] || fail "Run this on a Mac (it signs the apps and makes the .dmg there)."
command -v gh >/dev/null || fail "gh not found (brew install gh), then run: gh auth login"
gh release view "$tag" >/dev/null 2>&1 || fail "There is no release $tag on GitHub yet: run ./release.sh $version first (on Linux)."
git fetch -q --tags origin
[[ "$(git rev-parse HEAD)" == "$(git rev-parse "$tag^{commit}")" ]] || fail "This checkout isn't $tag: run git pull (or git checkout $tag) first."

"$ROOT/build.sh" mac "$version"

files=("$DIST/Sintoniza-$version-macos-arm64.dmg" "$DIST/Sintoniza-$version-macos-x64.dmg")
for f in "${files[@]}"; do [[ -f $f ]] || fail "Missing $f"; done

echo "▶ Adding the macOS apps to $tag"
gh release upload "$tag" "${files[@]}" --clobber
"$ROOT/scripts/release-downloads.sh" "$version"
echo "Done: $(gh release view "$tag" --json url --jq .url)"
