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
#   3  Content-Type of the scripts and fonts (the Blazor script is requested under the name the page itself advertises)
#   4  Set-Cookie on GET /: informational (D61), WARN with the cookie names, never FAIL
#   9  the image runs as uid 0 (asserted, not assumed)
#   10 no forbidden path in docker export
#   11 the Live start with Home Assistant unreachable (S13b, CR2-012): SUPERVISOR_TOKEN set, /data a volume with a valid options.json;
#      /healthz 200 within 10 s, /data/realm.db and /data/dp-keys created, still running after a settle time, no crash in docker logs
#   12 the licence notices ship in the image (FX2, licence audit 1 action 3): /app/LICENSE, /app/THIRD-PARTY-NOTICES.md,
#      /app/LICENSES/Apache-2.0.txt and MapLibre's LICENSE.txt in /app/wwwroot/lib/maplibre-gl, plus an OFL-*.txt beside any font file
# Items 5, 7 and 8 belong to S16a and item 6 to S10a: each adds its own block here and touches no other item.
#
# Environment: CI_OUT (default ci-out), SMOKE_PORT (host port, default 18099), SMOKE_HEALTHY_CAP_S (how long to wait for
# the first 200 before giving up, default 30; the FAIL line stays at 10 s, the cap only lets a slow start still be measured).
# Item 11 runs a second container: SMOKE_LIVE_PORT (host port, default SMOKE_PORT + 1) and SMOKE_LIVE_SETTLE_S (seconds the
# container must stay up after its first 200, so that the first attempts to reach Home Assistant have failed and been logged; default 3).
# When an item fails on a request, the response headers of that request (and, for the Blazor script, what the image holds for
# static web assets) are printed under the item's line and kept in smoke.json as failing_requests (make-summary.mjs shows them).
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
live_container="${container}-live"   # item 11
work=$(mktemp -d)
live_dir="$work/live-data"           # item 11: what the Live container sees as /data
# A fixed 43-character token, the length of a real Ingress token (research ha-addon section 2).
ingress_token="AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE"

declare -A item_status item_title item_detail
json_fields=()
log_tail=""
pending_log=""   # evidence for the item being checked: printed under its line by record(), then moved to request_log
request_log=""   # all of it, for smoke.json
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
  if [[ -n $pending_log ]]; then
    printf '%s' "$pending_log"
    request_log+="item $1 $3"$'\n'"$pending_log"
    pending_log=""
  fi
}

# queue_block <heading> <body>: evidence for the item being checked, printed (indented) under its line once record() runs.
queue_block() {
  local body
  body=$(printf '%s\n' "$2" | head -c 1500 | sed 's/^/    /')
  pending_log+="  $1"$'\n'"$body"$'\n'
}

# queue_request <label> <headers file>: the response headers of a request that failed the item.
queue_request() {
  local headers=""
  if [[ -s $2 ]]; then
    headers=$(tr -d '\r' <"$2")
  fi
  queue_block "response headers of $1" "${headers:-(none: curl got no response)}"
}

