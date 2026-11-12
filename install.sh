#!/bin/sh
# MX Dev Kit installer for macOS. Needs nothing that macOS does not ship: sh, curl,
# osascript. No git clone, no .NET, no Homebrew.
#
#   curl -fsSL https://github.com/lesjoiesducode/mx-dev-kit/releases/latest/download/install.sh | sh
#   ./install.sh                 the same, from the release archive or a clone
#
# It asks about each part, Enter meaning yes: the Actions Ring plugin, the coding agent hooks,
# the terminal integration. The haptic command and the tools of bin/ come with either of the
# last two, and so does the offer to install the Haptic Web Plugin, which their vibrations need.
# Without a terminal to ask, it installs everything.
#
#   --only <parts>    install these parts without asking: plugin, hooks, shell, comma-separated
#                     (--only hooks,shell)
#   --yes, -y         install everything without asking
#   --no-haptic-web   do not install the Haptic Web Plugin, and do not ask about it
#   --from-source     build the plugin from this clone instead (contributors: .NET 10 SDK, LogiPluginTool)
#   --link            build and link the plugin from this clone, for development (.NET 10 SDK)
#
# The plugin is MxDevKit.lplug4: the one next to this script (release archive), or the one of the
# latest release. It goes to the plugins folder of Logi Plugin Service, which then loads it.
# The Haptic Web Plugin (github.com/Fallstop/HapticWebPlugin, MIT) comes from its latest release,
# the same way, unless it is already installed: an existing one is never replaced.
# The terminal integration goes where your shell reads it: ${ZDOTDIR:-~}/.zshrc for zsh, ~/.bash_profile
# (or the ~/.bash_login or ~/.profile you already have) for bash, a file of its own in
# ~/.config/fish/conf.d for fish. Any other shell: it says so and adds nothing.
# Every file it changes (that profile, agent settings.json) is backed up first.
# ~/.config/mx-dev-kit/uninstall.sh reverses it.

set -eu

repo=lesjoiesducode/mx-dev-kit
release=${MX_DEV_KIT_RELEASE_URL:-https://github.com/$repo/releases/latest/download}
state="$HOME/.config/mx-dev-kit"
marker="# >>> mx-dev-kit >>>"

say() { printf '%s\n' "$*"; }
step() { printf '\n== %s\n' "$*"; }

if [ "$(uname -s)" != Darwin ]; then
  say "This installer is for macOS. On Windows, run install.ps1 in PowerShell." >&2
  exit 1
fi

# fetch <name> <file>: one file of the release, checked against its SHA256SUMS.
fetch() {
  curl -fsSL --retry 2 -o "$2" "$release/$1" || { say "Could not download $release/$1" >&2; return 1; }
  sums=$(curl -fsSL --retry 2 "$release/SHA256SUMS") || { say "Could not download $release/SHA256SUMS" >&2; return 1; }
  expected=$(printf '%s\n' "$sums" | awk -v f="$1" '$2 == f || $2 == "*" f { print $1 }')
  actual=$(shasum -a 256 "$2" | awk '{ print $1 }')
  if [ -z "$expected" ] || [ "$expected" != "$actual" ]; then
    say "$1: checksum mismatch, download refused." >&2
    rm -f "$2"
    return 1
  fi
}

# Run through curl | sh, this script is alone: get the rest of the kit from the release archive.
if [ ! -f "$0" ] || [ ! -f "$(dirname "$0")/hooks/install.sh" ]; then
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  # Downloaded next to the release archive: use it rather than the network.
  if [ -f "$0" ] && [ -f "$(dirname "$0")/mx-dev-kit.zip" ]; then
    cp "$(dirname "$0")/mx-dev-kit.zip" "$tmp/kit.zip"
  else
    say "Downloading MX Dev Kit from $release..."
    fetch mx-dev-kit.zip "$tmp/kit.zip"
  fi
  (cd "$tmp" && unzip -q kit.zip)
  sh "$tmp/mx-dev-kit/install.sh" "$@"
  exit
fi

root=$(cd "$(dirname "$0")" && pwd)

plugin=0 shell=0 hooks=0 only="" yes=0 source=0 link=0 haptic_web=1
while [ $# -gt 0 ]; do
  case $1 in
    --only) only="$only,${2:?--only needs plugin, hooks or shell}"; shift ;;
    --only=*) only="$only,${1#--only=}" ;;
    --yes | -y) yes=1 ;;
    --from-source) source=1 ;;
    --link) source=1 link=1 ;;
    --no-haptic-web) haptic_web=0 ;;
    -h | --help) sed -n '2,28s/^# \{0,1\}//p' "$0"; exit 0 ;;
    *) say "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done
