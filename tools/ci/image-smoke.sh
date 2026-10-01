#!/usr/bin/env bash
# image-smoke.sh <image>: run the image the way the Supervisor does and check it (03 section 7.8, 04 card S2).
#
#   bash tools/ci/image-smoke.sh realm:smoke 2>&1 | tee ci-out/smoke.log
#
# Prints one PASS, WARN, FAIL or SKIP line per item and writes $CI_OUT/smoke.json (sizes, timings, one entry per item;
# a flat { "name": number|string } map, read by make-summary.mjs for the "## Docker smoke" section).
# Exit 0 when no item failed, 1 when one did, 64 on a usage error.
#
# Items (numbers are 03 section 7.8's; this file is append-only, one numbered block per slice, 04 G-17):
#   1  first /healthz 200 after the container starts (target at most 3 s = WARN above, FAIL above 10 s)
#   2  base href with and without X-Ingress-Path, an invalid value is ignored
#   3  Content-Type of the scripts and fonts
#   4  no Set-Cookie on GET /
#   9  the image runs as uid 0 (asserted, not assumed)
#   10 no forbidden path in docker export
# Items 5, 7 and 8 belong to S16a and item 6 to S10a: each adds its own block here and touches no other item.
#
# Environment: CI_OUT (default ci-out), SMOKE_PORT (host port, default 18099), SMOKE_HEALTHY_CAP_S (how long to wait for
# the first 200 before giving up, default 30; the FAIL line stays at 10 s, the cap only lets a slow start still be measured).
# No `set -e` on purpose: every item runs and reports; the exit status comes from the items.
set -uo pipefail

