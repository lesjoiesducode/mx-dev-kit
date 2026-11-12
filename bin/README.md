# Command-line tools

`haptic` and the watchers talk to the [Haptic Web Plugin](https://marketplace.logi.com/plugin/HapticWeb/en) and need an MX Master 4. `mx-notify` works on any Mac or PC. The shell integration puts this folder on your PATH.

## haptic

Plays one waveform. Handy at the end of any script, Makefile target or deploy job running on your machine.

```sh
haptic completed
haptic --list                    # the waveforms the plugin knows
./deploy.sh && haptic happy_alert || haptic angry_alert
```

Windows: `haptic.ps1 completed`, `haptic.ps1 -List`.

## mx-notify

Shows a system notification that says what happened, and opens a page when you click it. The plugin uses it for GitHub events, `docker-watch` for containers, and you can call it from any script:

```sh
mx-notify "Deploy done" "api 1.4.2 is live" https://example.com/status
./deploy.sh && haptic happy_alert || mx-notify "Deploy failed" "see the logs" https://ci.example.com
```

| | How | Click |
|---|---|---|
| macOS | [terminal-notifier](https://github.com/julienXX/terminal-notifier) when it is installed (`brew install terminal-notifier`, the installer offers it) | Opens the URL |
| macOS, without terminal-notifier | AppleScript `display notification` | Opens nothing: the GitHub inbox bubble has the link |
| Windows | Native toast through PowerShell (`Windows.UI.Notifications`), nothing to install | Opens the URL |
| Linux | `notify-send` | Opens nothing |

Windows: `mx-notify.ps1 "Deploy done" "api 1.4.2 is live" https://example.com/status` (from PowerShell 7 it hands the toast over to Windows PowerShell, which is the one that can show it). From Git Bash, `mx-notify` calls `mx-notify.ps1` for you.

| Setting | |
|---|---|
| `touch ~/.config/mx-dev-kit/notifications.off` | Mutes every notification of the kit (the plugin's too). Windows: `%APPDATA%\mx-dev-kit\notifications.off`. Vibrations are not affected. |
| `MX_NOTIFY_WITH` | Forces `terminal-notifier`, `osascript` or `notify-send` |
| `MX_NOTIFY_APP_ID` (Windows) | The app the toast is shown under. Default: Windows PowerShell. |

**No notification?** macOS: System Settings > Notifications, allow **terminal-notifier** (AppleScript notifications show under **Script Editor**), and check Focus / Do Not Disturb. Windows: Settings > System > Notifications, allow **Windows PowerShell**.

## uptime-watch

Watches websites, health endpoints and local ports. The mouse vibrates hard (`angry_alert`) when one goes down and softly (`happy_alert`) when it is back.

```sh
uptime-watch https://example.com http://localhost:3000/health localhost:5432
```

```
Watching 3 target(s) every 60s. Ctrl-C to stop.
09:41:02  ok    https://example.com (HTTP 200)
09:41:02  ok    http://localhost:3000/health (HTTP 200)
09:41:02  ok    localhost:5432 (port open)
09:42:02  fail  http://localhost:3000/health (no answer, 1/2)
09:43:02  DOWN  http://localhost:3000/health (no answer)
09:47:02  UP    http://localhost:3000/health (HTTP 200)
```

| Target | Up when |
|---|---|
| `http://...` or `https://...` | It answers 2xx or 3xx (redirects are followed) within the timeout |
| `host:port` | It accepts a TCP connection: a database, a dev server, a queue |

| Option | Default | |
|---|---|---|
| `-i SECONDS` | `60` | Time between two checks |
| `-t SECONDS` | `10` | Timeout of one check |
| `-n COUNT` | `2` | Failed checks in a row before a target is reported down, to avoid false alarms |
| `-q` | | Only print state changes |

Windows:

```powershell
uptime-watch.ps1 https://example.com localhost:5432 -Interval 60 -Timeout 10 -Failures 2 -Quiet
```

### Keep it running in the background

macOS, simplest:

```sh
nohup uptime-watch -q https://example.com >> ~/uptime-watch.log 2>&1 &
```

To start it at login, save this as `~/Library/LaunchAgents/com.example.uptime-watch.plist` (adjust the path and targets), then `launchctl load ~/Library/LaunchAgents/com.example.uptime-watch.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.example.uptime-watch</string>
  <key>ProgramArguments</key>
  <array>
    <string>/Users/you/.config/mx-dev-kit/bin/uptime-watch</string>
    <string>-q</string>
    <string>https://example.com</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>/tmp/uptime-watch.log</string>
  <key>StandardErrorPath</key><string>/tmp/uptime-watch.log</string>
</dict>
</plist>
```

Windows: create a task in Task Scheduler that runs at logon:
`powershell -NoProfile -WindowStyle Hidden -File "%APPDATA%\mx-dev-kit\bin\uptime-watch.ps1" https://example.com -Quiet`.

## docker-watch

The database or the worker of your `docker compose` stack falls over while you code, and you only find out at the first 500. `docker-watch` taps you as soon as it happens, and says which container and why in a notification.

```sh
cd ~/code/shop && docker-watch
```

```
Watching the containers of the compose project in /Users/you/code/shop (--all for every container). Ctrl-C to stop.
09:41:02  DIED     shop-db-1 (exit code 1)
09:41:40  stopped  shop-web-1 (on purpose)
09:42:15  OOM      shop-worker-1 (out of memory)
09:44:03  DIED     shop-api-1 (exit code 1)
09:44:13  died     shop-api-1 (exit code 1, again)
09:44:24  LOOP     shop-api-1 (died 3 times in 60 s, quiet for 10 min)
09:47:30  unhealthy shop-cache-1
09:48:00  HEALTHY  shop-cache-1 (healthy again)
```

| What happens | Vibration | Notification |
|---|---|---|
| A container exits with a non-zero code, or runs out of memory | `angry_alert` | "shop-db-1 died: Exit code 1, service db of shop." |
| It dies 3 times within 60 s (a restart policy looping) | `angry_alert`, once, then quiet for 10 minutes | "shop-api-1 restarts in a loop" |
| A container that was unhealthy passes its healthcheck again | `happy_alert` | "shop-cache-1 is healthy again" |
| A container stopped on purpose (`docker stop`, `docker compose down`, `docker kill`: a stop or kill in the 5 s before it exits), or that exits with 0 | nothing | nothing |

It only reads the `docker events` stream: no setting, no polling, the same on Docker Desktop for macOS and Windows (and on Linux, without vibration). Started in a folder with a compose file (`compose.yaml`, `docker-compose.yml`...), or below one up to the git root, it only follows the containers of that project, so the stack of another project does not wake you up. Elsewhere it follows every container. When Docker Desktop quits or restarts, it waits and reconnects on its own.

| Option | |
|---|---|
| `--all` | Every container, wherever it is started |
| `-p NAME` | The containers of one compose project (`docker compose ls` gives the names) |
| `-q` | Only print what vibrates |

Windows: `docker-watch.ps1`, `docker-watch.ps1 -All`, `docker-watch.ps1 -Project shop -Quiet`.

To keep it running in the background, the same recipes as `uptime-watch` above work: `nohup docker-watch --all -q >> ~/docker-watch.log 2>&1 &`, a launchd agent with `docker-watch` and `--all` in `ProgramArguments`, or a Task Scheduler task running `docker-watch.ps1 -All -Quiet` at logon.

| Symptom | What to do |
|---|---|
| "docker not found" | Install Docker Desktop, or put the `docker` CLI on your PATH. In a launchd agent, give the full path in an `EnvironmentVariables` `PATH` (`/usr/local/bin` for Docker Desktop). |
| "Docker is not running: waiting for it..." | Start Docker Desktop: it picks up on its own. |
| Nothing for a container that clearly died | Started in a compose project folder, it ignores the other projects: use `--all`. A container stopped within 5 s of a `docker stop`/`kill` counts as stopped on purpose. Exit code 0 never vibrates. |
| No healthy-again vibration | Only containers with a `healthcheck` report health, and only a change from unhealthy to healthy vibrates. |
| Podman | Podman has the same `docker events` stream, but it has not been tested. |
