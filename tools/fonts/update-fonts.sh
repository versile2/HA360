#!/usr/bin/env bash
# Refreshes or verifies the self-hosted fonts (03 sections 1.4 and 3.8, 01 section 7.4, 04 card S10b).
#
#   tools/fonts/update-fonts.sh            npm pack the two Fontsource packages, copy three woff2 files and the OFL texts, verify
#   tools/fonts/update-fonts.sh --check    verify the committed files only (no network): sha256 and the 153,600-byte budget
#
# Needs npm (the registry only) for a refresh. Cinzel (variable weight, latin subset) comes from @fontsource-variable/cinzel and Atkinson
# Hyperlegible 400 and 700 (latin subset) from @fontsource/atkinson-hyperlegible; both are SIL OFL 1.1, and the licence file of each package
# is copied next to the fonts under a name with "OFL" in it (the font-budget guard of tools/ci/guards.mjs asks for one per fonts folder).
# The digests below are the integrity check: a refresh whose files differ from them stops before anything is copied, and a version bump
# means editing the two package versions and these lines together. Never hand-edit a font file.
set -euo pipefail

CINZEL_PACKAGE='@fontsource-variable/cinzel@5.3.0'
ATKINSON_PACKAGE='@fontsource/atkinson-hyperlegible@5.3.0'
BUDGET_BYTES=153600

# sha256, then the file name under wwwroot/fonts
EXPECTED=(
  '09941fb1c169c38fd414536b37690057fc01b3117a5c63dd6571186540c8f370  cinzel-latin-wght-normal.woff2'
  'd64ba838ef5472bba248620ec4fd8b5aa7cf0db2908e0bb230600caf279ba7bc  atkinson-hyperlegible-latin-400-normal.woff2'
  '140e2bd25a7315c8a062508391426b0d8c3297400c947b8d847be28f73a199f0  atkinson-hyperlegible-latin-700-normal.woff2'
  'f5a242cf68ad6ebd0603b3359a74c593ca080318a681035be5296ba2c6b04ae6  OFL-Cinzel.txt'
  'e137caeef2ff7c04fbf87078ad09cf3dcba8a3a58a4aa4e2f399d42c50bc4b18  OFL-AtkinsonHyperlegible.txt'
)

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
dest="$repo/src/Realm.Web/wwwroot/fonts"

# sha256sum -c over the EXPECTED list, run inside the folder that holds the files.
verify_digests() {
  (cd "$1" && printf '%s\n' "${EXPECTED[@]}" | sha256sum -c --quiet -)
}

# The woff2 files together stay inside the budget of AC-43 (the guard checks the same number).
verify_budget() {
  local total=0 size file
  for file in "$1"/*.woff2; do
    size=$(wc -c <"$file")
    total=$((total + size))
  done
  echo "fonts: $total bytes of woff2 (budget $BUDGET_BYTES)"
  if ((total > BUDGET_BYTES)); then
    echo "the woff2 files total $total bytes, over the budget of $BUDGET_BYTES" >&2
    return 1
  fi
}

if [[ ${1:-} == --check ]]; then
  verify_digests "$dest"
  verify_budget "$dest"
  exit 0
fi
if [[ $# -gt 0 ]]; then
  echo "usage: tools/fonts/update-fonts.sh [--check]" >&2
  exit 64
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cinzel_tgz=$(cd "$work" && npm pack "$CINZEL_PACKAGE" --silent --ignore-scripts | tail -n 1)
atkinson_tgz=$(cd "$work" && npm pack "$ATKINSON_PACKAGE" --silent --ignore-scripts | tail -n 1)

mkdir -p "$work/cinzel" "$work/atkinson" "$work/stage"
tar -xzf "$work/$cinzel_tgz" -C "$work/cinzel" package/files/cinzel-latin-wght-normal.woff2 package/LICENSE
tar -xzf "$work/$atkinson_tgz" -C "$work/atkinson" \
  package/files/atkinson-hyperlegible-latin-400-normal.woff2 \
  package/files/atkinson-hyperlegible-latin-700-normal.woff2 \
  package/LICENSE

cp "$work/cinzel/package/files/cinzel-latin-wght-normal.woff2" "$work/stage/"
cp "$work/atkinson/package/files/atkinson-hyperlegible-latin-400-normal.woff2" "$work/stage/"
cp "$work/atkinson/package/files/atkinson-hyperlegible-latin-700-normal.woff2" "$work/stage/"
cp "$work/cinzel/package/LICENSE" "$work/stage/OFL-Cinzel.txt"
cp "$work/atkinson/package/LICENSE" "$work/stage/OFL-AtkinsonHyperlegible.txt"

if ! verify_digests "$work/stage"; then
  echo "the files of $CINZEL_PACKAGE and $ATKINSON_PACKAGE differ from the digests in this script; nothing was copied" >&2
  exit 1
fi
verify_budget "$work/stage"

mkdir -p "$dest"
cp "$work"/stage/* "$dest/"
verify_digests "$dest"
