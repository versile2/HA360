#!/usr/bin/env bash
# Wait for the CI run of one pushed commit and print its SUMMARY.md (03 section 7.4, 04 section 1.6).
#
#   tools/ci/wait-for-ci.sh <sha> [branch]
#
# <sha>     the pushed commit: full, or at least 7 hex digits.
# [branch]  the branch it was pushed to. Without it: `git rev-parse --abbrev-ref HEAD` in the current
#           directory (never `git branch --contains`, which also answers "main", R3-018).
#
# Every 30 s: `git fetch origin ci-artifacts --depth=1`, then
#   match (1)  LATEST.json "sha" starts with <sha>, or
#   match (2)  the directory runs/<branch-slug>/<run>-<sha7>/ exists (safe with parallel branches).
# On a match: print that run's SUMMARY.md, then a line "RESULT=success" or "RESULT=failure" (from the
# SUMMARY header) and a line "RUN_URL=<url>" when the header has one. Progress goes to stderr.
#
# Exit codes: 0 success, 1 failure, 2 timeout, 3 superseded (a newer run of the same branch was published
# and ours will never appear: publish-ci skips cancelled runs), 64 usage error.
# Exit 3 rule (R3-018): the highest run number under runs/<slug>/ at the first poll is the baseline; when no
# match is found and a run above the baseline with another sha7 appears, the branch tip moved on.
#
# Environment: WAIT_TIMEOUT_S (default 1500 = 25 min), WAIT_POLL_S (default 30; tests use 1).
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source-path=SCRIPTDIR
# shellcheck source=ci-common.sh
source "$script_dir/ci-common.sh"

usage() {
  cat <<'USAGE'
usage: wait-for-ci.sh <sha> [branch]
  <sha>     pushed commit, full or at least 7 hex digits
  [branch]  branch it was pushed to (default: git rev-parse --abbrev-ref HEAD)
  env: WAIT_TIMEOUT_S (default 1500), WAIT_POLL_S (default 30)
  exit: 0 success, 1 failure, 2 timeout, 3 superseded, 64 usage error
USAGE
}

die() {
  local code=$1
  shift
  echo "wait-for-ci: $*" >&2
  exit "$code"
}

log() { echo "wait-for-ci: $*" >&2; }

if [[ ${1:-} == -h || ${1:-} == --help ]]; then
  usage
  exit 0
