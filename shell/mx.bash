# MX Dev Kit: shell integration for bash (macOS, and Git Bash on Windows).
# Source it from ~/.bashrc (or ~/.bash_profile on macOS).
#
#   - tells the MX Dev Kit plugin which folder you are working in
#   - commands that run longer than MX_LONG_COMMAND_SECONDS vibrate when they end
#   - buzz <command>        vibrates when the command ends, whatever its duration
#   - ci-watch [sha]        waits for the GitHub Actions runs of a commit, vibrates at the end
#   - haptic, mx-notify, uptime-watch, docker-watch  added to PATH (see bin/)
#
# Settings, before the source line:
#   MX_LONG_COMMAND_SECONDS=30          0 turns long-command vibrations off
#   MX_LONG_COMMAND_IGNORE="vim ssh"    commands (first word) that never vibrate
#   MX_HAPTIC_URL=https://local.jmw.nz:41443/haptic
#
# Long-command vibrations use the DEBUG trap. With bash-preexec loaded, its hooks are used instead;
# if another tool already owns the DEBUG trap, that part is skipped and the rest still works.

[[ $- == *i* ]] || return 0

MX_DEV_KIT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
if [[ -n ${APPDATA-} && -d ${APPDATA-} ]]; then
  MX_STATE_DIR=${MX_STATE_DIR:-$APPDATA/mx-dev-kit}
else
  MX_STATE_DIR=${MX_STATE_DIR:-$HOME/.config/mx-dev-kit}
fi
MX_HAPTIC_URL=${MX_HAPTIC_URL:-https://local.jmw.nz:41443/haptic}
MX_LONG_COMMAND_SECONDS=${MX_LONG_COMMAND_SECONDS:-30}
MX_LONG_COMMAND_IGNORE=${MX_LONG_COMMAND_IGNORE:-"vim nvim vi nano emacs micro less more man ssh mosh tmux screen top htop btop watch tail claude gemini codex"}

[[ -d $MX_DEV_KIT_DIR/bin && ":$PATH:" != *":$MX_DEV_KIT_DIR/bin:"* ]] && PATH="$MX_DEV_KIT_DIR/bin:$PATH"
mkdir -p "$MX_STATE_DIR" 2>/dev/null

# Non-blocking: the prompt never waits for the mouse.
mx_haptic() {
  [[ -e $MX_STATE_DIR/haptics.off ]] && return 0
  (curl -s -m 2 -o /dev/null -X POST -d '' --resolve local.jmw.nz:41443:127.0.0.1 \
    "${MX_HAPTIC_URL%/}/$1" >/dev/null 2>&1 &)
}

buzz() {
  "$@"
  local code=$?
  _mx_buzzed=1
  if (( code == 0 )); then mx_haptic completed; else mx_haptic angry_alert; fi
  return $code
}

ci-watch() {
  if ! command -v gh >/dev/null; then
    echo "ci-watch: the GitHub CLI (gh) is not installed" >&2
    return 1
  fi

  local sha=${1:-$(git rev-parse HEAD 2>/dev/null)} i id failed=0 ids=""
  if [[ -z $sha ]]; then
    echo "ci-watch: not in a git repository" >&2
    return 1
  fi

  echo "Waiting for the workflow runs of ${sha:0:7}..."
  for i in {1..24}; do
    ids=$(gh run list --commit "$sha" --json databaseId -q '.[].databaseId' 2>/dev/null)
    [[ -n $ids ]] && break
    sleep 5
  done

  if [[ -z $ids ]]; then
    echo "ci-watch: no workflow run for ${sha:0:7} after two minutes" >&2
    return 1
  fi

  for id in $ids; do
    gh run watch "$id" --exit-status || failed=1
  done

  _mx_buzzed=1
  if (( failed )); then mx_haptic angry_alert; else mx_haptic happy_alert; fi
  return $failed
}

_mx_pwd() {
  # Git Bash: the plugin is a Windows program and needs C:/... rather than /c/...
  if [[ ${OSTYPE-} == msys* || ${OSTYPE-} == cygwin* ]]; then pwd -W; else printf '%s\n' "$PWD"; fi
}

_mx_preexec() {
  _mx_start=$SECONDS
  _mx_cmd=$1
}

_mx_precmd() {
  local code=$? buzzed=${_mx_buzzed-} start=${_mx_start-} cmd=${_mx_cmd-}
  unset _mx_buzzed _mx_start _mx_cmd _mx_armed

  _mx_pwd >| "$MX_STATE_DIR/current-dir" 2>/dev/null

  [[ -n $start && -z $buzzed ]] || return 0
  (( MX_LONG_COMMAND_SECONDS > 0 && SECONDS - start >= MX_LONG_COMMAND_SECONDS )) || return 0
  (( code == 130 || code == 148 )) && return 0   # Ctrl-C, Ctrl-Z

  local first=${cmd%% *}
  [[ " $MX_LONG_COMMAND_IGNORE " == *" $first "* ]] && return 0

  if (( code == 0 )); then mx_haptic completed; else mx_haptic angry_alert; fi
}

# Without bash-preexec: the DEBUG trap records the first command typed after each prompt.
_mx_debug() {
  [[ -n ${_mx_armed-} && -z ${COMP_LINE-} && $BASH_COMMAND != _mx_* ]] || return 0
  unset _mx_armed
  _mx_preexec "$BASH_COMMAND"
}

_mx_ready() {
  _mx_armed=1
}

if declare -p preexec_functions >/dev/null 2>&1 && declare -p precmd_functions >/dev/null 2>&1; then
  [[ " ${precmd_functions[*]} " == *" _mx_precmd "* ]] || precmd_functions=(_mx_precmd "${precmd_functions[@]}")
  [[ " ${preexec_functions[*]} " == *" _mx_preexec "* ]] || preexec_functions+=(_mx_preexec)
elif [[ ${PROMPT_COMMAND[*]-} != *_mx_precmd* ]]; then
  if [[ "$(declare -p PROMPT_COMMAND 2>/dev/null)" == "declare -a"* ]]; then
    PROMPT_COMMAND=(_mx_precmd "${PROMPT_COMMAND[@]}" _mx_ready)
  else
    PROMPT_COMMAND="_mx_precmd${PROMPT_COMMAND:+; $PROMPT_COMMAND}; _mx_ready"
  fi

  if [[ -z $(trap -p DEBUG) ]]; then
    trap '_mx_debug' DEBUG
  fi
fi