for part in $(printf '%s' "$only" | tr ',' ' '); do
  case $part in
    plugin) plugin=1 ;;
    hooks) hooks=1 ;;
    shell) shell=1 ;;
    *) say "Unknown part: $part (--only takes plugin, hooks or shell)" >&2; exit 2 ;;
  esac
done

if [ -f "$root/VERSION" ]; then
  version=$(cat "$root/VERSION")
else
  version=$(sed -n 's/^version: *//p' "$root/src/MxDevKitPlugin/package/metadata/LoupedeckPackage.yaml" 2>/dev/null || true)
fi

if [ "$source" = 1 ] && [ ! -d "$root/src/MxDevKitPlugin" ]; then
  say "--from-source and --link build the plugin from a clone: git clone https://github.com/$repo.git" >&2
  exit 2
fi

# Through curl | sh, standard input is the script: questions are read from the terminal.
ask() {
  printf '%s' "$1"
  if [ -t 0 ]; then
    read -r answer || answer=n
  else
    read -r answer </dev/tty || answer=n
  fi
}
# ask_part <question>: part=1 unless the answer starts with n.
ask_part() {
  ask "$1 [Y/n] "
  case $answer in [nN]*) part=0 ;; *) part=1 ;; esac
}
asks=1
[ -z "$only" ] && [ "$yes" = 0 ] || asks=0
if [ "$asks" = 1 ] && [ ! -t 0 ] && ! (: </dev/tty) 2>/dev/null; then
  asks=0 yes=1
  no_terminal=1
fi

backup() {
  if [ -e "$1" ]; then
    b="$1.bak-$(date +%Y%m%d-%H%M%S)"
    n=1
    while [ -e "$b" ]; do n=$((n + 1)); b="$1.bak-$(date +%Y%m%d-%H%M%S)-$n"; done
    cp -p "$1" "$b"
    say "Backup: $b"
  fi
}

# The kit used to be named Ring Dev Kit and lived in ~/.config/actions-ring-dev-kit: its own
# uninstaller removes it (plugin, shell line, hooks), and the settings move here.
migrate_legacy() {
  legacy="$HOME/.config/actions-ring-dev-kit"
  [ -d "$legacy" ] || return 0
  [ ! -f "$legacy/uninstall.sh" ] || sh "$legacy/uninstall.sh" >/dev/null 2>&1 || true
  mkdir -p "$state"
  for f in config.json haptics.off notifications.off; do
    [ ! -e "$legacy/$f" ] || [ -e "$state/$f" ] || mv "$legacy/$f" "$state/$f"
  done
  rmdir "$legacy" 2>/dev/null || true
  say "Replaced Ring Dev Kit, the former name of this kit (your settings moved to $state)."
}

# The shell your terminal opens: $SHELL, else the one of your account.
login_shell=${SHELL:-}
[ -n "$login_shell" ] || login_shell=$(dscl . -read "/Users/$(id -un)" UserShell 2>/dev/null | awk '{ print $2 }')
shell_name=$(basename "${login_shell:-/bin/zsh}")
fish_dir="${XDG_CONFIG_HOME:-$HOME/.config}/fish/conf.d"