# asset_diag: what the running container holds for static web assets, to tell a manifest without the Blazor script from a missing
# manifest or a wrong content root. Read-only, runs inside the container (its shell is fed through stdin).
asset_diag() {
  docker exec -i "$container" sh -s 2>&1 <<'EOF'
cd /app 2>/dev/null || { echo "no /app in the image"; exit 0; }
echo "content root: $(pwd); ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-unset}; ASPNETCORE_CONTENTROOT=${ASPNETCORE_CONTENTROOT:-unset}"
found=0
for m in ./*.staticwebassets.endpoints.json; do
  [ -f "$m" ] || continue
  found=1
  echo "$m: $(wc -c <"$m" | tr -d ' ') bytes, $(grep -o '"Route": *"' "$m" | wc -l | tr -d ' ') routes"
  grep -o '"Route": *"_framework/[^"]*"' "$m" | sort -u | head -n 8
  echo "routes of _framework/blazor*: $(grep -o '"Route": *"_framework/blazor[^"]*"' "$m" | sort -u | wc -l | tr -d ' ')"
done
[ "$found" = 1 ] || echo "no *.staticwebassets.endpoints.json next to the app: MapStaticAssets has no manifest"
echo "wwwroot/_framework: $(ls wwwroot/_framework 2>&1 | head -n 8 | tr '\n' ' ')"
EOF
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
  if [[ -n $request_log ]]; then
    local requests=$request_log
    if ((${#requests} > 6000)); then
      requests=${requests:0:6000}
    fi
    fields+=("\"failing_requests\": $(json_string "$requests")")
  fi
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

# cleanup_live: removes the Live container of item 11 and what it wrote into its bind mount. The container runs as root (item 9), so the
# directories it created in $live_dir belong to root and the runner's own rm cannot empty them; a throwaway container of the same image can.
cleanup_live() {
  docker rm -f "$live_container" >/dev/null 2>&1
  if [[ -d $live_dir ]]; then
    docker run --rm -v "$live_dir:/d" --entrypoint find "$image" /d -mindepth 1 -delete >/dev/null 2>&1
    rm -rf "$live_dir" 2>/dev/null
  fi
}

# shellcheck disable=SC2317
on_exit() {
  local status=$?
  docker rm -f "$container" >/dev/null 2>&1
  cleanup_live
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
    queue_request "GET /$1 -> $h_status" "$work/headers.txt"
  fi
}

# probe_root [header...]: GET / and set root_status and root_base (the href of <base>); the headers are kept in $work/root.headers.
probe_root() {
  local args=() header
  for header in "$@"; do
    args+=(-H "$header")
  done
  rm -f "$work/root.html" "$work/root.headers"
  root_status=$(curl -s -o "$work/root.html" -D "$work/root.headers" -w '%{http_code}' --max-time 20 "${args[@]}" "$base_url/")
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
  record 4 SKIP "Set-Cookie on / (informational)" "$not_run"
else
  # --- Item 2: <base href> -----------------------------------------------------------------------------------------
  title="base href follows X-Ingress-Path"
  problems=()
  probe_root
  if [[ $root_status != 200 || $root_base != "/" ]]; then
    problems+=("no header: status $root_status, base '$root_base' (want 200 and '/')")
    queue_request "GET / without the header -> $root_status" "$work/root.headers"
  fi
  probe_root "X-Ingress-Path: /api/hassio_ingress/$ingress_token"
  if [[ $root_status != 200 || $root_base != "/api/hassio_ingress/$ingress_token/" ]]; then
    problems+=("with the header: status $root_status, base '$root_base' (want 200 and '/api/hassio_ingress/TOKEN/')")
    queue_request "GET / with the header -> $root_status" "$work/root.headers"
  fi
  probe_root "X-Ingress-Path: api/hassio_ingress/$ingress_token"
  if [[ $root_status != 200 || $root_base != "/" ]]; then
    problems+=("invalid header (no leading slash): status $root_status, base '$root_base' (want 200 and '/', the value is ignored)")
    queue_request "GET / with the invalid header -> $root_status" "$work/root.headers"
  fi
  if ((${#problems[@]} == 0)); then
    record 2 PASS "$title" "'/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored"
  else
    record 2 FAIL "$title" "$(join_problems)"
  fi

  # --- Item 3: content types ---------------------------------------------------------------------------------------
  # blazor.web.js always, under the name the page advertises: .NET 10 serves it as a static web asset with a fingerprint in the
  # name (_framework/blazor.web.<hash>.js) and a browser requests exactly the name the page carries. A page that still carries the
  # plain _framework/blazor.web.js has no mapping for it, which is what a missing framework asset looks like (the 404 of S2 fix 1).
  # The S5 files and a font are checked as soon as they are in the image (S5 onward), so S5 does not have to edit this script; a
  # file that is in the image but not served with the right type fails.
  title="content types"
  problems=()
  checked=()
  probe_root
  script_path=$(grep -o 'src="[^"]*blazor\.web[^"]*\.js"' "$work/root.html" 2>/dev/null | head -n 1 | sed 's/^src="//; s/"$//')
  script_path=${script_path#/}
  if [[ -z $script_path || $script_path == *://* ]]; then
    problems+=("GET / (status $root_status) has no relative <script src> for blazor.web.js")
    queue_request "GET / -> $root_status" "$work/root.headers"
    script_path=_framework/blazor.web.js
  fi
  check_type "$script_path" text/javascript
  if ((${#problems[@]} > 0)); then
    queue_block "the image's static web assets (what MapStaticAssets reads)" "$(asset_diag)"
  fi
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

  # --- Item 4: Set-Cookie on / (informational) ---------------------------------------------------------------------
  # D61: the antiforgery cookie of Blazor is expected and harmless behind Ingress, so this item reports and never fails. Only the
  # cookie names are logged, never a value.
  title="Set-Cookie on / (informational)"
  get_headers ""
  cookie_names=$(tr -d '\r' <"$work/headers.txt" 2>/dev/null | awk 'tolower($0) ~ /^set-cookie:/ { v = $0; sub(/^[^:]*:[ \t]*/, "", v); sub(/[=;].*$/, "", v); print v }' | sort -u | head -n 3 | tr '\n' ' ')
  cookie_names=${cookie_names% }
  if [[ $h_status != 200 ]]; then
    record 4 WARN "$title" "GET / answered $h_status, so the headers say nothing about cookies"
  elif [[ -n $cookie_names ]]; then
    record 4 WARN "$title" "GET / sets a cookie: $cookie_names (D61: informational; the Blazor antiforgery cookie is expected)"
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

