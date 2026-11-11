#!/usr/bin/env python3
"""Types commands into an interactive shell in a terminal, for tests/shell/run.sh.

  type.py <seconds> <line> [<line>...] -- <command...>

Each line is typed, then the shell gets <seconds> to run it before the next one; `exit` comes last.
Terminal queries (primary device attributes, cursor position) get an answer, as a terminal would.
"""
import os
import pty
import select
import sys
import time

args = sys.argv[1:]
pause = float(args[0])
split = args.index("--")
lines, command = args[1:split], args[split + 1:]

pid, fd = pty.fork()
if pid == 0:
    os.execvp(command[0], command)


def pump(seconds):
    end = time.time() + seconds
    while time.time() < end:
        ready, _, _ = select.select([fd], [], [], 0.05)
        if not ready:
            continue
        try:
            data = os.read(fd, 4096)
        except OSError:
            return
        if not data:
            return
        if b"\x1b[c" in data or b"\x1b[0c" in data:
            os.write(fd, b"\x1b[?62;22c")
        for _ in range(data.count(b"\x1b[6n")):
            os.write(fd, b"\x1b[1;1R")


pump(2)
for line in lines + ["exit"]:
    os.write(fd, line.encode() + b"\r")
    pump(pause)

try:
    _, status = os.waitpid(pid, 0)
    sys.exit(os.waitstatus_to_exitcode(status))
except ChildProcessError:
    sys.exit(0)
