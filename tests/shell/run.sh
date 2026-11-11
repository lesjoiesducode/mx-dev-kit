#!/bin/sh
# Tests of the command-line tools, with fake terminal-notifier, osascript, curl and docker
# that record how they are called. Nothing reaches the mouse, the screen or Docker.
#
#   sh tests/shell/run.sh

set -u

root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fake="$work/fake"
calls="$work/calls"
mkdir -p "$fake" "$work/home"
export HOME="$work/home" MX_TEST_CALLS="$calls"
unset APPDATA MX_NOTIFY_WITH MX_HAPTIC_URL GEMINI_CLI_HOME CLAUDE_CONFIG_DIR CODEX_HOME COPILOT_HOME MX_TURN_SECONDS
state="$HOME/.config/mx-dev-kit"
mkdir -p "$state"

# A fake tool writes "name|arg1|arg2|..." to the calls file, in one write: fakes that run
# in the background at the same time must not mix their lines.
for tool in terminal-notifier osascript notify-send curl; do
  printf '#!/bin/sh\nline="%s"; for a in "$@"; do line="$line|$a"; done; printf "%%s\\n" "$line" >> "$MX_TEST_CALLS"\n' "$tool" > "$fake/$tool"
  chmod +x "$fake/$tool"
done
PATH="$fake:$PATH"

passed=0
failed=0

ok() { passed=$((passed + 1)); }
ko() { failed=$((failed + 1)); printf 'FAIL  %s\n' "$1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/      /'; }

# expect <description> <expected exit code> <command...>: runs the command with a fresh calls file.
expect() {
  desc=$1 code=$2
  shift 2
  : > "$calls"
  "$@" > "$work/out" 2>&1
  got=$?
  if [ "$got" -eq "$code" ]; then ok; else ko "$desc (exit $got, expected $code)" "$(cat "$work/out")"; fi
}

# called <description> <exact line expected in the calls file>
called() {
  if grep -qxF -- "$2" "$calls"; then ok; else ko "$1" "expected: $2
got:
$(cat "$calls")"; fi
}

# not_called <description> <tool name>
not_called() {
  if grep -q "^$2|" "$calls"; then ko "$1" "$(cat "$calls")"; else ok; fi
}

# output_has <description> <text>
output_has() {
  if grep -qF -- "$2" "$work/out"; then ok; else ko "$1" "expected in output: $2
got:
$(cat "$work/out")"; fi
}

# --- mx-notify -------------------------------------------------------------

n="$root/bin/mx-notify"

expect "mx-notify without arguments shows the usage" 2 sh "$n"
expect "mx-notify --help" 0 sh "$n" --help
output_has "mx-notify --help prints the usage" "mx-notify <title> <text> [url]"

if [ "$(uname -s)" = Darwin ]; then
  expect "mx-notify with terminal-notifier" 0 sh "$n" "Merged" "acme/api#12 Add search" "https://github.com/acme/api/pull/12"
  called "terminal-notifier gets the title, the text and the URL" \
    "terminal-notifier|-title|Merged|-message|acme/api#12 Add search|-open|https://github.com/acme/api/pull/12"

  expect "mx-notify without URL" 0 sh "$n" "db-1 died" "exit code 1"
  called "no -open without URL" "terminal-notifier|-title|db-1 died|-message|exit code 1"

  expect "mx-notify escapes a leading bracket or dash" 0 sh "$n" "[api] down" "-1 left"
  called "leading [ and - are escaped for terminal-notifier" "terminal-notifier|-title|\\[api] down|-message|\\-1 left"

  expect "mx-notify through AppleScript" 0 env MX_NOTIFY_WITH=osascript sh "$n" 'Say "hi"' "it's \$HOME" "https://example.com"
  called "osascript gets the texts as arguments, untouched" \
    "osascript|-e|on run argv|-e|display notification (item 2 of argv) with title (item 1 of argv)|-e|end run|Say \"hi\"|it's \$HOME"
  not_called "MX_NOTIFY_WITH=osascript does not call terminal-notifier" terminal-notifier
fi

expect "mx-notify through notify-send" 0 env MX_NOTIFY_WITH=notify-send sh "$n" "Title" "Text"
called "notify-send gets the title and the text" "notify-send|--|Title|Text"

expect "mx-notify refuses an unknown MX_NOTIFY_WITH" 2 env MX_NOTIFY_WITH=growl sh "$n" "Title" "Text"

touch "$state/notifications.off"
expect "mx-notify is silent with notifications.off" 0 sh "$n" "Title" "Text"
not_called "notifications.off: no terminal-notifier" terminal-notifier
not_called "notifications.off: no osascript" osascript
rm -f "$state/notifications.off"

# --- docker-watch -------------------------------------------------------------

# A fake docker: `docker events` replays a recorded stream in docker-watch's format.
cat > "$fake/docker" <<'FAKE'
#!/bin/sh
line=docker; for a in "$@"; do line="$line|$a"; done; printf '%s\n' "$line" >> "$MX_TEST_CALLS"
case $1 in
  info) [ -n "${MX_TEST_DOCKER_DOWN:-}" ] && exit 1; exit 0 ;;
  ps) [ -f "$MX_TEST_UNHEALTHY" ] && cat "$MX_TEST_UNHEALTHY"; exit 0 ;;
  events) cat "$MX_TEST_EVENTS" ;;
esac
FAKE
chmod +x "$fake/docker"

shop="$work/shop"
mkdir -p "$shop/src"
: > "$shop/compose.yaml"
export MX_TEST_EVENTS="$work/events" MX_TEST_UNHEALTHY="$work/unhealthy"
echo "shop-seeded-1" > "$MX_TEST_UNHEALTHY"