# --- Item 11: the Live start with Home Assistant unreachable (CR2-012) ----------------------------------------------
# The way the add-on really starts: SUPERVISOR_TOKEN set, /data a volume that holds options.json (02 section 3.5, demo_mode false, one
# member and one vehicle with fictional ids) and no Home Assistant to talk to (the Supervisor's own "supervisor" host does not resolve
# outside it). It is the longest start the app has (the SQLite schema, the Data Protection key ring, the three services that dial Home
# Assistant) and none of it runs in the Demo start of item 1. The schema bootstrap runs before the listener opens (03 section 2.4), so the
# 10 s limit on the first /healthz also bounds it, on a new database. Not a failure: "fail:" lines (the connection loops log an
# unreachable Home Assistant at Error and retry); a failure: a crash line, a container that stops, a missing file.
title="Live start, Home Assistant unreachable"
live_port=${SMOKE_LIVE_PORT:-$((port + 1))}
live_url="http://127.0.0.1:${live_port}"
live_settle_s=${SMOKE_LIVE_SETTLE_S:-3}
mkdir -p "$live_dir"
cat >"$live_dir/options.json" <<'EOF'
{
  "log_level": "information",
  "ui_stale_after_minutes": 30,
  "ui_offline_after_hours": 24,
  "ui_vehicle_stale_after_minutes": 45,
  "ui_low_battery_percent": 15,
  "ui_poor_accuracy_meters": 500,
  "ui_default_view_radius_km": 40,
  "ui_max_zone_radius_km": 5,
  "ui_far_away_km": 80,
  "ui_history_tokens": true,
  "features_temp_bubble": false,
  "features_add_rows": false,
  "fusion_stale_grace_minutes": 10,
  "trips_start_speed_mph": 15,
  "trips_stop_merge_seconds": 180,
  "trips_min_distance_miles": 0.3,
  "trips_min_duration_seconds": 120,
  "driving_week_start": "monday",
  "driving_speeding_mph": 80,
  "driving_speeding_min_seconds": 30,
  "driving_phone_min_seconds": 10,
  "retention_fix_days": 120,
  "backfill_days": 10,
  "privacy_log_positions": false,
  "demo_mode": false,
  "allow_demo_param": false,
  "ignore_entities": [],
  "members": [
    { "id": "king", "display_name": "Alden", "person": "person.alden", "life360_tracker": "device_tracker.alden_life360" }
  ],
  "vehicles": [
    { "id": "truck", "name": "The Truck", "glyph": "pickup", "integration": "none" }
  ],
  "places": []
}
EOF
problems=()
live_start_ms=$(now_ms)
if ! docker run -d --init --name "$live_container" -e SUPERVISOR_TOKEN=x -v "$live_dir:/data" -p "127.0.0.1:${live_port}:8099" "$image" >/dev/null 2>"$work/run-live.err"; then
  record 11 FAIL "$title" "docker run failed: $(head -c 300 "$work/run-live.err")"
