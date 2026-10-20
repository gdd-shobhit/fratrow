"""Wait until a TCP port accepts connections.

Use after launching the Unity editor (MCP for Unity listens on 8080 by default)
or Blender to know when its MCP server is up, before calling mcpcall.py.

Usage:
  python tools/unity/wait_port.py <port> [timeout_seconds] [--host HOST]
  python tools/unity/wait_port.py 8080 180

Prints "port N up after Ns" and exits 0, or prints "TIMEOUT: ..." and exits 1
(default timeout 120 s, host 127.0.0.1); exits 2 on bad usage.
"""
import argparse
import socket
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("port", type=int)
    parser.add_argument("timeout", type=float, nargs="?", default=120, help="seconds (default 120)")
    parser.add_argument("--host", default="127.0.0.1")
    args = parser.parse_args()
    start = time.time()
    while time.time() - start < args.timeout:
        try:
            with socket.create_connection((args.host, args.port), timeout=2):
                print(f"port {args.port} up after {time.time() - start:.0f}s")
                return 0
        except OSError:
            time.sleep(2)
    print(f"TIMEOUT: {args.host}:{args.port} not listening after {args.timeout:.0f}s")
    return 1


if __name__ == "__main__":
    sys.exit(main())