fi
if (($# < 1 || $# > 2)); then
  usage >&2
  exit 64
fi

sha=${1,,}
[[ $sha =~ ^[0-9a-f]{7,40}$ ]] || die 64 "'$1' is not a commit SHA (7 to 40 hex digits)"
sha7=${sha:0:7}

git rev-parse --git-dir >/dev/null 2>&1 || die 64 "not inside a git repository (run it in the worktree that pushed)"
git remote get-url origin >/dev/null 2>&1 || die 64 "this repository has no remote named 'origin'"

branch=${2:-}
if [[ -z $branch ]]; then
  branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null) || branch=""
  if [[ -z $branch || $branch == HEAD ]]; then
    die 64 "cannot tell the branch (detached HEAD or no commits): pass it as the second argument"
  fi
fi
slug=$(branch_slug "$branch")

timeout_s=${WAIT_TIMEOUT_S:-1500}
poll_s=${WAIT_POLL_S:-30}
[[ $timeout_s =~ ^[0-9]+$ ]] || die 64 "WAIT_TIMEOUT_S must be a whole number of seconds, got '$timeout_s'"
[[ $poll_s =~ ^[0-9]+$ && $poll_s -gt 0 ]] || die 64 "WAIT_POLL_S must be a positive whole number of seconds, got '$poll_s'"

ref=refs/remotes/origin/ci-artifacts
baseline=""
deadline=$((SECONDS + timeout_s))
log "waiting for the run of $sha7 on '$branch' (slug '$slug'), timeout ${timeout_s}s"

# print_verdict <summary-text>: the SUMMARY, the RESULT line, the run URL; exits with the verdict.
print_verdict() {
  local text=$1 tmp result run_url
  tmp=$(mktemp)
  printf '%s\n' "$text" >"$tmp"
  result=$(summary_field "$tmp" result)
  run_url=$(summary_field "$tmp" url)
  rm -f "$tmp"
  printf '%s\n' "$text"
  if [[ $result == success ]]; then
    echo "RESULT=success"
    [[ -z $run_url ]] || echo "RUN_URL=$run_url"
    exit 0
  fi
  [[ $result == failure ]] || log "the SUMMARY header has no '- result:' line (got '$result'); reporting failure"
  echo "RESULT=failure"
  [[ -z $run_url ]] || echo "RUN_URL=$run_url"
  exit 1
}

while :; do
  fetch_err=""
  if fetch_err=$(git fetch --quiet --depth=1 origin "+refs/heads/ci-artifacts:$ref" 2>&1); then
    latest_sha=$(git show "$ref:LATEST.json" 2>/dev/null | awk -F'"' '$2 == "sha" { print $4; exit }' || true)

    match_dir=""
    match_run=-1
    max_run=0
    newer_dir=""
    while IFS= read -r entry; do
      name=${entry##*/}
      [[ $name =~ ^([0-9]+)-([0-9a-f]{7})$ ]] || continue
      run=$((10#${BASH_REMATCH[1]}))
      if ((run > max_run)); then max_run=$run; fi
      if [[ ${BASH_REMATCH[2]} == "$sha7" ]]; then
        if ((run > match_run)); then
          match_run=$run
          match_dir=$name
        fi
      elif [[ -n $baseline ]] && ((run > baseline)); then
        newer_dir=$name
      fi
    done < <(git ls-tree --name-only "$ref" "runs/$slug/" 2>/dev/null || true)

    # The match comes first on every poll.
    if [[ -n $match_dir ]]; then
      log "found runs/$slug/$match_dir"
      summary_text=$(git show "$ref:runs/$slug/$match_dir/SUMMARY.md" 2>&1) || die 1 "runs/$slug/$match_dir has no readable SUMMARY.md: $summary_text"
      print_verdict "$summary_text"
    fi
    if [[ -n $latest_sha && $latest_sha == "$sha"* ]]; then
      log "found LATEST.json for $sha7"
      summary_text=$(git show "$ref:LATEST.md" 2>&1) || die 1 "LATEST.md is not readable: $summary_text"
      print_verdict "$summary_text"
    fi
    if [[ -n $newer_dir ]]; then
      die 3 "superseded: runs/$slug/$newer_dir is newer than the run that was current when waiting started (run $baseline), and no run for $sha7 exists. Wait for the newest commit of '$branch' instead"
    fi
    if [[ -z $baseline ]]; then
      baseline=$max_run
      log "baseline run number under runs/$slug/: $baseline"
    fi
  else
    # No ci-artifacts branch yet is the normal state before the very first run: it counts as baseline 0.
    # Any other fetch error (network, auth) is retried and does not set the baseline.
    ls_rc=0
    git ls-remote --exit-code --heads origin refs/heads/ci-artifacts >/dev/null 2>&1 || ls_rc=$?
    if ((ls_rc == 2)); then
      [[ -n $baseline ]] || baseline=0
      log "branch ci-artifacts does not exist on origin yet"
    else
      log "fetch failed, will retry: ${fetch_err%%$'\n'*}"
    fi
  fi

  remaining=$((deadline - SECONDS))
  if ((remaining <= 0)); then
    die 2 "timed out after ${timeout_s}s: no run for $sha7 on '$branch' was published"
  fi
  log "no run for $sha7 yet (${remaining}s left)"
  if ((poll_s < remaining)); then sleep "$poll_s"; else sleep "$remaining"; fi
done
