#!/usr/bin/env bash
# Publish ci-out/ to the orphan branch "ci-artifacts" (03 section 7.4, 04 card S0a).
#
#   ci-artifacts
#     README.md                      static
#     LATEST.md, LATEST.json         the newest run of ANY branch
#     runs/<branch-slug>/<run>-<sha7>/...   everything in ci-out/ (SUMMARY.md, errors.log, ...)
#
# Input: $CI_OUT (default ci-out) holding the SUMMARY.md written by make-summary.mjs. The branch,
# sha, run number and result are read from its header ("- branch: ..." and so on).
# Every publish is ONE new root (orphan) commit force-pushed with --force-with-lease against the
# tip it just fetched, retried 3 times, so two runs finishing together cannot lose each other.
# The runs/ folders of other branches are kept; the folder of this branch is replaced.
#
# Environment:
#   CI_OUT              directory with SUMMARY.md (default ci-out)
#   CI_REMOTE           remote name or URL to publish to (default origin)
#   GH_TOKEN            when set and the remote is https://github.com/<owner>/<repo>, the script pushes
#                       through https://x-access-token:$GH_TOKEN@github.com/<owner>/<repo>.git itself
#   PUBLISH_BACKOFF_S   seconds to sleep between attempts (default 2)
#
# Exit: 0 published, 1 push failed after 3 attempts, 64 usage or input error.
set -euo pipefail
set +x # never trace: the token is in the push URL
unset GIT_TRACE GIT_TRACE_CURL GIT_TRACE_PACKET GIT_CURL_VERBOSE
export GIT_TERMINAL_PROMPT=0 # a refused login must fail, never wait for input

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source-path=SCRIPTDIR
# shellcheck source=ci-common.sh
source "$script_dir/ci-common.sh"

ci_out=${CI_OUT:-ci-out}
remote=${CI_REMOTE:-origin}
backoff_s=${PUBLISH_BACKOFF_S:-2}
publish_branch=ci-artifacts
max_attempts=3
git_name="github-actions[bot]"
git_email="41898282+github-actions[bot]@users.noreply.github.com"

die() {
  local code=$1
  shift
  echo "publish-ci-artifacts: $*" >&2
  exit "$code"
}