# event <seconds> <action> <id> <name> <exit code> [working dir] [project] [service]
base=1794312000
event() {
  printf '%s000000000\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$((base + $1))" "$2" "$3" "$4" "$5" "${6-$shop}" "${7-shop}" "${8-$4}" >> "$MX_TEST_EVENTS"
}
: > "$MX_TEST_EVENTS"
event 0 die c1 shop-db-1 1
event 1 kill c2 shop-web-1 ""
event 2 die c2 shop-web-1 143
event 3 stop c2 shop-web-1 ""
event 4 oom c3 shop-worker-1 ""
event 4 die c3 shop-worker-1 137
event 10 die c4 shop-api-1 1
event 20 die c4 shop-api-1 1
event 30 die c4 shop-api-1 1
event 40 die c4 shop-api-1 1
event 50 "health_status: unhealthy" c5 shop-cache-1 ""
event 60 "health_status: healthy" c5 shop-cache-1 ""
event 61 "health_status: healthy" c5 shop-cache-1 ""
event 62 "health_status: healthy" c6 shop-seeded-1 ""
event 70 die c7 shop-job-1 0
event 80 die c8 other-db-1 1 /work/other other db
event 700 die c4 shop-api-1 2

dw="$root/bin/docker-watch"
export MX_DOCKER_EVENTS_ONCE=1 MX_NOTIFY_WITH=notify-send

waves() { grep -c "haptic/$1\$" "$calls"; }

cd "$shop/src" || exit 1
expect "docker-watch in a compose project" 0 sh "$dw"
output_has "docker-watch says what it watches" "Watching the containers of the compose project in $shop"
output_has "a container that dies is reported" "DIED     shop-db-1 (exit code 1)"
output_has "a stop on purpose is quiet" "stopped  shop-web-1 (on purpose)"
output_has "out of memory is reported" "OOM      shop-worker-1 (out of memory)"
output_has "three deaths in a minute is a loop" "LOOP     shop-api-1 (died 3 times in 60 s, quiet for 10 min)"
output_has "a loop is quiet afterwards" "died     shop-api-1 (exit code 1, restarting in a loop)"
output_has "a container that comes back to health is reported" "HEALTHY  shop-cache-1 (healthy again)"
output_has "unhealthy containers found at start count" "HEALTHY  shop-seeded-1 (healthy again)"
output_has "exit 0 is not a death" "exited   shop-job-1 (exit 0)"
output_has "the loop is reported again after 10 minutes" "DIED     shop-api-1 (exit code 2)"
if grep -q other-db-1 "$work/out"; then ko "containers of another compose project are left out" "$(cat "$work/out")"; else ok; fi
# db, worker, the first death of api, the loop of api, api again 10 minutes later.
if [ "$(waves angry_alert)" = 5 ]; then ok; else ko "5 angry_alert" "$(cat "$calls")"; fi
if [ "$(waves happy_alert)" = 2 ]; then ok; else ko "2 happy_alert (cache, seeded)" "$(cat "$calls")"; fi
called "a death shows a notification" "notify-send|--|shop-db-1 died|Exit code 1, service shop-db-1 of shop."
called "out of memory shows a notification" "notify-send|--|shop-worker-1 ran out of memory|Out of memory, service shop-worker-1 of shop."
called "a loop shows one notification" "notify-send|--|shop-api-1 restarts in a loop|Died 3 times in a minute (last: exit code 1), service shop-api-1 of shop. Quiet about it for 10 minutes."
called "health back shows a notification" "notify-send|--|shop-cache-1 is healthy again|Its healthcheck passes again, service shop-cache-1 of shop."
called "docker events is filtered on the container events it needs" \
  "docker|events|--filter|type=container|--filter|event=die|--filter|event=oom|--filter|event=kill|--filter|event=stop|--filter|event=health_status|--format|{{.TimeNano}}	{{.Action}}	{{.Actor.ID}}	{{index .Actor.Attributes \"name\"}}	{{index .Actor.Attributes \"exitCode\"}}	{{index .Actor.Attributes \"com.docker.compose.project.working_dir\"}}	{{index .Actor.Attributes \"com.docker.compose.project\"}}	{{index .Actor.Attributes \"com.docker.compose.service\"}}"

expect "docker-watch -q" 0 sh "$dw" -q
if grep -q "stopped\|exited\|unhealthy" "$work/out"; then ko "-q only prints what vibrates" "$(cat "$work/out")"; else ok; fi
output_has "-q still prints alerts" "DIED     shop-db-1 (exit code 1)"

expect "docker-watch -p other" 0 sh "$dw" -p other
output_has "-p keeps the containers of that project" "DIED     other-db-1 (exit code 1)"
if grep -q shop-db-1 "$work/out"; then ko "-p leaves the other projects out" "$(cat "$work/out")"; else ok; fi

cd "$work" || exit 1
expect "docker-watch outside a compose project watches everything" 0 sh "$dw"
output_has "every container" "Watching every container."
output_has "both projects are reported" "DIED     other-db-1 (exit code 1)"

expect "docker-watch --all" 0 sh "$dw" --all
output_has "--all reports the other project too" "DIED     other-db-1 (exit code 1)"

touch "$state/haptics.off"
expect "docker-watch with haptics.off" 0 sh "$dw" --all
if [ "$(waves angry_alert)" = 0 ]; then ok; else ko "haptics.off: no vibration"; fi
rm -f "$state/haptics.off"

expect "docker-watch when Docker is not running" 1 env MX_TEST_DOCKER_DOWN=1 sh "$dw"
output_has "it says Docker is not running" "Docker is not running: waiting for it..."
expect "docker-watch rejects an unknown option" 2 sh "$dw" --nope
cd "$root" || exit 1
unset MX_DOCKER_EVENTS_ONCE MX_NOTIFY_WITH

# --- install.sh / uninstall.sh ------------------------------------------------

