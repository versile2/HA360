#!/usr/bin/env bash
# Shared helpers for tools/ci/*.sh. Source this file; do not run it.

# branch_slug <branch>: the directory name of a branch under runs/ on the ci-artifacts branch.
# Lower-case, every character outside [a-z0-9._-] becomes "-" ("slice/S5-map-1" -> "slice-s5-map-1").
# LC_ALL=C keeps the character classes ASCII-only whatever the caller's locale.
# publish-ci-artifacts.sh and wait-for-ci.sh both call this one function (04 PR-2).
branch_slug() {
  local LC_ALL=C
  local s=${1,,}
  printf '%s' "${s//[^a-z0-9._-]/-}"
}

# summary_field <summary-file> <key>: value of the first "- <key>: <value>" header line of a SUMMARY.md.
# make-summary.mjs writes those lines; publish-ci-artifacts.sh and wait-for-ci.sh read them.
summary_field() {
  awk -v key="$2" '
    index($0, "- " key ": ") == 1 { print substr($0, length(key) + 5); exit }
  ' "$1"
}