# scrub <text>: print text with the token masked (the token must never reach a log).
scrub() {
  local text=$1
  if [[ -n ${GH_TOKEN:-} ]]; then
    text=${text//"$GH_TOKEN"/***}
  fi
  printf '%s\n' "$text"
}

# authenticated_url <url>: the URL to talk to. Plain https://github.com/<owner>/<repo>[.git] URLs get the
# x-access-token form when GH_TOKEN is set; everything else (file://, ssh, other hosts, no token) is unchanged.
authenticated_url() {
  local url=${1%/}
  if [[ -n ${GH_TOKEN:-} && $url =~ ^https://([^@/]+@)?github\.com/([^/]+)/([^/]+)$ ]]; then
    local repo=${BASH_REMATCH[3]%.git}
    printf 'https://x-access-token:%s@github.com/%s/%s.git' "$GH_TOKEN" "${BASH_REMATCH[2]}" "$repo"
  else
    printf '%s' "$1"
  fi
}

json_escape() {
  local s=$1
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  printf '%s' "$s"
}

# --- input -------------------------------------------------------------------------------------------
summary="$ci_out/SUMMARY.md"
[[ -f $summary ]] || die 64 "$summary not found (run tools/ci/make-summary.mjs first, or set CI_OUT)"

result=$(summary_field "$summary" result)
branch=$(summary_field "$summary" branch)
sha=$(summary_field "$summary" sha)
run=$(summary_field "$summary" run)
run_url=$(summary_field "$summary" url)

[[ $result == success || $result == failure ]] || die 64 "SUMMARY.md header: '- result:' must be success or failure, got '$result'"
[[ -n $branch && $branch != unknown ]] || die 64 "SUMMARY.md header: '- branch:' is missing or unknown"
[[ $sha =~ ^[0-9a-f]{7,40}$ ]] || die 64 "SUMMARY.md header: '- sha:' must be 7 to 40 lower-case hex digits, got '$sha'"
[[ $run =~ ^[0-9]+$ ]] || die 64 "SUMMARY.md header: '- run:' must be a number, got '$run'"

slug=$(branch_slug "$branch")
sha7=${sha:0:7}
run_dir="runs/$slug/$run-$sha7"

if [[ $remote == */* || $remote == *:* ]]; then
  remote_url=$remote
else
  remote_url=$(git remote get-url "$remote" 2>/dev/null) || die 64 "remote '$remote' not found in $(pwd)"
fi
url=$(authenticated_url "$remote_url")

# --- one attempt: fetch the tip, build a new root commit on top of its tree, push with a lease ----------
# attempt_in runs inside "while ! attempt_publish", where "set -e" is switched off by the shell: every step
# that changes state therefore ends in "|| return 1" so a half-built tree is never pushed.
attempt_in() {
  local work=$1 expected="" out rc=0
  git init -q "$work" || return 1

  out=$(git ls-remote --exit-code --heads "$url" "refs/heads/$publish_branch" 2>&1) || rc=$?
  if ((rc == 0)); then
    if ! out=$(git -C "$work" fetch --quiet --no-tags --depth=1 "$url" "+refs/heads/$publish_branch:refs/remotes/pub/$publish_branch" 2>&1); then
      scrub "$out" >&2
      return 1
    fi
    expected=$(git -C "$work" rev-parse "refs/remotes/pub/$publish_branch") || return 1
    git -C "$work" checkout -q --detach "$expected" || return 1
  elif ((rc != 2)); then
    scrub "$out" >&2
    return 1
  fi
  # HEAD becomes an unborn branch while index and files stay: the next commit has no parent.
  git -C "$work" symbolic-ref HEAD refs/heads/publish || return 1

  rm -rf "${work:?}/runs/$slug" || return 1
  mkdir -p "$work/$run_dir" || return 1
  cp -R "$ci_out/." "$work/$run_dir/" || return 1
  if [[ ! -e $work/$run_dir/errors.log ]]; then
    : >"$work/$run_dir/errors.log" || return 1
  fi
  cp "$summary" "$work/LATEST.md" || return 1
  cat >"$work/LATEST.json" <<JSON || return 1
{
  "run": $run,
  "sha": "$sha",
  "branch": "$(json_escape "$branch")",
  "slug": "$slug",
  "result": "$result",
  "url": "$(json_escape "$run_url")"
}
JSON
  cat >"$work/README.md" <<'README' || return 1
# ci-artifacts

Machine-written branch: one orphan commit, rewritten by the `publish-ci` job of `.github/workflows/ci.yml`
after every run, pass or fail. Nothing here is edited by hand.

- `LATEST.md`, `LATEST.json`: the newest run of any branch.
- `runs/<branch-slug>/<run>-<sha7>/SUMMARY.md`: read this first. The same folder holds `errors.log`
  (compiler errors, deduplicated), `build.tail.log` and `tests/*.trx`. The slug is the branch name in lower
  case with every character outside `[a-z0-9._-]` replaced by `-`.
- One folder per branch slug: the newest run replaces the previous one.

How to read it: `tools/ci/README.md` on the source branches (`tools/ci/wait-for-ci.sh <sha>` does the waiting).
README

  git -C "$work" add -A || return 1
  git -C "$work" -c user.name="$git_name" -c user.email="$git_email" -c commit.gpgsign=false \
    commit -q -m "ci: $slug run $run $sha7 $result" || return 1

  if ! out=$(git -C "$work" push --quiet --force-with-lease="refs/heads/$publish_branch:$expected" \
    "$url" "HEAD:refs/heads/$publish_branch" 2>&1); then
    scrub "$out" >&2
    return 1
  fi
}

attempt_publish() {
  local work rc=0
  work=$(mktemp -d) || return 1
  attempt_in "$work" || rc=$?
  rm -rf "$work"
  return "$rc"
}

attempt=1
while ! attempt_publish; do
  if ((attempt >= max_attempts)); then
    die 1 "push to $publish_branch failed after $max_attempts attempts"
  fi
  attempt=$((attempt + 1))
  echo "publish-ci-artifacts: attempt failed (the branch moved or the push was refused); retrying, attempt $attempt of $max_attempts" >&2
  sleep "$backoff_s"
done

echo "publish-ci-artifacts: published $run_dir ($result) to $publish_branch (attempt $attempt)"