# zsh reads ${ZDOTDIR:-$HOME}/.zshrc. ZDOTDIR is often set in ~/.zshenv without export: ask zsh.
zsh_rc() {
  zdot=${ZDOTDIR:-}
  if [ -z "$zdot" ] && command -v zsh >/dev/null 2>&1; then
    zdot=$(zsh -c 'printf "%s\n" "${ZDOTDIR:-}"' </dev/null 2>/dev/null | tail -n 1)
  fi
  [ -n "$zdot" ] && [ -d "$zdot" ] || zdot=$HOME
  printf '%s/.zshrc' "$zdot"
}

# The login bash of macOS reads the first of these that exists, and never ~/.bashrc on its own.
bash_rc() {
  for f in "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.profile"; do
    [ -e "$f" ] && { printf '%s' "$f"; return; }
  done
  printf '%s' "$HOME/.bash_profile"
}

rc="" shell_file=""
case $shell_name in
  zsh) rc=$(zsh_rc); shell_file=mx.zsh ;;
  bash) rc=$(bash_rc); shell_file=mx.bash ;;
  fish) rc="$fish_dir/mx-dev-kit.fish"; shell_file=mx.fish ;;
esac

service="$HOME/Library/Application Support/Logi/LogiPluginService"
plugins="$service/Plugins"
haptic_web_url=${MX_HAPTIC_WEB_URL:-https://github.com/Fallstop/HapticWebPlugin/releases/latest/download/HapticWeb.lplug4}
logi_installed() { [ -d /Applications/Utilities/LogiPluginService.app ] || [ -d "$service" ]; }

say "MX Dev Kit ${version:-(latest release)}"
if [ -z "$only" ] && [ "$asks" = 0 ]; then
  plugin=1 hooks=1 shell=1
  [ -z "${no_terminal:-}" ] || say "No terminal to ask questions: installing everything (--only picks parts)."
elif [ "$asks" = 1 ]; then
  ask_part 'Actions Ring bubbles (Options+ plugin)?'; plugin=$part
  ask_part 'Coding agent haptics (Claude Code, Codex, Gemini, Copilot)?'; hooks=$part
  ask_part 'Terminal integration (long commands vibrate, bubbles follow your prompt)?'; shell=$part
fi
tools=0
[ "$hooks" = 0 ] && [ "$shell" = 0 ] || tools=1
if [ "$plugin" = 0 ] && [ "$tools" = 0 ]; then
  say "Nothing to install."
  exit 0
fi
# The vibrations of the hooks, the shell and the scripts go through the Haptic Web Plugin.
[ "$tools" = 1 ] || haptic_web=0
if [ "$haptic_web" = 1 ] && [ "$asks" = 1 ] && [ ! -d "$plugins/HapticWeb" ] && logi_installed; then
  ask_part 'Haptic Web Plugin by Jasper M-W (MIT license), for these vibrations: install it from github.com/Fallstop/HapticWebPlugin?'
  haptic_web=$part
fi

migrate_legacy

step "Files"
say "In $state"
mkdir -p "$state/hooks"
if [ "$tools" = 1 ]; then
  mkdir -p "$state/bin" "$state/shell"
  cp "$root"/shell/mx.zsh "$root"/shell/mx.bash "$root"/shell/mx.fish "$root"/shell/mx.ps1 "$state/shell/"
  cp "$root"/bin/haptic "$root"/bin/mx-notify "$root"/bin/uptime-watch "$root"/bin/docker-watch "$root"/bin/*.ps1 "$state/bin/"
  chmod +x "$state/bin/haptic" "$state/bin/mx-notify" "$state/bin/uptime-watch" "$state/bin/docker-watch"
  say "haptic, mx-notify, uptime-watch and docker-watch in $state/bin"
fi
# The uninstaller stays here, so that it works once this folder is gone.
cp "$root/uninstall.sh" "$state/uninstall.sh"
cp "$root/hooks/uninstall.sh" "$root/hooks/settings-json.sh" "$root/hooks/settings-json.js" "$state/hooks/"
chmod +x "$state/uninstall.sh"
cp "$root/config.example.json" "$state/config.example.json"
if [ -e "$state/config.json" ]; then
  say "Kept your config.json"
else
  cp "$root/config.example.json" "$state/config.json"
  say "Created $state/config.json"
fi

if [ "$shell" = 1 ]; then
  step "Shell"
  line_for() { printf '[ -f "%s" ] && . "%s"' "$state/shell/$1" "$state/shell/$1"; }
  if [ -z "$shell_file" ]; then
    # Never "Added" for a shell that would not load it.
    say "Your shell is $shell_name: MX Dev Kit has no integration for it, nothing was added."
    say "The rest of the kit works without it. If you also use zsh, bash or fish, add this line to its startup file:"
    say "  zsh (~/.zshrc):          $(line_for mx.zsh)"
    say "  bash (~/.bash_profile):  $(line_for mx.bash)"
    say "  fish (~/.config/fish/conf.d/mx-dev-kit.fish):  source \"$state/shell/mx.fish\""
  elif [ "$shell_file" = mx.fish ]; then
    # A file of its own in conf.d, which fish loads at startup: nothing of yours is edited.
    fish_file=$(printf '%s\n# Written by the MX Dev Kit installer, deleted by its uninstaller.\ntest -f "%s"; and source "%s"\n%s' \
      "$marker" "$state/shell/mx.fish" "$state/shell/mx.fish" "# <<< mx-dev-kit <<<")
    if [ -f "$rc" ] && [ "$(cat "$rc")" = "$fish_file" ]; then
      say "Already in $rc"
    else
      mkdir -p "$fish_dir"
      printf '%s\n' "$fish_file" > "$rc"
      say "Added $rc (open a new terminal to use it)"
    fi
  elif [ -e "$rc" ] && grep -qF "$marker" "$rc"; then
    say "Already in $rc"
  else
    backup "$rc"
    {
      printf '\n%s\n' "$marker"
      if [ "$shell_file" = mx.bash ]; then
        # ~/.profile is also read by other shells: only bash loads it.
        printf '[ -n "${BASH_VERSION:-}" ] && %s\n' "$(line_for mx.bash)"
      else
        printf '%s\n' "$(line_for "$shell_file")"
      fi
      printf '%s\n' "# <<< mx-dev-kit <<<"
    } >> "$rc"
    say "Added to $rc (open a new terminal to use it)"
  fi
fi

if [ "$hooks" = 1 ]; then
  step "Coding agent hooks"
  sh "$root/hooks/install.sh" || say "Hooks skipped (see the message above)."
fi

installed_version() {
  sed -n 's/^version: *//p' "$plugins/MxDevKit/metadata/LoupedeckPackage.yaml" 2>/dev/null | tr -d '\r'
}