if [ "$(uname -s)" = Darwin ]; then
  expect "install.sh --only shell" 0 sh "$root/install.sh" --only shell
  for f in haptic mx-notify uptime-watch docker-watch; do
    if [ -x "$state/bin/$f" ]; then ok; else ko "install.sh installs bin/$f, executable"; fi
  done
  for f in haptic.ps1 mx-notify.ps1 uptime-watch.ps1 docker-watch.ps1; do
    if [ -f "$state/bin/$f" ]; then ok; else ko "install.sh installs bin/$f"; fi
  done
  if [ -x "$state/uninstall.sh" ] && [ -f "$state/hooks/settings-json.js" ]; then ok; else ko "install.sh leaves its uninstaller in the state folder"; fi
  expect "uninstall.sh" 0 sh "$root/uninstall.sh"
  if [ -d "$state/bin" ]; then ko "uninstall.sh removes the installed scripts"; else ok; fi
  if [ -f "$state/config.json" ]; then ok; else ko "uninstall.sh keeps config.json"; fi

  # A user's machine: what macOS ships, no .NET, no Homebrew, no jq; open and the release faked.
  bare="$work/bare"
  mkdir -p "$bare"
  printf '#!/bin/sh\nline=open; for a in "$@"; do line="$line|$a"; done; printf "%%s\\n" "$line" >> "$MX_TEST_CALLS"\n' > "$bare/open"
  chmod +x "$bare/open"
  user_path="$bare:/usr/bin:/bin:/usr/sbin:/sbin"
  u="$work/user"
  release="$work/release"
  uplugins="$u/Library/Application Support/Logi/LogiPluginService/Plugins"
  mkdir -p "$uplugins" "$release/pkg/metadata" "$release/pkg/bin" "$release/stage"
  printf 'type: plugin4\nname: MxDevKit\nversion: 9.9.9\n' > "$release/pkg/metadata/LoupedeckPackage.yaml"
  printf 'not a real DLL\n' > "$release/pkg/bin/MxDevKitPlugin.dll"
  (cd "$release/pkg" && zip -qr ../MxDevKit.lplug4 metadata bin)
  # The Haptic Web Plugin of its own release, and a package that is not it.
  mkdir -p "$release/hw/metadata" "$release/hw/bin" "$release/other/metadata" "$release/other/bin"
  printf 'type: plugin4\nname: HapticWeb\ndisplayName: Haptic Web Plugin\nversion: 1.2\n' > "$release/hw/metadata/LoupedeckPackage.yaml"
  printf 'not a real DLL\n' > "$release/hw/bin/HapticWebPlugin.dll"
  (cd "$release/hw" && zip -qr ../HapticWeb.lplug4 metadata bin)
  printf 'type: plugin4\nname: Other\nversion: 1.0\n' > "$release/other/metadata/LoupedeckPackage.yaml"
  printf 'not a real DLL\n' > "$release/other/bin/Other.dll"
  (cd "$release/other" && zip -qr ../Other.lplug4 metadata bin)
  git -C "$root" ls-files -z -- install.sh install.ps1 uninstall.sh uninstall.ps1 config.example.json hooks shell bin \
    | (cd "$root" && xargs -0 tar cf -) | (mkdir -p "$release/stage/mx-dev-kit" && cd "$release/stage/mx-dev-kit" && tar xf -)
  # The working tree, not the last commit: the files under test.
  for f in install.sh uninstall.sh hooks/install.sh hooks/uninstall.sh hooks/settings-json.sh hooks/settings-json.js; do
    cp "$root/$f" "$release/stage/mx-dev-kit/$f"
  done
  printf '9.9.9\n' > "$release/stage/mx-dev-kit/VERSION"
  cp "$release/MxDevKit.lplug4" "$release/stage/mx-dev-kit/"
  (cd "$release/stage" && zip -qr ../mx-dev-kit.zip mx-dev-kit)
  cp "$root/install.sh" "$release/install.sh"
  (cd "$release" && shasum -a 256 MxDevKit.lplug4 mx-dev-kit.zip install.sh > SHA256SUMS)
  ustate="$u/.config/mx-dev-kit"
  hw_url="file://$release/HapticWeb.lplug4"
  as_user() { env -i HOME="$u" PATH="$user_path" SHELL=/bin/zsh MX_TEST_CALLS="$calls" MX_DEV_KIT_INSTALL_WAIT=0 \
    MX_DEV_KIT_RELEASE_URL="file://$release" MX_HAPTIC_WEB_URL="${hw_url}" "$@"; }

  if as_user sh -c 'command -v dotnet || command -v brew || command -v logiplugintool' >/dev/null; then
    ko "the user PATH of the tests has no .NET, Homebrew or LogiPluginTool"
  else ok; fi

  printf '# my zshrc\nexport A=1\n' > "$u/.zshrc"
  cp "$u/.zshrc" "$work/zshrc-before"
  expect "curl | sh, from the release, without .NET" 0 as_user sh -c "curl -fsSL file://$release/install.sh | sh -s -- --yes"
  output_has "curl | sh downloads the kit" "Downloading MX Dev Kit"
  output_has "curl | sh installs version 9.9.9" "MX Dev Kit 9.9.9"
  if [ "$(sed -n 's/^version: *//p' "$uplugins/MxDevKit/metadata/LoupedeckPackage.yaml" 2>/dev/null)" = 9.9.9 ]; then ok; else
    ko "curl | sh puts the plugin in the Logi Plugin Service folder" "$(find "$uplugins")"; fi
  called "curl | sh asks Logi Plugin Service to load it" "open|-g|loupedeck://plugin/MxDevKit/reload"
  if grep -qi 'dotnet\|\.NET' "$work/out"; then ko "curl | sh never mentions .NET" "$(cat "$work/out")"; else ok; fi
  if grep -q 'mx-dev-kit >>>' "$u/.zshrc"; then ok; else ko "curl | sh adds the shell integration"; fi
  if [ -x "$ustate/uninstall.sh" ]; then ok; else ko "curl | sh leaves the uninstaller"; fi
  if [ -f "$uplugins/HapticWeb/bin/HapticWebPlugin.dll" ]; then ok; else ko "curl | sh --yes installs the Haptic Web Plugin" "$(cat "$work/out")"; fi
  called "curl | sh asks Logi Plugin Service to load the Haptic Web Plugin" "open|-g|loupedeck://plugin/HapticWeb/reload"

  expect "the uninstaller left by install.sh" 0 as_user sh "$ustate/uninstall.sh" --purge
  output_has "uninstall.sh says the Haptic Web Plugin stays" "The Haptic Web Plugin stays installed"
  if [ -d "$uplugins/HapticWeb" ]; then ok; else ko "uninstall.sh leaves the Haptic Web Plugin"; fi
  rm -rf "$uplugins/HapticWeb"
  if cmp -s "$u/.zshrc" "$work/zshrc-before"; then ok; else ko "uninstall.sh restores ~/.zshrc as it was" "$(diff "$work/zshrc-before" "$u/.zshrc")"; fi
  if [ -e "$ustate" ]; then ko "uninstall.sh --purge removes the state folder" "$(find "$ustate")"; else ok; fi
  if [ -e "$uplugins/MxDevKit" ]; then ko "uninstall.sh removes the plugin"; else ok; fi

  # From a clone, without the package next to it: the one of the latest release.
  expect "install.sh from a clone downloads the plugin" 0 as_user sh "$root/install.sh" --only plugin
  if [ -f "$uplugins/MxDevKit/bin/MxDevKitPlugin.dll" ]; then ok; else ko "install.sh from a clone installs the downloaded plugin"; fi
  expect "install.sh again: nothing to do for the plugin" 0 as_user sh "$root/install.sh" --only plugin
  output_has "install.sh says the plugin is already there" "MX Dev Kit 9.9.9 is already installed"
  if cmp -s "$ustate/MxDevKit.lplug4" "$release/MxDevKit.lplug4"; then ok; else ko "install.sh downloads the release package"; fi
  printf '0000000000000000000000000000000000000000000000000000000000000000  MxDevKit.lplug4\n' > "$release/SHA256SUMS"
  rm -rf "$ustate/MxDevKit.lplug4" "$uplugins/MxDevKit"
  expect "install.sh refuses a package with a wrong checksum" 0 as_user sh "$root/install.sh" --only plugin
  output_has "install.sh says the checksum does not match" "checksum mismatch"
  if [ -e "$uplugins/MxDevKit" ]; then ko "a package with a wrong checksum is not installed"; else ok; fi
  (cd "$release" && shasum -a 256 MxDevKit.lplug4 mx-dev-kit.zip install.sh > SHA256SUMS)
  expect "--from-source without .NET says what it needs" 1 as_user sh "$root/install.sh" --only plugin --from-source
  output_has "--from-source asks for the .NET 10 SDK" ".NET 10 SDK"
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

  # One part at a time. --only skips the questions.
  mkdir -p "$u/.claude" && printf '{}\n' > "$u/.claude/settings.json"
  expect "install.sh --only hooks,shell" 0 as_user sh "$root/install.sh" --only hooks,shell
  if [ -e "$uplugins/MxDevKit" ]; then ko "--only hooks,shell installs no plugin"; else ok; fi
  if [ -x "$ustate/bin/haptic" ]; then ok; else ko "--only hooks,shell installs the haptic command"; fi
  if grep -q 'mx-dev-kit >>>' "$u/.zshrc"; then ok; else ko "--only hooks,shell adds the shell integration"; fi
  if grep -q "agent-haptics.sh' claude Stop" "$u/.claude/settings.json"; then ok; else ko "--only hooks,shell adds the Claude Code hooks" "$(cat "$u/.claude/settings.json")"; fi
  if grep -q '\[Y/n\]' "$work/out"; then ko "--only asks nothing" "$(cat "$work/out")"; else ok; fi
  if [ -f "$uplugins/HapticWeb/metadata/LoupedeckPackage.yaml" ]; then ok; else ko "--only hooks,shell installs the Haptic Web Plugin" "$(cat "$work/out")"; fi
  output_has "install.sh says where the Haptic Web Plugin went" "Installed Haptic Web Plugin 1.2 in $uplugins/HapticWeb"
  output_has "install.sh gives the test once the Haptic Web Plugin is in" "test the vibrations with $ustate/bin/haptic completed"
  if grep -q 'open the Marketplace' "$work/out"; then ko "no Marketplace step once the Haptic Web Plugin is in" "$(cat "$work/out")"; else ok; fi
  if [ -e "$ustate/HapticWeb.lplug4.part" ] || [ -e "$ustate/haptic-web.new" ]; then ko "install.sh leaves no Haptic Web download behind"; else ok; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  if grep -q agent-haptics "$u/.claude/settings.json"; then ko "uninstall.sh removes the hooks"; else ok; fi

  # The Haptic Web Plugin: never replaced, refused when it is not the right package, and a failure
  # never stops the installation.
  rm -rf "$uplugins/HapticWeb"
  mkdir -p "$uplugins/HapticWeb/metadata"
  printf 'name: HapticWeb\nversion: 1.1\n' > "$uplugins/HapticWeb/metadata/LoupedeckPackage.yaml"
  cp "$uplugins/HapticWeb/metadata/LoupedeckPackage.yaml" "$work/hw-before"
  expect "install.sh with a Haptic Web Plugin already there" 0 as_user sh "$root/install.sh" --only hooks
  output_has "install.sh says the Haptic Web Plugin is already there" "Already installed ($uplugins/HapticWeb): left as it is."
  if cmp -s "$uplugins/HapticWeb/metadata/LoupedeckPackage.yaml" "$work/hw-before" && [ ! -e "$uplugins/HapticWeb/bin" ]; then ok; else
    ko "install.sh never replaces an installed Haptic Web Plugin" "$(find "$uplugins/HapticWeb")"; fi
  not_called "nothing to load when the Haptic Web Plugin is already there" "open|-g|loupedeck://plugin/HapticWeb/reload"
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  rm -rf "$uplugins/HapticWeb"

  expect "install.sh --no-haptic-web" 0 as_user sh "$root/install.sh" --only shell --no-haptic-web
  if [ -e "$uplugins/HapticWeb" ]; then ko "--no-haptic-web installs no Haptic Web Plugin"; else ok; fi
  output_has "--no-haptic-web keeps the Marketplace step" "open the Marketplace and install the Haptic Web Plugin"
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

  hw_url="file://$release/Other.lplug4"
  expect "install.sh with a package that is not the Haptic Web Plugin" 0 as_user sh "$root/install.sh" --only shell
  output_has "install.sh refuses it" "is not the Haptic Web Plugin package (name: Other)"
  if [ -e "$uplugins/HapticWeb" ] || [ -e "$uplugins/Other" ]; then ko "a wrong package is not installed" "$(ls "$uplugins")"; else ok; fi
  output_has "a refused package falls back to the Marketplace" "open the Marketplace and install the Haptic Web Plugin"
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

  hw_url="file://$release/missing.lplug4"
  expect "install.sh when the Haptic Web Plugin cannot be downloaded" 0 as_user sh "$root/install.sh" --only hooks,shell
  output_has "install.sh says the download failed" "Could not download it."
  output_has "a failed download falls back to the Marketplace" "open the Marketplace and install the Haptic Web Plugin"
  if [ -x "$ustate/bin/haptic" ] && grep -q 'mx-dev-kit >>>' "$u/.zshrc"; then ok; else ko "a failed download installs the rest" "$(cat "$work/out")"; fi
  if [ -e "$uplugins/HapticWeb" ] || [ -e "$ustate/HapticWeb.lplug4.part" ]; then ko "a failed download leaves nothing behind"; else ok; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  hw_url="file://$release/HapticWeb.lplug4"

  expect "install.sh --only plugin" 0 as_user sh "$root/install.sh" --only plugin
  if [ -f "$uplugins/MxDevKit/bin/MxDevKitPlugin.dll" ]; then ok; else ko "--only plugin installs the plugin"; fi
  if [ -e "$ustate/bin" ]; then ko "--only plugin installs no scripts" "$(ls "$ustate/bin")"; else ok; fi
  if grep -q 'mx-dev-kit >>>' "$u/.zshrc"; then ko "--only plugin leaves ~/.zshrc alone"; else ok; fi
  if grep -q agent-haptics "$u/.claude/settings.json"; then ko "--only plugin adds no hooks"; else ok; fi
  if [ -e "$uplugins/HapticWeb" ]; then ko "--only plugin installs no Haptic Web Plugin"; else ok; fi
  if grep -q 'Haptic Web' "$work/out"; then ko "--only plugin says nothing about the Haptic Web Plugin" "$(cat "$work/out")"; else ok; fi
  if [ -x "$ustate/uninstall.sh" ]; then ok; else ko "--only plugin leaves the uninstaller"; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  if [ -e "$uplugins/MxDevKit" ]; then ko "uninstall.sh removes a plugin installed alone"; else ok; fi
  expect "install.sh --only with an unknown part" 2 as_user sh "$root/install.sh" --only plugin,mouse
  output_has "--only names the unknown part" "Unknown part: mouse"

  # The questions, one per part, Enter meaning yes.
  if command -v python3 >/dev/null 2>&1; then
    answer() { a=$1; shift; python3 -I "$root/tests/shell/answer.py" "$a" env -i HOME="$u" PATH="$user_path" SHELL=/bin/zsh \
      MX_TEST_CALLS="$calls" MX_DEV_KIT_INSTALL_WAIT=0 MX_DEV_KIT_RELEASE_URL="file://$release" MX_HAPTIC_WEB_URL="$hw_url" "$@"; }
    expect "install.sh asks: no plugin, hooks, no shell" 0 answer "n,,n" sh "$root/install.sh"
    output_has "install.sh asks about the plugin" "Actions Ring bubbles (Options+ plugin)? [Y/n]"
    output_has "install.sh asks about the hooks" "Coding agent haptics (Claude Code, Codex, Gemini, Copilot)? [Y/n]"
    output_has "install.sh asks about the shell" "Terminal integration (long commands vibrate, bubbles follow your prompt)? [Y/n]"
    if [ -e "$uplugins/MxDevKit" ]; then ko "n to the plugin installs no plugin"; else ok; fi
    if grep -q "agent-haptics.sh' claude Stop" "$u/.claude/settings.json"; then ok; else ko "Enter to the hooks installs them"; fi
    if [ -x "$ustate/bin/haptic" ]; then ok; else ko "the hooks bring the haptic command"; fi
    if grep -q 'mx-dev-kit >>>' "$u/.zshrc"; then ko "n to the shell leaves ~/.zshrc alone"; else ok; fi
    output_has "install.sh asks about the Haptic Web Plugin, its author and license" \
      "Haptic Web Plugin by Jasper M-W (MIT license), for these vibrations: install it from github.com/Fallstop/HapticWebPlugin? [Y/n]"
    if [ -f "$uplugins/HapticWeb/bin/HapticWebPlugin.dll" ]; then ok; else ko "Enter to the Haptic Web Plugin installs it" "$(cat "$work/out")"; fi
    as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

    expect "install.sh asks again with the Haptic Web Plugin there" 0 answer "n,,n,n" sh "$root/install.sh"
    if grep -q 'Haptic Web Plugin by' "$work/out"; then ko "no question when the Haptic Web Plugin is there" "$(cat "$work/out")"; else ok; fi
    as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
    rm -rf "$uplugins/HapticWeb"

    expect "install.sh: n to the Haptic Web Plugin" 0 answer "n,,n,n" sh "$root/install.sh"
    if [ -e "$uplugins/HapticWeb" ]; then ko "n to the Haptic Web Plugin installs nothing"; else ok; fi
    output_has "n to the Haptic Web Plugin keeps the Marketplace step" "open the Marketplace and install the Haptic Web Plugin"
    as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

    expect "install.sh with every answer n" 0 answer "n,n,n" sh "$root/install.sh"
    output_has "install.sh says there is nothing to install" "Nothing to install."
    if [ -e "$ustate" ]; then ko "nothing chosen, nothing written" "$(find "$ustate")"; else ok; fi

    expect "curl | sh asks through the terminal" 0 answer "y,n,n" sh -c "curl -fsSL file://$release/install.sh | sh"
    if [ -d "$uplugins/MxDevKit" ]; then ok; else ko "curl | sh installs the plugin answered y" "$(cat "$work/out")"; fi
    if [ -e "$ustate/bin" ]; then ko "curl | sh installs no scripts when the hooks and the shell are answered n"; else ok; fi
    as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

    expect "install.sh without a terminal" 0 python3 -I "$root/tests/shell/answer.py" --no-tty env -i HOME="$u" PATH="$user_path" \
      SHELL=/bin/zsh MX_TEST_CALLS="$calls" MX_DEV_KIT_INSTALL_WAIT=0 MX_DEV_KIT_RELEASE_URL="file://$release" MX_HAPTIC_WEB_URL="$hw_url" \
      sh "$root/install.sh"
    output_has "install.sh says it installs everything" "No terminal to ask questions: installing everything"
    if [ -d "$uplugins/MxDevKit" ] && [ -x "$ustate/bin/haptic" ] && grep -q 'mx-dev-kit >>>' "$u/.zshrc" && [ -d "$uplugins/HapticWeb" ]; then ok; else
      ko "install.sh without a terminal installs every part" "$(cat "$work/out")"; fi
    as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
    rm -rf "$uplugins/HapticWeb"
  else
    printf 'skip  the installer questions (no python3)\n'
  fi

  # The shell decides where the integration goes.
  rm -f "$u/.zshrc"
  mkdir -p "$u/.zdot"
  # ZDOTDIR set in ~/.zshenv without export, as many dotfiles do: the installer asks zsh.
  printf 'ZDOTDIR="%s/.zdot"\n' "$u" > "$u/.zshenv"
  expect "install.sh with ZDOTDIR" 0 as_user sh "$root/install.sh" --only shell --no-haptic-web
  if grep -q 'mx-dev-kit >>>' "$u/.zdot/.zshrc" 2>/dev/null && [ ! -e "$u/.zshrc" ]; then ok; else ko "ZDOTDIR: the line goes to \$ZDOTDIR/.zshrc" "$(cat "$work/out")"; fi
  expect "uninstall.sh with ZDOTDIR" 0 as_user sh "$ustate/uninstall.sh" --purge
  if grep -q 'mx-dev-kit' "$u/.zdot/.zshrc"; then ko "uninstall.sh removes it from \$ZDOTDIR/.zshrc"; else ok; fi
  rm -rf "$u/.zshenv" "$u/.zdot"

  expect "install.sh for bash, no profile yet" 0 as_user env SHELL=/bin/bash sh "$root/install.sh" --only shell --no-haptic-web
  if grep -q 'mx.bash' "$u/.bash_profile" 2>/dev/null && [ ! -e "$u/.bashrc" ]; then ok; else ko "bash: the line goes to ~/.bash_profile, the file login bash reads" "$(cat "$work/out")"; fi
  if [ -e "$u/.zshrc" ]; then ko "bash: ~/.zshrc is left alone"; else ok; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  rm -f "$u/.bash_profile" "$u/.bash_profile".bak-*

  printf '# my profile\n' > "$u/.profile"
  expect "install.sh for bash with only ~/.profile" 0 as_user env SHELL=/bin/bash sh "$root/install.sh" --only shell --no-haptic-web
  if grep -q 'BASH_VERSION' "$u/.profile" && [ ! -e "$u/.bash_profile" ]; then ok; else ko "bash: an existing ~/.profile gets the line, for bash only" "$(cat "$u/.profile")"; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
  if [ "$(cat "$u/.profile")" = "# my profile" ]; then ok; else ko "uninstall.sh restores ~/.profile" "$(cat "$u/.profile")"; fi
  rm -f "$u/.profile" "$u/.profile".bak-*

  ufish="$u/.config/fish/conf.d/mx-dev-kit.fish"
  expect "install.sh for fish" 0 as_user env SHELL=/opt/homebrew/bin/fish sh "$root/install.sh" --only shell --no-haptic-web
  if grep -qF "source \"$ustate/shell/mx.fish\"" "$ufish" 2>/dev/null && [ -f "$ustate/shell/mx.fish" ]; then ok; else ko "fish: a file of its own in conf.d" "$(cat "$work/out")"; fi
  if [ -e "$u/.zshrc" ]; then ko "fish: ~/.zshrc is left alone"; else ok; fi
  expect "install.sh for fish again" 0 as_user env SHELL=/opt/homebrew/bin/fish sh "$root/install.sh" --only shell --no-haptic-web
  output_has "fish: nothing added twice" "Already in $ufish"
  expect "uninstall.sh for fish" 0 as_user sh "$ustate/uninstall.sh" --purge
  if [ -e "$ufish" ]; then ko "uninstall.sh deletes the fish file"; else ok; fi

  expect "install.sh with a shell it does not know" 0 as_user env SHELL=/opt/homebrew/bin/nu sh "$root/install.sh" --only shell --no-haptic-web
  output_has "an unknown shell is named" "Your shell is nu: MX Dev Kit has no integration for it, nothing was added."
  output_has "the line to add by hand is given" ". \"$ustate/shell/mx.zsh\""
  if grep -q 'Added' "$work/out"; then ko "an unknown shell never reads Added" "$(cat "$work/out")"; else ok; fi
  if [ -e "$u/.zshrc" ] || [ -e "$u/.bash_profile" ] || [ -e "$ufish" ]; then ko "an unknown shell gets nothing written"; else ok; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1

  # Ring Dev Kit, the former name: its uninstaller runs and the settings move over.
  legacy="$u/.config/actions-ring-dev-kit"
  mkdir -p "$legacy"
  printf '{"legacy": true}\n' > "$legacy/config.json"
  : > "$legacy/haptics.off"
  printf '#!/bin/sh\ntouch "$HOME/legacy-uninstalled"\nrm -f "$HOME/.config/actions-ring-dev-kit/uninstall.sh"\n' > "$legacy/uninstall.sh"
  expect "install.sh over a Ring Dev Kit install" 0 as_user sh "$root/install.sh" --only hooks
  output_has "install.sh says it replaced Ring Dev Kit" "Replaced Ring Dev Kit"
  if [ -e "$u/legacy-uninstalled" ]; then ok; else ko "install.sh runs the uninstaller of Ring Dev Kit"; fi
  if grep -q legacy "$ustate/config.json" 2>/dev/null && [ -e "$ustate/haptics.off" ]; then ok; else ko "install.sh moves the Ring Dev Kit settings" "$(find "$ustate")"; fi
  if [ -e "$legacy" ]; then ko "install.sh removes the emptied Ring Dev Kit folder" "$(find "$legacy")"; else ok; fi
  as_user sh "$ustate/uninstall.sh" --purge > /dev/null 2>&1
