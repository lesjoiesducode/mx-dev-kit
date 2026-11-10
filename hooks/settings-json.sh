# JSON helpers shared by hooks/install.sh and hooks/uninstall.sh (sourced, POSIX sh).
#
# macOS: osascript -l JavaScript (settings-json.js), which ships with every Mac. Elsewhere: jq.
# MX_JSON_TOOL=osascript or jq forces one (the tests run both).
#
# Needs $here, the folder of settings-json.js.

json_tool=${MX_JSON_TOOL:-}
if [ -z "$json_tool" ]; then
  if [ -x /usr/bin/osascript ]; then json_tool=osascript
  elif command -v jq >/dev/null 2>&1; then json_tool=jq
  fi
fi

json_tool_missing() {
  if [ -z "$json_tool" ]; then
    echo "Neither osascript (macOS) nor jq found to edit the settings safely." >&2
    echo "Install jq, or add the hooks by hand: see hooks/README.md." >&2
    return 0
  fi
  return 1
}

# A JSON string, for a command line made of a path and plain words.
json_string() {
  printf '"%s"' "$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g')"
}

# Drops any earlier agent-haptics entry, then appends ours to each event.
jq_merge='
  def without_ours: map(.hooks |= map(select(((.command // "") | contains("agent-haptics")) | not)))
                    | map(select((.hooks | length) > 0));
  .hooks = (.hooks // {})
  | reduce ($entries | to_entries[]) as $e (.;
      .hooks[$e.key] = (((.hooks[$e.key] // []) | without_ours) + $e.value))
'

jq_strip='
  def without_ours: map(.hooks |= map(select(((.command // "") | contains("agent-haptics")) | not)))
                    | map(select((.hooks | length) > 0));
  if (.hooks | type) == "object" then
    .hooks |= (with_entries(.value |= without_ours) | with_entries(select((.value | length) > 0)))
    | if (.hooks | length) == 0 then del(.hooks) else . end
  else . end
'

# json_check <file>: true when the file is missing, empty or a JSON object.
json_check() {
  case $json_tool in
    osascript) /usr/bin/osascript -l JavaScript "$here/settings-json.js" check "$1" >/dev/null 2>&1 ;;
    jq) [ ! -s "$1" ] || [ "$(jq -r 'type' "$1" 2>/dev/null)" = object ] ;;
  esac
}

# json_format <file>: the file as the other commands print it.
json_format() {
  case $json_tool in
    osascript) /usr/bin/osascript -l JavaScript "$here/settings-json.js" format "$1" ;;
    jq) if [ -s "$1" ]; then jq . "$1"; else jq -n '{}'; fi ;;
  esac
}

# json_merge <file> <entries>
json_merge() {
  case $json_tool in
    osascript) /usr/bin/osascript -l JavaScript "$here/settings-json.js" merge "$1" "$2" ;;
    jq) if [ -s "$1" ]; then jq --argjson entries "$2" "$jq_merge" "$1"; else jq -n --argjson entries "$2" "$jq_merge"; fi ;;
  esac
}

# json_strip <file>
json_strip() {
  case $json_tool in
    osascript) /usr/bin/osascript -l JavaScript "$here/settings-json.js" strip "$1" ;;
    jq) jq "$jq_strip" "$1" ;;
  esac
}
