"""Wait until the Unity editor is out of Play mode and ready for tools.

The editor is often left in Play mode; reimports, compiles and MCP checks must
wait for edit mode. Polls the MCP for Unity editor state (via mcpcall.py next to
this file) until play mode is neither on nor changing and the server advises
ready_for_tools.

Usage:
  python tools/unity/wait_not_playing.py [--timeout SECONDS] [--interval SECONDS]

Prints "NOT_PLAYING after ~Ns" and exits 0, or prints "TIMEOUT: ..." with the
last state seen and exits 1 (default timeout 1500 s = 25 min). The last state
tells the two timeouts apart: "server unreachable" (editor closed, MCP server
down) is not the same as "still in Play mode". For long waits, run it in the
background (Bash run_in_background) instead of blocking the session.

Honours UNITY_MCP_URL / UNITY_MCP_SESSION_FILE (see mcpcall.py).
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # works under `python -I` too
import mcpcall  # noqa: E402


def describe(state):
    """One-word reason the editor is not ready yet."""
    if state is None:
        return "server unreachable or state unreadable"
    play = (state.get("editor") or {}).get("play_mode") or {}
    if play.get("is_playing"):
        return "in Play mode"
    if play.get("is_changing"):
        return "changing play mode"
    if not (state.get("advice") or {}).get("ready_for_tools"):
        return "busy (compiling/importing; not ready_for_tools)"
    return "ready"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--timeout", type=float, default=1500, help="seconds to wait (default 1500)")
    parser.add_argument("--interval", type=float, default=2, help="seconds between polls (default 2)")
    args = parser.parse_args()
    start, last = time.time(), "not polled"
    while time.time() - start < args.timeout:
        last = describe(mcpcall.editor_state(timeout=60))
        if last == "ready":
            print(f"NOT_PLAYING after ~{time.time() - start:.0f}s")
            return 0
        time.sleep(args.interval)
    print(f"TIMEOUT after {args.timeout:.0f}s: last state = {last}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