fi

# --- hooks/install.sh and hooks/uninstall.sh ----------------------------------

# The settings are edited with osascript on macOS and jq elsewhere: both must give the same file.
tools=""
[ -x /usr/bin/osascript ] && tools="$tools osascript"
command -v jq >/dev/null 2>&1 && tools="$tools jq"
claude_before='{"model":"opus","note":"caf\u00e9 \u2192","hooks":{"Stop":[{"matcher":"","hooks":[{"type":"command","command":"true"}]}],"PreToolUse":[{"hooks":[{"type":"command","command":"x"}]}]}}'
codex_before='{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"true"}]}]}}'
for tool in $tools; do
  h="$work/hooks-$tool/it's home"
  mkdir -p "$h/.claude" "$h/.codex"
  printf '%s\n' "$claude_before" > "$h/.claude/settings.json"
  printf '%s\n' "$codex_before" > "$h/.codex/hooks.json"
  run_hooks() { env HOME="$h" CODEX_HOME="$h/.codex" CLAUDE_CONFIG_DIR= MX_JSON_TOOL=$tool sh "$@"; }

  expect "hooks/install.sh with $tool" 0 run_hooks "$root/hooks/install.sh" --claude --codex
  cp "$h/.claude/settings.json" "$work/claude-$tool.json"
  if grep -q "agent-haptics.sh' claude Notification" "$h/.claude/settings.json"; then ok; else ko "hooks/install.sh ($tool) adds the Claude Code hooks" "$(cat "$h/.claude/settings.json")"; fi
  if grep -q '"model": "opus"' "$h/.claude/settings.json" && grep -q '"command": "x"' "$h/.claude/settings.json"; then ok; else ko "hooks/install.sh ($tool) keeps the other settings"; fi
  if ls "$h/.claude"/settings.json.bak-* >/dev/null 2>&1; then ok; else ko "hooks/install.sh ($tool) makes a backup"; fi
  expect "hooks/install.sh again with $tool" 0 run_hooks "$root/hooks/install.sh" --claude --codex
  output_has "hooks/install.sh ($tool) adds nothing twice" "Codex CLI: hooks already up to date"

  printf '{oops\n' > "$h/.gemini.json"
  mkdir -p "$h/.gemini" && cp "$h/.gemini.json" "$h/.gemini/settings.json"
  expect "hooks/install.sh with $tool and an invalid settings.json" 0 run_hooks "$root/hooks/install.sh" --gemini
  if [ "$(cat "$h/.gemini/settings.json")" = '{oops' ]; then ok; else ko "hooks/install.sh ($tool) leaves an invalid file alone"; fi
  output_has "hooks/install.sh ($tool) says the file is invalid" "left untouched"

  expect "hooks/uninstall.sh with $tool" 0 run_hooks "$root/hooks/uninstall.sh"
  if [ "$(tr -d ' \n' < "$h/.codex/hooks.json")" = "$codex_before" ]; then ok; else ko "hooks/uninstall.sh ($tool) restores hooks.json" "$(cat "$h/.codex/hooks.json")"; fi
  if grep -q agent-haptics "$h/.claude/settings.json"; then ko "hooks/uninstall.sh ($tool) removes the Claude Code hooks"; else ok; fi
  if grep -q 'café →' "$h/.claude/settings.json"; then ok; else ko "hooks/uninstall.sh ($tool) keeps non-ASCII text" "$(cat "$h/.claude/settings.json")"; fi