# Puts the plugin in place as Logi Plugin Service does (a .lplug4 is a zip of the plugin folder),
# then asks the service to load it. A double-click on the .lplug4 does the same, but only while
# the plugins manager of Logi Options+ is open, and after a confirmation in it.
install_package() {
  package_version=$(unzip -p "$1" metadata/LoupedeckPackage.yaml 2>/dev/null | sed -n 's/^version: *//p' | tr -d '\r')
  if [ -z "$package_version" ]; then
    say "$1 is not a Logi plugin package." >&2
    return 1
  fi
  if [ "$(installed_version)" = "$package_version" ]; then
    say "MX Dev Kit $package_version is already installed."
    return 0
  fi
  if [ ! -d /Applications/Utilities/LogiPluginService.app ] && [ ! -d "$service" ]; then
    say "Logi Options+ is not installed: install it (https://www.logitech.com/software/logi-options-plus.html), then run this again."
    return 0
  fi
  unpack="$state/plugin.new"
  rm -rf "$unpack" "$state/plugin.old"
  mkdir -p "$unpack" "$plugins"
  unzip -q "$1" -d "$unpack" || return 1
  [ -f "$unpack/metadata/LoupedeckPackage.yaml" ] && [ -f "$unpack/bin/MxDevKitPlugin.dll" ] || { say "$1 is incomplete." >&2; rm -rf "$unpack"; return 1; }
  [ ! -d "$plugins/MxDevKit" ] || mv "$plugins/MxDevKit" "$state/plugin.old" || return 1
  mv "$unpack" "$plugins/MxDevKit" || return 1
  rm -rf "$state/plugin.old"
  say "Installed MX Dev Kit $package_version in $plugins/MxDevKit"

  if load_plugin MxDevKit "$package_version"; then
    say "Logi Plugin Service loaded it."
  else
    say "If \"MX Dev Kit\" does not show in Logi Options+, restart Logi Plugin Service (Options+ settings)."
  fi
}