else
  live_healthy_ms=-1
  live_state=running
  live_code=000
  while :; do
    live_code=$(curl -s -o /dev/null -w '%{http_code}' --connect-timeout 1 --max-time 2 "$live_url/healthz")
    elapsed=$(($(now_ms) - live_start_ms))
    if [[ $live_code == 200 ]]; then
      live_healthy_ms=$elapsed
      break
    fi
    if ((elapsed > fail_ms)); then
      break
    fi
    live_state=$(docker inspect --format '{{.State.Status}}' "$live_container" 2>/dev/null || echo gone)
    if [[ $live_state != running ]]; then
      break
    fi
    sleep 0.1
  done
  if ((live_healthy_ms < 0)); then
    if [[ $live_state != running ]]; then
      live_exit=$(docker inspect --format '{{.State.ExitCode}}' "$live_container" 2>/dev/null || echo '?')
      problems+=("the container is $live_state (exit code $live_exit) and /healthz never answered 200")
    else
      problems+=("no 200 from /healthz within $((fail_ms / 1000)) s (last status $live_code)")
    fi
  else
    add_number live_time_to_healthy_s "$(fmt_s "$live_healthy_ms")"
    if ((live_healthy_ms > fail_ms)); then
      problems+=("first /healthz 200 after $(fmt_s "$live_healthy_ms") s, over the $((fail_ms / 1000)) s limit")
    fi
    # Let the first attempts to reach Home Assistant fail and be logged: the loops must back off and carry on, not stop the host.
    sleep "$live_settle_s"
    live_state=$(docker inspect --format '{{.State.Status}}' "$live_container" 2>/dev/null || echo gone)
    if [[ $live_state != running ]]; then
      live_exit=$(docker inspect --format '{{.State.ExitCode}}' "$live_container" 2>/dev/null || echo '?')
      problems+=("the container is $live_state (exit code $live_exit) $live_settle_s s after the first 200")
    fi
  fi
  live_db_bytes=0
  if [[ -s $live_dir/realm.db ]]; then
    live_db_bytes=$(stat -c %s "$live_dir/realm.db" 2>/dev/null || echo 0)
  else
    problems+=("/data/realm.db was not created")
  fi
  if [[ ! -d $live_dir/dp-keys ]]; then
    problems+=("/data/dp-keys was not created (the Data Protection key ring of 03 section 5.7)")
  fi
  live_log=$(docker logs "$live_container" 2>&1)
  # An unhandled exception, a crashed hosted service (the host stops on it), a start that failed, any critical line. "fail:" is not one of them.
  crashes=$(grep -E 'Unhandled exception|BackgroundService failed|Application startup exception|Host terminated unexpectedly|Hosting failed to start|(^|[[:space:]])crit: ' <<<"$live_log" | head -n 3 | cut -c1-200)
  if [[ -n $crashes ]]; then
    problems+=("the log shows a crash: $(tr '\n' '|' <<<"$crashes")")
  fi
  if ((${#problems[@]} == 0)); then
    live_lines=$(wc -l <<<"$live_log" | tr -d ' ')
    record 11 PASS "$title" "/healthz 200 after $(fmt_s "$live_healthy_ms") s, realm.db $live_db_bytes bytes and dp-keys in /data, still running ${live_settle_s} s later, no crash among $live_lines log lines"
  else
    queue_block "container log of the Live start, last 10 lines" "$(tail -n 10 <<<"$live_log" | cut -c1-150)"
    record 11 FAIL "$title" "$(join_problems)"
  fi
fi
cleanup_live

# --- Item 12: the licence notices ship in the image (licence audit 1, action 3) -----------------------------------------------------
# The image redistributes MIT, BSD-3-Clause and Apache-2.0 components, so their texts travel with it: the project's LICENSE, the
# THIRD-PARTY-NOTICES.md that names every component, the Apache-2.0 text it points to, and MapLibre's own LICENSE.txt beside the vendored
# files. The SIL OFL asks for its text with every copy of a font, so while a .woff2 is in wwwroot/fonts an OFL-*.txt must sit there too.
# A throw-away container of the same image (--entrypoint: the image's ENTRYPOINT is "dotnet Realm.Web.dll"); a file that is missing or empty fails.
title="licence notices in the image"
lic_out=$(docker run --rm --entrypoint sh "$image" -c '
cd /app || exit 3
for f in LICENSE THIRD-PARTY-NOTICES.md LICENSES/Apache-2.0.txt wwwroot/lib/maplibre-gl/LICENSE.txt; do
  [ -s "$f" ] || echo "missing /app/$f"
done
if [ -n "$(find wwwroot/fonts -name "*.woff2" -print -quit 2>/dev/null)" ]; then
  echo "fonts present"
  [ -n "$(find wwwroot/fonts -name "OFL-*.txt" -size +0c -print -quit 2>/dev/null)" ] || echo "missing /app/wwwroot/fonts/OFL-*.txt (a font file is in the image)"
fi
' 2>"$work/licences.err")
lic_status=$?
lic_missing=$(grep '^missing ' <<<"$lic_out" | sed 's/^missing //' | tr '\n' ' ')
lic_missing=${lic_missing% }
if ((lic_status != 0)); then
  record 12 FAIL "$title" "docker run failed (exit $lic_status): $(head -c 300 "$work/licences.err")"
elif [[ -n $lic_missing ]]; then
  record 12 FAIL "$title" "missing or empty in the image: $lic_missing"
elif grep -q '^fonts present' <<<"$lic_out"; then
  record 12 PASS "$title" "LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them"
else
  record 12 PASS "$title" "LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app (no font file in the image yet)"
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