done
if [ -n "$tools" ] && [ -f "$work/claude-osascript.json" ] && [ -f "$work/claude-jq.json" ]; then
  # Same settings once the home folder is the same.
  if [ "$(sed 's|hooks-osascript|X|' "$work/claude-osascript.json")" = "$(sed 's|hooks-jq|X|' "$work/claude-jq.json")" ]; then ok; else
    ko "osascript and jq write the same settings.json" "$(diff "$work/claude-osascript.json" "$work/claude-jq.json")"
  fi
fi

# --- Copilot CLI hooks ----------------------------------------------------------

h="$work/copilot/it's home"
mkdir -p "$h"
run_copilot() { env HOME="$h" COPILOT_HOME="$h/.copilot" sh "$@"; }
cfile="$h/.copilot/hooks/mx-dev-kit.json"

expect "hooks/install.sh --copilot" 0 run_copilot "$root/hooks/install.sh" --copilot
if [ -f "$cfile" ]; then ok; else ko "hooks/install.sh --copilot writes ~/.copilot/hooks/mx-dev-kit.json"; fi
for event in userPromptSubmitted agentStop notification errorOccurred; do
  if grep -qF "agent-haptics.sh' copilot $event\"" "$cfile"; then ok; else ko "Copilot hook file has $event" "$(cat "$cfile")"; fi
