#!/usr/bin/env bash
# Refreshes or verifies the vendored MapLibre GL JS files (03 section 1.4, 04 card S5a).
#
#   tools/vendor-maplibre.sh            npm pack maplibre-gl@VERSION, copy the files, write VERSION.txt and SHA256SUMS, verify
#   tools/vendor-maplibre.sh --check    verify the committed files only (no network): sha256sum -c SHA256SUMS
#
# Needs npm (the registry only) for a refresh. The digests below are the integrity check of 03 section 1.4: a refresh whose
# files differ from them stops before anything is copied, and a version bump means editing VERSION and these lines together with
# package.json (the vendored-maplibre guard compares VERSION.txt with package.json). Never hand-edit a vendored file.
set -euo pipefail

VERSION=6.11.2
TARBALL_INTEGRITY='sha512-Xh06pxoipjX/Ad1sUPGhiNh9go9naCNGZZ1IJm3sIeqK1kYGuMaMDSTIGDQ2igOLfVyukJU7kvEOA089VO7Z7g=='

# file (relative to the package root of the tarball) -> expected sha256
EXPECTED=(
  '3f55566295583644617fe17d008a36c580414b8c71dd2e1fcff1309de6fdee5d  dist/maplibre-gl.mjs'
  '76b5f55bdee928c65d592684aaff2b913d50b6b17b0ec6334e88b09b6aa47960  dist/maplibre-gl-shared.mjs'
  '01ad197aa7f4cec258a890febd71b7515e96309881b036a7095befc01a45296e  dist/maplibre-gl-worker.mjs'
  'd8617d8421930e3fc6185365400e788c374c1a5d9fbe87999998c0bc14a202d3  dist/maplibre-gl.css'
  'ee5fc05a0677eaf69601d2c7db0d9ecd6cc27c3abc1d0733bc9ed34707cf8ef2  LICENSE.txt'
)

repo=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
dest="$repo/src/Realm.Web/wwwroot/lib/maplibre-gl"

if [[ ${1:-} == --check ]]; then
  cd "$dest"
  sha256sum -c SHA256SUMS
  grep -q "^maplibre-gl $VERSION\$" VERSION.txt || { echo "VERSION.txt does not say 'maplibre-gl $VERSION'" >&2; exit 1; }
  exit 0
fi
if [[ $# -gt 0 ]]; then
  echo "usage: tools/vendor-maplibre.sh [--check]" >&2
  exit 64
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

(cd "$work" && npm pack "maplibre-gl@$VERSION" --silent --ignore-scripts >/dev/null)
tarball="$work/maplibre-gl-$VERSION.tgz"

# The registry's own integrity string (npm view ... dist.integrity) is the sha512 of the tarball, base64.
actual=$(node -e "const c=require('node:crypto'),f=require('node:fs');process.stdout.write('sha512-'+c.createHash('sha512').update(f.readFileSync(process.argv[1])).digest('base64'))" "$tarball")
if [[ $actual != "$TARBALL_INTEGRITY" ]]; then
  echo "maplibre-gl@$VERSION tarball integrity is $actual, expected $TARBALL_INTEGRITY" >&2
  exit 1
fi

files=()
for entry in "${EXPECTED[@]}"; do files+=("package/${entry#*  }"); done
tar -xzf "$tarball" -C "$work" "${files[@]}"

for entry in "${EXPECTED[@]}"; do
  digest=${entry%%  *}
  file=${entry#*  }
  got=$(sha256sum "$work/package/$file" | cut -d' ' -f1)
  if [[ $got != "$digest" ]]; then
    echo "$file: sha256 $got differs from the digest of 03 section 1.4 ($digest); nothing was copied" >&2
    exit 1
  fi
done

mkdir -p "$dest"
for entry in "${EXPECTED[@]}"; do
  file=${entry#*  }
  cp "$work/package/$file" "$dest/$(basename "$file")"
done

{
  echo "maplibre-gl $VERSION"
  echo "source: npm pack maplibre-gl@$VERSION (tools/vendor-maplibre.sh)"
  echo "tarball: $TARBALL_INTEGRITY"
} >"$dest/VERSION.txt"

(
  cd "$dest"
  for entry in "${EXPECTED[@]}"; do basename "${entry#*  }"; done | xargs sha256sum >SHA256SUMS
  sha256sum -c SHA256SUMS
)
