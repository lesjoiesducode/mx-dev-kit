#!/usr/bin/env python3
"""Runs an installer the way a person would, for tests/shell/run.sh.

  answer.py <answers> <command...>   in a terminal, answering its [Y/n] questions in order:
                                  comma-separated, empty meaning Enter ("n,,n")
  answer.py --no-tty <command...>    with no terminal at all (new session, input from /dev/null)
"""
import os
import pty
import re
import subprocess
import sys

args = sys.argv[1:]
if args[0] == "--no-tty":
    sys.exit(subprocess.run(args[1:], stdin=subprocess.DEVNULL, start_new_session=True).returncode)

answers = args[0].split(",") if args[0] else []
pid, fd = pty.fork()
if pid == 0:
    os.execvp(args[1], args[1:])

seen = b""
answered = 0
while True:
    try:
        data = os.read(fd, 1024)
    except OSError:
        break
    if not data:
        break
    # PowerShell asks the terminal where the cursor is before it reads a line.
    for _ in range(data.count(b"\x1b[6n")):
        os.write(fd, b"\x1b[1;1R")
    data = data.replace(b"\x1b[6n", b"")
    sys.stdout.buffer.write(data.replace(b"\r\n", b"\n"))
    sys.stdout.flush()
    seen += data
    for _ in range(len(re.findall(rb"\[[Yy]/[Nn]\]:? ", seen)) - answered):
        answer = answers[answered] if answered < len(answers) else ""
        os.write(fd, answer.encode() + b"\n")
        answered += 1

_, status = os.waitpid(pid, 0)
sys.exit(os.waitstatus_to_exitcode(status))