if [[ $# -ne 1 || -z $1 ]]; then
  echo "usage: image-smoke.sh <image>" >&2
  exit 64
fi
image=$1
out_dir=${CI_OUT:-ci-out}
port=${SMOKE_PORT:-18099}
cap_ms=$((${SMOKE_HEALTHY_CAP_S:-30} * 1000))
warn_ms=3000
fail_ms=10000
base_url="http://127.0.0.1:${port}"
container="realm-smoke-$$"
work=$(mktemp -d)
# A fixed 43-character token, the length of a real Ingress token (research ha-addon section 2).
ingress_token="AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE"

declare -A item_status item_title item_detail
json_fields=()
log_tail=""
failures=0

now_ms() {
  local ns
  ns=$(date +%s%N)
  echo $((ns / 1000000))
}

# fmt_s <ms>: 1842 -> 1.842
fmt_s() {
  printf '%d.%03d' $(($1 / 1000)) $(($1 % 1000))
}

# record <n> <PASS|WARN|FAIL|SKIP> <title> <detail>
record() {
  item_status[$1]=$2
  item_title[$1]=$3
  item_detail[$1]=$4
  if [[ $2 == FAIL ]]; then
    failures=$((failures + 1))
  fi
  printf '%s item%s %s: %s\n' "$2" "$1" "$3" "$4"
}

# join_problems: the entries of the global array "problems", separated by "; "
join_problems() {
  local out="" p
  for p in "${problems[@]}"; do
    out+="${out:+; }$p"
  done
  printf '%s' "$out"
}

json_string() {
  local s
  s=$(printf '%s' "$1" | tr -d '\000-\010\013\014\016-\037')
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\n'/\\n}
  s=${s//$'\r'/\\r}
  s=${s//$'\t'/\\t}
  printf '"%s"' "$s"
}

add_number() { json_fields+=("\"$1\": $2"); }
add_string() { json_fields+=("\"$1\": $(json_string "$2")"); }

# shellcheck disable=SC2317 # write_json and on_exit run from the EXIT trap, which shellcheck 0.9 does not follow
write_json() {
  mkdir -p "$out_dir" 2>/dev/null || return 0
  local fields=("${json_fields[@]}") n f sep=""
  for n in $(printf '%s\n' "${!item_status[@]}" | sort -n); do
    fields+=("\"item$n\": $(json_string "${item_status[$n]}")")
    fields+=("\"item${n}_title\": $(json_string "${item_title[$n]}")")
    fields+=("\"item${n}_detail\": $(json_string "${item_detail[$n]}")")
  done
  if [[ -n $log_tail ]]; then
    fields+=("\"container_log_tail\": $(json_string "$log_tail")")
  fi
  {
    printf '{\n'
    for f in "${fields[@]}"; do
      printf '%s  %s' "$sep" "$f"
      sep=$',\n'
    done
    printf '\n}\n'
  } >"$out_dir/smoke.json"
}

# shellcheck disable=SC2317
on_exit() {
  local status=$?
  docker rm -f "$container" >/dev/null 2>&1
  rm -rf "$work"
  write_json
  exit "$status"
}
trap on_exit EXIT

# get_headers <url path without the leading slash> [header...]: sets h_status and h_ctype (lower-case, no parameters).
get_headers() {
  local path=$1 args=() header
  shift
  for header in "$@"; do
    args+=(-H "$header")
  done
  rm -f "$work/headers.txt"
  h_status=$(curl -s -o /dev/null -D "$work/headers.txt" -w '%{http_code}' --max-time 20 "${args[@]}" "$base_url/$path")
  h_ctype=""
  if [[ -f $work/headers.txt ]]; then
    h_ctype=$(tr -d '\r' <"$work/headers.txt" | awk 'tolower($0) ~ /^content-type:/ { v = tolower($0); sub(/^[^:]*:[ \t]*/, "", v); sub(/[ \t]*;.*$/, "", v); sub(/[ \t]+$/, "", v); print v; exit }')
  fi
}

# check_type <url path> <expected type>: adds the path to "checked" and a problem to "problems" when its status or type is wrong.
check_type() {
  get_headers "$1"
  checked+=("$1 ($2)")
  if [[ $h_status != 200 || $h_ctype != "$2" ]]; then
    problems+=("$1: status $h_status, type '${h_ctype:-none}' (want 200 and $2)")
  fi
}

# probe_root [header...]: GET / and set root_status and root_base (the href of <base>).
probe_root() {
  local args=() header
  for header in "$@"; do
    args+=(-H "$header")
  done
  rm -f "$work/root.html"
  root_status=$(curl -s -o "$work/root.html" -w '%{http_code}' --max-time 20 "${args[@]}" "$base_url/")
  root_base=$(grep -o '<base href="[^"]*"' "$work/root.html" 2>/dev/null | head -n 1 | sed 's/^<base href="//; s/"$//')
}

# ---------------------------------------------------------------------------------------------------------------------
# Start: the image the way the Supervisor runs it (Docker's own init, as config.yaml says init: true), Demo data, no HA.
# ---------------------------------------------------------------------------------------------------------------------
if ! docker image inspect "$image" >/dev/null 2>&1; then
  echo "FAIL image: '$image' does not exist locally (did the build step run with load: true?)"
  add_string error "image '$image' does not exist locally (did the build step run with load: true?)"
  exit 1
fi
add_string image "$image"
size_bytes=$(docker image inspect --format '{{.Size}}' "$image" 2>/dev/null)
if [[ $size_bytes =~ ^[0-9]+$ ]]; then
  add_number image_size_bytes "$size_bytes"
fi

# --- Item 1: first /healthz 200 -----------------------------------------------------------------------------------
title="first /healthz 200"
healthy=0
start_ms=$(now_ms)
if ! docker run -d --init --name "$container" -e REALM_DATA_SOURCE=demo -p "127.0.0.1:${port}:8099" "$image" >/dev/null 2>"$work/run.err"; then
  record 1 FAIL "$title" "docker run failed: $(head -c 300 "$work/run.err")"
else
  healthy_ms=-1
  state=running
  code=000
  while :; do
    code=$(curl -s -o /dev/null -w '%{http_code}' --connect-timeout 1 --max-time 2 "$base_url/healthz")
    elapsed=$(($(now_ms) - start_ms))
    if [[ $code == 200 ]]; then
      healthy_ms=$elapsed
      break
    fi
    if ((elapsed > cap_ms)); then
      break
    fi
    state=$(docker inspect --format '{{.State.Status}}' "$container" 2>/dev/null || echo gone)
    if [[ $state != running ]]; then
      break
    fi
    sleep 0.1
  done
  if ((healthy_ms < 0)); then
    if [[ $state != running ]]; then
      exit_code=$(docker inspect --format '{{.State.ExitCode}}' "$container" 2>/dev/null || echo '?')
      record 1 FAIL "$title" "the container is $state (exit code $exit_code) and /healthz never answered 200"
    else
      record 1 FAIL "$title" "no 200 from /healthz within $((cap_ms / 1000)) s (last status $code)"
    fi
  else
    healthy=1
    add_number time_to_healthy_s "$(fmt_s "$healthy_ms")"
    if ((healthy_ms > fail_ms)); then
      record 1 FAIL "$title" "$(fmt_s "$healthy_ms") s is over the $((fail_ms / 1000)) s limit"
    elif ((healthy_ms > warn_ms)); then
      record 1 WARN "$title" "$(fmt_s "$healthy_ms") s is over the $((warn_ms / 1000)) s target (03 section 6.3: ReadyToRun is the first optimisation)"
    else
      record 1 PASS "$title" "$(fmt_s "$healthy_ms") s"
    fi
  fi
fi

if ((healthy == 0)); then
  not_run="not run: the container never answered /healthz with 200 (item 1)"
  record 2 SKIP "base href follows X-Ingress-Path" "$not_run"
  record 3 SKIP "content types" "$not_run"
  record 4 SKIP "no Set-Cookie on /" "$not_run"
else
  # --- Item 2: <base href> -----------------------------------------------------------------------------------------
  title="base href follows X-Ingress-Path"
  problems=()
  probe_root
  if [[ $root_status != 200 || $root_base != "/" ]]; then
    problems+=("no header: status $root_status, base '$root_base' (want 200 and '/')")
  fi
  probe_root "X-Ingress-Path: /api/hassio_ingress/$ingress_token"
  if [[ $root_status != 200 || $root_base != "/api/hassio_ingress/$ingress_token/" ]]; then
    problems+=("with the header: status $root_status, base '$root_base' (want 200 and '/api/hassio_ingress/TOKEN/')")
  fi
  probe_root "X-Ingress-Path: api/hassio_ingress/$ingress_token"
  if [[ $root_status != 200 || $root_base != "/" ]]; then
    problems+=("invalid header (no leading slash): status $root_status, base '$root_base' (want 200 and '/', the value is ignored)")
  fi
  if ((${#problems[@]} == 0)); then
    record 2 PASS "$title" "'/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored"
  else
    record 2 FAIL "$title" "$(join_problems)"
  fi

  # --- Item 3: content types ---------------------------------------------------------------------------------------
  # blazor.web.js always. The S5 files and a font are checked as soon as they are in the image (S5 onward), so S5 does not
  # have to edit this script; a file that is in the image but not served with the right type fails.
  title="content types"
  problems=()
  checked=()
  check_type _framework/blazor.web.js text/javascript
  for rel in lib/maplibre-gl/maplibre-gl.mjs lib/maplibre-gl/maplibre-gl-worker.mjs js/realmMap.js; do
    if docker exec "$container" test -f "/app/wwwroot/$rel" 2>/dev/null; then
      check_type "$rel" text/javascript
    fi
  done
  font=$(docker exec "$container" find /app/wwwroot -name '*.woff2' -print -quit 2>/dev/null)
  if [[ -n $font ]]; then
    check_type "${font#/app/wwwroot/}" font/woff2
  fi
  if ((${#problems[@]} == 0)); then
    record 3 PASS "$title" "${checked[*]}"
  else
    record 3 FAIL "$title" "$(join_problems)"
  fi

  # --- Item 4: no Set-Cookie on / ----------------------------------------------------------------------------------
  title="no Set-Cookie on /"
  get_headers ""
  cookie_names=$(tr -d '\r' <"$work/headers.txt" 2>/dev/null | grep -i '^set-cookie:' | cut -d= -f1 | head -n 3)
  if [[ $h_status != 200 ]]; then
    record 4 FAIL "$title" "GET / answered $h_status, so the headers prove nothing"
  elif [[ -n $cookie_names ]]; then
    record 4 FAIL "$title" "GET / sets a cookie: $(echo "$cookie_names" | tr '\n' ' ')"
  else
    record 4 PASS "$title" "GET / answered 200 without Set-Cookie"
  fi
fi

# Items 5 (healthcheck mode), 6 (diagnostics.json), 7 (docker logs) and 8 (image size) are added by S10a and S16a.

# --- Item 9: root --------------------------------------------------------------------------------------------------
# --entrypoint: the image's ENTRYPOINT is "dotnet Realm.Web.dll", so "docker run <image> id -u" would start the app with two arguments.
title="image runs as root"
uid=$(docker run --rm --entrypoint id "$image" -u 2>"$work/id.err")
if [[ $uid == 0 ]]; then
  record 9 PASS "$title" "id -u prints 0"
else
  id_err=$(head -c 200 "$work/id.err")
  record 9 FAIL "$title" "id -u printed '$uid'${id_err:+ ($id_err)}: the decision of 03 section 6.3 is root, so /data is writable"
fi

# --- Item 10: nothing but published output in the image ------------------------------------------------------------
title="no forbidden path in the image"
created=$(docker create "$image" 2>/dev/null)
if [[ -z $created ]] || ! docker export "$created" | tar -t >"$work/export.lst" 2>"$work/export.err"; then
  record 10 FAIL "$title" "docker create or docker export failed: $(head -c 300 "$work/export.err" 2>/dev/null)"
else
  # Whole path components (.git, .private, node_modules, ci-out) and file names (options.json, *.db, *.db-wal, *.db-shm).
  forbidden=$(awk -F/ '{
    n = NF; if ($n == "") n--
    bad = 0
    for (i = 1; i <= n; i++) if ($i == ".git" || $i == ".private" || $i == "node_modules" || $i == "ci-out") bad = 1
    if ($n == "options.json" || $n ~ /\.db(-wal|-shm)?$/) bad = 1
    if (bad) print $0
  }' "$work/export.lst" | head -n 10)
  entries=$(wc -l <"$work/export.lst" | tr -d ' ')
  if [[ -z $forbidden ]]; then
    record 10 PASS "$title" "$entries entries checked"
  else
    record 10 FAIL "$title" "found: $(echo "$forbidden" | tr '\n' ' ')"
  fi
fi
if [[ -n $created ]]; then
  docker rm "$created" >/dev/null 2>&1
fi

# ---------------------------------------------------------------------------------------------------------------------
# Sizes for the table of 03 section 6.5 (recorded, not enforced: item 8 is S16a's) and the log of a failed run.
# ---------------------------------------------------------------------------------------------------------------------
app_bytes=$(docker run --rm --entrypoint du "$image" -sb /app 2>/dev/null | awk '{ print $1 }')
if [[ $app_bytes =~ ^[0-9]+$ ]]; then
  add_number app_layer_bytes "$app_bytes"
fi
# The registry size is the sum of the compressed layers; gzip of docker save is close enough to read the trend (not enforced).
gz_bytes=$(docker save "$image" 2>/dev/null | gzip -1 | wc -c | tr -d ' ')
if [[ $gz_bytes =~ ^[0-9]+$ && $gz_bytes -gt 0 ]]; then
  add_number compressed_estimate_bytes "$gz_bytes"
fi

if ((failures > 0)) && docker inspect "$container" >/dev/null 2>&1; then
  log_tail=$(docker logs --tail 40 "$container" 2>&1)
  if ((${#log_tail} > 8000)); then
    log_tail=${log_tail: -8000}
  fi
  if [[ -n $log_tail ]]; then
    echo "--- container log, last 40 lines ---"
    echo "$log_tail"
    echo "--- end of container log ---"
  fi
fi

passed=0
warned=0
for n in "${!item_status[@]}"; do
  case ${item_status[$n]} in
    PASS) passed=$((passed + 1)) ;;
    WARN) warned=$((warned + 1)) ;;
    *) ;;
  esac
done
echo "smoke: ${#item_status[@]} items, $passed PASS, $warned WARN, $failures FAIL; image ${size_bytes:-unknown} bytes; $out_dir/smoke.json"
if ((failures > 0)); then
  exit 1
fi
exit 0