done
if grep -q '"version": 1' "$cfile"; then ok; else ko "Copilot hook file has version 1"; fi
if [ -x /usr/bin/osascript ]; then
  if /usr/bin/osascript -l JavaScript -e "JSON.parse(\$.NSString.stringWithContentsOfFileEncodingError('$(printf '%s' "$cfile" | sed "s/'/\\\\'/g")', 4, null).js); 'ok'" >/dev/null 2>&1; then ok; else ko "Copilot hook file is valid JSON" "$(cat "$cfile")"; fi
fi
expect "hooks/install.sh --copilot again" 0 run_copilot "$root/hooks/install.sh" --copilot
output_has "hooks/install.sh --copilot adds nothing twice" "Copilot CLI: hooks already up to date"
expect "hooks/install.sh finds Copilot by its folder" 0 run_copilot "$root/hooks/install.sh" --dry-run
output_has "hooks/install.sh without options includes Copilot CLI" "== Copilot CLI:"
expect "hooks/uninstall.sh removes the Copilot hooks" 0 run_copilot "$root/hooks/uninstall.sh"
if [ -e "$cfile" ]; then ko "hooks/uninstall.sh deletes ~/.copilot/hooks/mx-dev-kit.json"; else ok; fi

# agent-haptics.sh for Copilot: the payloads of the Copilot CLI hooks reference.
ah="$root/hooks/agent-haptics.sh"
haptic_for() {
  : > "$calls"
  printf '%s' "$2" | env MX_HAPTIC_URL=https://local.jmw.nz:41443/haptic sh "$ah" copilot "$1" > "$work/out" 2>&1
  i=0
  while [ ! -s "$calls" ] && [ $i -lt 20 ]; do sleep 0.05; i=$((i + 1)); done
  sed -n 's|.*/haptic/\([a-z_]*\)$|\1|p' "$calls" | head -n 1
}
check_wave() {
  got=$(haptic_for "$2" "$3")
  if [ "$got" = "$4" ]; then ok; else ko "$1 (got '${got}', expected '$4')" "$(cat "$calls")"; fi
}
check_wave "Copilot agentStop plays completed" agentStop '{"stopReason":"end_turn"}' completed
check_wave "Copilot permission prompt knocks" notification '{"hook_event_name":"Notification","notification_type":"permission_prompt","message":"Allow?"}' knock
check_wave "Copilot question knocks" notification '{"notification_type": "elicitation_dialog"}' knock
check_wave "Copilot background agent idle is silent" notification '{"notification_type":"agent_idle"}' ""
check_wave "Copilot shell completed is silent" notification '{"notification_type":"shell_completed"}' ""
check_wave "Copilot fatal error plays angry_alert" errorOccurred '{"error":{"message":"x"},"errorContext":"model_call","recoverable":false}' angry_alert
check_wave "Copilot recoverable error is silent" errorOccurred '{"error":{"message":"x"},"errorContext":"model_call","recoverable": true}' ""