# load_plugin <name> <version>: asks Logi Plugin Service to load the plugin, then waits for its log
# to say so. Returns 1 when the service refused it, 2 when it said nothing in time.
load_plugin() {
  started=$(date +%Y-%m-%dT%H-%M-%S)
  open -g "loupedeck://plugin/$1/reload" 2>/dev/null || true
  log="$service/Logs/plugin_logs/$1.log"
  i=0
  while [ "$i" -lt "${MX_DEV_KIT_INSTALL_WAIT:-20}" ]; do
    if [ -f "$log" ]; then
      result=$(awk -v t="$started" -v want="Plugin '$1' version '$2' loaded" -v refused="Cannot load plugin from '$plugins/$1'" '
        $1 < t { next }
        index($0, want) || index($0, "is already loaded") { r = "loaded" }
        index($0, refused) && !index($0, "is already loaded") { r = "refused" }
        END { print r }' "$log")
      [ "$result" != loaded ] || return 0
      [ "$result" != refused ] || return 1
    fi
    sleep 1
    i=$((i + 1))
  done
  return 2
}

# The Haptic Web Plugin, from its latest release. Never replaces one that is already there
# (from the Marketplace or elsewhere). Returns 1, leaving nothing behind, when it is not installed.
install_haptic_web() {
  if [ -d "$plugins/HapticWeb" ]; then
    say "Already installed ($plugins/HapticWeb): left as it is."
    return 0
  fi
  if ! logi_installed; then
    say "Logi Options+ is not installed: install it (https://www.logitech.com/software/logi-options-plus.html), then run this again."
    return 1
  fi
  hw_file="$state/HapticWeb.lplug4.part"
  hw_unpack="$state/haptic-web.new"
  rm -rf "$hw_file" "$hw_unpack"
  say "Downloading HapticWeb.lplug4 from $haptic_web_url..."
  if ! curl -fsSL --retry 2 -o "$hw_file" "$haptic_web_url"; then
    say "Could not download it."
    rm -f "$hw_file"
    return 1
  fi
  hw_yaml=$(unzip -p "$hw_file" metadata/LoupedeckPackage.yaml 2>/dev/null | tr -d '\r')
  hw_name=$(printf '%s\n' "$hw_yaml" | sed -n 's/^name: *//p')
  hw_version=$(printf '%s\n' "$hw_yaml" | sed -n 's/^version: *//p')
  if [ "$hw_name" != HapticWeb ]; then
    say "The download is not the Haptic Web Plugin package (name: ${hw_name:-none}): not installed."
    rm -f "$hw_file"
    return 1
  fi
  mkdir -p "$hw_unpack" "$plugins"
  if ! unzip -q "$hw_file" -d "$hw_unpack" || [ ! -f "$hw_unpack/metadata/LoupedeckPackage.yaml" ] || [ ! -d "$hw_unpack/bin" ]; then
    say "The package is incomplete: not installed."
    rm -rf "$hw_file" "$hw_unpack"
    return 1
  fi
  rm -f "$hw_file"
  mv "$hw_unpack" "$plugins/HapticWeb" || { rm -rf "$hw_unpack"; return 1; }
  say "Installed Haptic Web Plugin $hw_version in $plugins/HapticWeb"
  load_plugin HapticWeb "$hw_version"
  case $? in
    0) say "Logi Plugin Service loaded it." ;;
    1)
      say "Logi Plugin Service refused to load it: removed."
      rm -rf "$plugins/HapticWeb"
      return 1
      ;;
    *) say "Logi Plugin Service has not loaded it yet: if haptic completed does not vibrate, restart Logi Plugin Service (Options+ settings)." ;;
  esac
}

