# ci-artifacts

Machine-written branch: one orphan commit, rewritten by the `publish-ci` job of `.github/workflows/ci.yml`
after every run, pass or fail. Nothing here is edited by hand.

- `LATEST.md`, `LATEST.json`: the newest run of any branch.
- `runs/<branch-slug>/<run>-<sha7>/SUMMARY.md`: read this first. The same folder holds `errors.log`
  (compiler errors, deduplicated), `build.tail.log` and `tests/*.trx`. The slug is the branch name in lower
  case with every character outside `[a-z0-9._-]` replaced by `-`.
- One folder per branch slug: the newest run replaces the previous one.

How to read it: `tools/ci/README.md` on the source branches (`tools/ci/wait-for-ci.sh <sha>` does the waiting).