# agent-haptics.sh: the end of a turn vibrates only after MX_TURN_SECONDS.
hook_wave() {
  : > "$calls"
  printf '%s' "$3" | env -u CLAUDE_CODE_ENTRYPOINT MX_HAPTIC_URL=https://local.jmw.nz:41443/haptic ${4:+MX_TURN_SECONDS=$4} sh "$ah" "$1" "$2" > "$work/out" 2>&1
  i=0
  while [ ! -s "$calls" ] && [ $i -lt 10 ]; do sleep 0.05; i=$((i + 1)); done
  sed -n 's|.*/haptic/\([a-z_]*\)$|\1|p' "$calls" | head -n 1
}
check_turn() {
  got=$(hook_wave "$2" "$3" "$4" "${6:-}")
  if [ "$got" = "$5" ]; then ok; else ko "$1 (got '${got}', expected '$5')" "$(cat "$calls")"; fi
}
turns="$state/turns"
check_turn "the start of a turn is silent" claude UserPromptSubmit '{"session_id":"t-1","prompt":"hi"}' ""
if [ -f "$turns/claude-t-1" ]; then ok; else ko "the start of a turn is noted" "$(ls "$turns" 2>&1)"; fi
check_turn "a short turn ends silently" claude Stop '{"session_id":"t-1"}' ""
check_turn "a permission always knocks" claude Notification '{"session_id":"t-1","notification_type":"permission_prompt"}' knock
check_turn "a failed turn always vibrates" claude StopFailure '{"session_id":"t-1"}' angry_alert
check_turn "MX_TURN_SECONDS=0 vibrates at every turn" claude Stop '{"session_id":"t-1"}' completed 0
echo $(($(date +%s) - 45)) > "$turns/claude-t-1"
check_turn "a turn of 30 seconds or more vibrates" claude Stop '{"session_id":"t-1"}' completed
check_turn "a turn whose start is unknown vibrates" claude Stop '{"session_id":"t-2"}' completed
check_turn "Gemini BeforeAgent notes the start" gemini BeforeAgent '{"session_id":"g-1"}' ""
output_has "Gemini BeforeAgent answers {}" "{}"
check_turn "a short Gemini turn ends silently" gemini AfterAgent '{"session_id":"g-1"}' ""
check_turn "Codex UserPromptSubmit notes the start" codex UserPromptSubmit '{"session_id":"c-1"}' ""
check_turn "a short Codex turn ends silently" codex Stop '{"session_id":"c-1"}' ""
check_turn "Copilot turns are told apart by folder" copilot userPromptSubmitted '{"timestamp":1,"cwd":"/work/api","prompt":"x"}' ""
check_turn "a short Copilot turn ends silently" copilot agentStop '{"timestamp":2,"cwd":"/work/api"}' ""
check_turn "another Copilot folder vibrates" copilot agentStop '{"timestamp":2,"cwd":"/work/web"}' completed
rm -rf "$turns"