if [ "$plugin" = 1 ]; then
  step "Plugin"
  if [ "$source" = 1 ]; then
    # Homebrew's .NET needs DOTNET_ROOT for global tools such as LogiPluginTool.
    if [ -z "${DOTNET_ROOT:-}" ] && command -v brew >/dev/null 2>&1 && [ -d "$(brew --prefix)/opt/dotnet/libexec" ]; then
      export DOTNET_ROOT="$(brew --prefix)/opt/dotnet/libexec"
    fi
    PATH="$PATH:$HOME/.dotnet/tools"
    if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
      say "Building from source needs the .NET 10 SDK: brew install dotnet. Without it, run ./install.sh: it installs the prebuilt plugin."
      exit 1
    fi
    if [ "$link" = 1 ]; then
      dotnet build "$root/src/MxDevKitPlugin" -c Debug -p:LinkPlugin=true -nologo -v q
      say "Linked. Rebuild with: dotnet build src/MxDevKitPlugin -p:LinkPlugin=true"
    else
      if ! command -v logiplugintool >/dev/null 2>&1; then
        say "Installing LogiPluginTool (the packaging tool of the Logi Actions SDK)..."
        dotnet tool install --global LogiPluginTool
      fi
      dotnet build "$root/src/MxDevKitPlugin" -c Release -nologo -v q
      mkdir -p "$root/dist"
      package="$root/dist/MxDevKit.lplug4"
      rm -f "$package"
      logiplugintool pack "$root/src/MxDevKitPlugin/bin/Release" "$package"
      logiplugintool verify "$package"
      logiplugintool install "$package" || install_package "$package"
    fi
  else
    if [ -e "$plugins/MxDevKitPlugin.link" ]; then
      say "Note: a development link (MxDevKitPlugin.link) is in $plugins. Delete it to use the installed plugin."
    fi
    # Kept in the state folder: Logi Plugin Service reads it after this script is gone.
    got=1
    if [ -f "$root/MxDevKit.lplug4" ]; then
      cp "$root/MxDevKit.lplug4" "$state/MxDevKit.lplug4"
    else
      say "Downloading MxDevKit.lplug4 from $release..."
      if fetch MxDevKit.lplug4 "$state/MxDevKit.lplug4.part"; then
        mv "$state/MxDevKit.lplug4.part" "$state/MxDevKit.lplug4"
      else
        got=0
        say "No plugin installed: download MxDevKit.lplug4 from https://github.com/$repo/releases (see the README)."
      fi
    fi
    [ "$got" = 0 ] || install_package "$state/MxDevKit.lplug4" || say "No plugin installed: see the message above."
  fi
fi

haptic_web_ready=0
if [ "$tools" = 1 ]; then
  step "Haptic Web Plugin"
  if [ -d "$plugins/HapticWeb" ]; then
    say "Already installed ($plugins/HapticWeb): left as it is."
    haptic_web_ready=1
  elif [ "$haptic_web" = 0 ]; then
    say "Not installed (your choice)."
  elif install_haptic_web; then
    haptic_web_ready=1
  fi
fi