# --- fish integration -------------------------------------------------------------

fish_bin=$(command -v fish 2>/dev/null || true)
if [ -n "$fish_bin" ] && command -v python3 >/dev/null 2>&1; then
  fh="$work/fish-home"
  mkdir -p "$fh/.config/fish"
  printf 'set -g MX_LONG_COMMAND_SECONDS 1\nsource "%s"\n' "$root/shell/mx.fish" > "$fh/.config/fish/config.fish"
  : > "$calls"
  env -i HOME="$fh" PATH="$fake:/usr/bin:/bin" MX_TEST_CALLS="$calls" TERM=xterm-256color TERM_PROGRAM=ghostty __CFBundleIdentifier=com.mitchellh.ghostty \
    python3 -I "$root/tests/shell/type.py" 2.2 'sleep 1.3; false' 'sleep 1.3' 'ls >/dev/null' 'tail -n 1 /dev/null; sleep 1.3' 'buzz true' -- "$fish_bin" -i > /dev/null 2>&1
  sleep 0.5
  fwaves=$(sed -n 's|.*/haptic/\([a-z_]*\)$|\1|p' "$calls" | tr '\n' ' ')
  # The failing long command, the long one, buzz. Not ls (short), not tail (ignored).
  if [ "$fwaves" = "angry_alert completed completed " ]; then ok; else ko "fish: long commands and buzz vibrate (got: $fwaves)" "$(cat "$calls")"; fi
  if grep -q -- '--resolve|local.jmw.nz:41443:127.0.0.1' "$calls"; then ok; else ko "fish: no DNS for the vibrations" "$(cat "$calls")"; fi
  if [ "$(cat "$fh/.config/mx-dev-kit/current-terminal" 2>/dev/null)" = "$(printf 'ghostty\tcom.mitchellh.ghostty')" ]; then ok; else ko "fish: the terminal is written at each prompt"; fi
  if [ -s "$fh/.config/mx-dev-kit/current-dir" ]; then ok; else ko "fish: the folder is written at each prompt"; fi
else
  printf 'skip  the fish integration (fish not installed)\n'
fi

# --- summary ------------------------------------------------------------------

printf '%s passed, %s failed\n' "$passed" "$failed"
[ "$failed" -eq 0 ]