# GitHub CLI runs the GitHub bubbles, the GitHub inbox and ci-watch. Optional: offered, never required.
if [ "$plugin" = 1 ] || [ "$tools" = 1 ]; then
  step "GitHub"
  gh_bin=$(command -v gh 2>/dev/null || true)
  if [ -z "$gh_bin" ]; then
    if [ "$asks" = 1 ] && command -v brew >/dev/null 2>&1; then
      ask 'GitHub CLI (gh) runs the GitHub bubbles, the GitHub inbox and ci-watch: install it with Homebrew? [Y/n] '
      case $answer in
        [nN]*) say "Skipped: the GitHub bubbles stay off until gh is installed (brew install gh) and signed in." ;;
        *)
          if brew install gh; then
            gh_bin=$(command -v gh 2>/dev/null || true)
            [ -n "$gh_bin" ] || gh_bin="$(brew --prefix 2>/dev/null)/bin/gh"
            [ -x "$gh_bin" ] || gh_bin=""
          fi
          [ -n "$gh_bin" ] || say "brew install gh failed: the GitHub bubbles stay off until gh is installed."
          ;;
      esac
    else
      say "GitHub CLI not found: the GitHub bubbles, the GitHub inbox and ci-watch stay off."
      say "Install it from https://cli.github.com (brew install gh), then run gh auth login."
    fi
  fi
  # Through curl | sh, gh asks its questions on the terminal too.
  gh_login() { if [ -t 0 ]; then "$gh_bin" auth login; else "$gh_bin" auth login </dev/tty; fi; }
  if [ -n "$gh_bin" ]; then
    # gh auth token reads the stored credentials only (no network, no prompt); older gh: auth status.
    if "$gh_bin" auth token >/dev/null 2>&1 || "$gh_bin" auth status >/dev/null 2>&1; then
      say "GitHub CLI signed in: the GitHub bubbles are on."
    elif [ "$asks" = 1 ]; then
      ask 'Sign in to GitHub now with gh auth login, for the GitHub bubbles? [Y/n] '
      case $answer in
        [nN]*) say "Skipped: run gh auth login later to turn the GitHub bubbles on." ;;
        *)
          if gh_login; then
            say "Signed in: the GitHub bubbles are on."
          else
            say "gh auth login did not finish: run it later to turn the GitHub bubbles on."
          fi
          ;;
      esac
    else
      say "GitHub CLI found but not signed in: run gh auth login to turn the GitHub bubbles on."
    fi
  fi
fi

step "Notifications"
if command -v terminal-notifier >/dev/null 2>&1 || [ -x /opt/homebrew/bin/terminal-notifier ] || [ -x /usr/local/bin/terminal-notifier ]; then
  say "terminal-notifier found: a click on a notification opens the page."
elif [ "$asks" = 0 ] || ! command -v brew >/dev/null 2>&1; then
  say "Notifications work as they are (AppleScript); a click on them opens nothing."
  say "Optional, for clickable ones: terminal-notifier (https://github.com/julienXX/terminal-notifier, brew install terminal-notifier)."
else
  ask 'Optional: install terminal-notifier with Homebrew, so that a click on a notification opens the page? [y/N] '
  case $answer in
    [yY]*) brew install terminal-notifier || say "brew install terminal-notifier failed: notifications go through AppleScript." ;;
    *) say "Skipped: notifications go through AppleScript (a click opens nothing)." ;;
  esac
fi

step "Next steps"
n=0
if [ "$tools" = 1 ]; then
  n=$((n + 1))
  if [ "$haptic_web_ready" = 1 ]; then
    say "$n. MX Master 4: test the vibrations with $state/bin/haptic completed"
  else
    say "$n. MX Master 4: in Logi Options+, open the Marketplace and install the Haptic Web Plugin, for the vibrations."
    say "   Test it: $state/bin/haptic completed"
  fi
fi
if [ "$plugin" = 1 ]; then
  n=$((n + 1))
  say "$n. Logi Options+ > your device > Actions Ring: drag actions from \"MX Dev Kit\" onto the ring."
  say "   If a bubble shows only its name, edit its icon once and reset it to see the live image."
fi
say "$((n + 1)). Optional tools, each for its own feature: osv-scanner (vulnerabilities for every stack),"
say "   docker (docker-watch). Nothing breaks without them."
say "Uninstall: $state/uninstall.sh"
