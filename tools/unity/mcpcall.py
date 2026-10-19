"""Minimal MCP (streamable HTTP) client for the MCP for Unity server in an open editor.

Use it when the Unity editor is open on this project and its MCP server is
listening (default http://127.0.0.1:8080/mcp). When the editor is closed, use
tools/unity/unity_batch.py instead. See tools/unity/README.md.

Usage:
  python tools/unity/mcpcall.py list                         # tool names + first line of each description
  python tools/unity/mcpcall.py schema <tool>                # one tool's description and input schema
  python tools/unity/mcpcall.py read <resource-uri>          # e.g. mcpforunity://editor/state
  python tools/unity/mcpcall.py call <tool> ['<json args>']  # call a tool, print its text content
  python tools/unity/mcpcall.py call <tool> @args.json       # same, arguments read from a JSON file
  python tools/unity/mcpcall.py code <file.cs>               # run a C# method body in the editor (execute_code)
  python tools/unity/mcpcall.py compile                      # force refresh + compile, wait, print console errors

A .cs file for `code` is the body of a method that returns a value, e.g.

    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Characters/Female/SK_Female_Base.fbx");
    return $"verts={mesh.vertexCount}";

UnityEngine and UnityEditor names resolve unqualified; fully qualify anything
else (System.Linq.Enumerable.First(...), System.Collections.Generic.List<T>).
Keep such files outside Assets/ (Unity would try to compile them).

Environment:
  UNITY_MCP_URL (or MCP_URL)  server endpoint, default http://127.0.0.1:8080/mcp
  UNITY_MCP_SESSION_FILE      where the MCP session id is cached between runs;
                              default <system temp>/unity_mcp_session_<port>.txt

Exit status: 0 on success; 1 when the server is unreachable, a request fails,
the tool reports isError, or `compile` times out; 2 on bad usage.
"""
import json
import os
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

URL = os.environ.get("UNITY_MCP_URL") or os.environ.get("MCP_URL") or "http://127.0.0.1:8080/mcp"
PROTOCOL = "2025-06-18"
STATE_URI = "mcpforunity://editor/state"
COMPILE_POLL_S = 2
COMPILE_TIMEOUT_S = 400


def _default_session_file():
    port = urllib.parse.urlparse(URL).port or 80
    return os.path.join(tempfile.gettempdir(), f"unity_mcp_session_{port}.txt")


SESSION_FILE = os.environ.get("UNITY_MCP_SESSION_FILE") or _default_session_file()


class McpError(Exception):
    """The server could not be reached, or the HTTP request itself failed."""


# --- Protocol -------------------------------------------------------------------

def post(payload, session=None, timeout=300):
    """POST one JSON-RPC message. Returns (session id from the response headers, [messages])."""
    headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream",
               "MCP-Protocol-Version": PROTOCOL}
    if session:
        headers["Mcp-Session-Id"] = session
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            sid = resp.headers.get("Mcp-Session-Id")
            ctype = resp.headers.get("Content-Type", "")
            body = resp.read().decode("utf-8", "replace")
    except (urllib.error.URLError, OSError) as e:  # HTTPError is a URLError
        raise McpError(f"Unity MCP request to {URL} failed: {e}. Is the editor open with its MCP server "
                       f"running? (python tools/unity/wait_port.py 8080)") from e
    messages = []
    if "text/event-stream" in ctype:
        for line in body.splitlines():
            if line.startswith("data:"):
                data = line[5:].strip()
                if data:
                    messages.append(json.loads(data))
    elif body.strip():
        messages.append(json.loads(body))
    return sid, messages


def session(timeout=300):
    """A live session id: the cached one if the server still accepts it, else a new one."""
    if os.path.exists(SESSION_FILE):
        with open(SESSION_FILE, encoding="utf-8") as f:
            sid = f.read().strip()
        if sid:
            try:
                _, msgs = post({"jsonrpc": "2.0", "id": 0, "method": "ping"}, sid, timeout)
                if msgs and "error" not in msgs[0]:
                    return sid
            except McpError:
                pass  # stale session (server restarted): start a new one below
    sid, _ = post({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {
        "protocolVersion": PROTOCOL, "capabilities": {},
        "clientInfo": {"name": "fratrow-mcpcall", "version": "1.0"}}}, timeout=timeout)
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, sid, timeout)
    try:
        os.makedirs(os.path.dirname(SESSION_FILE) or ".", exist_ok=True)
        with open(SESSION_FILE, "w", encoding="utf-8") as f:
            f.write(sid or "")
    except OSError:
        pass  # caching is an optimisation; the session works without it
    return sid


def request(method, params, sid, timeout=300):
    """Send a request and return its response message ({} if the server sent none)."""
    _, msgs = post({"jsonrpc": "2.0", "id": 2, "method": method, "params": params}, sid, timeout)
    for m in msgs:
        if m.get("id") == 2:
            return m
    return msgs[-1] if msgs else {}


def editor_state(timeout=30):
    """The editor-state resource's "data" dict (play_mode, compilation, advice), or None if unavailable."""
    try:
        res = request("resources/read", {"uri": STATE_URI}, session(timeout), timeout)
        return json.loads(res["result"]["contents"][0]["text"])["data"]
    except (McpError, KeyError, IndexError, TypeError, ValueError):
        return None


def _print_content(res):
    """Print a tools/call result; returns the exit status it implies."""
    if "error" in res:
        print("ERROR:", json.dumps(res["error"], indent=1))
        return 1
    for item in res.get("result", {}).get("content", []):
        print(item.get("text", item) if item.get("type") == "text" else f"<{item.get('type')}>")
    if res.get("result", {}).get("isError"):
        print("(tool reported isError)")
        return 1
    return 0


# --- Commands -------------------------------------------------------------------

def cmd_list(sid, args):
    res = request("tools/list", {}, sid)
    for t in res.get("result", {}).get("tools", []):
        desc = (t.get("description") or "").strip().splitlines()
        print(f"{t['name']}: {desc[0][:110] if desc else ''}")
    return 0


def cmd_schema(sid, args):
    res = request("tools/list", {}, sid)
    for t in res.get("result", {}).get("tools", []):
        if t["name"] == args[0]:
            print(t.get("description", ""))
            print(json.dumps(t.get("inputSchema", {}), indent=1))
            return 0
    print(f"ERROR: no tool named '{args[0]}' (run `mcpcall.py list`)")
    return 1


def cmd_read(sid, args):
    res = request("resources/read", {"uri": args[0]}, sid)
    if "error" in res:
        print("ERROR:", json.dumps(res["error"], indent=1))
        return 1
    for item in res.get("result", {}).get("contents", []):
        print(item.get("text", item))
    return 0


def cmd_call(sid, args):
    raw = args[1] if len(args) > 1 else "{}"
    try:
        if raw.startswith("@"):
            with open(raw[1:], encoding="utf-8-sig") as f:
                arguments = json.load(f)
        else:
            arguments = json.loads(raw)
    except (OSError, ValueError) as e:
        print(f"ERROR: tool arguments are not valid JSON ({e})")
        return 2
    return _print_content(request("tools/call", {"name": args[0], "arguments": arguments}, sid))


def cmd_code(sid, args):
    if not os.path.isfile(args[0]):
        print(f"ERROR: no such file: {args[0]}")
        return 2
    with open(args[0], encoding="utf-8-sig") as f:
        code = f.read()
    return _print_content(request("tools/call", {"name": "execute_code", "arguments": {
        "action": "execute", "code": code}}, sid))


def cmd_compile(sid, args):
    """Refresh + compile, wait for a *new* compile to finish, then print console errors."""
    before = ((editor_state() or {}).get("compilation") or {}).get("last_compile_finished_unix_ms")
    request("tools/call", {"name": "refresh_unity", "arguments": {
        "mode": "force", "scope": "all", "compile": "request", "wait_for_ready": False}}, sid)
    start, ok = time.time(), False
    while time.time() - start < COMPILE_TIMEOUT_S:
        time.sleep(COMPILE_POLL_S)
        s = editor_state() or {}
        comp, advice = s.get("compilation") or {}, s.get("advice") or {}
        done = comp.get("last_compile_finished_unix_ms")
        if done and done != before and advice.get("ready_for_tools") and not comp.get("is_compiling"):
            print(f"compiled + ready after ~{time.time() - start:.0f}s")
            ok = True
            break
    if not ok:
        print(f"TIMEOUT: no new compile finished within {COMPILE_TIMEOUT_S}s "
              f"(editor in Play mode? run wait_not_playing.py first)")
    print("--- console errors (most recent 10; may include errors from before this compile) ---")
    _print_content(request("tools/call", {"name": "read_console", "arguments": {
        "types": ["error"], "count": 10, "include_stacktrace": False}}, session()))
    return 0 if ok else 1


COMMANDS = {"list": cmd_list, "schema": cmd_schema, "read": cmd_read, "call": cmd_call,
            "code": cmd_code, "compile": cmd_compile}
REQUIRED_ARGS = {"schema": 1, "read": 1, "call": 1, "code": 1}


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="replace")  # Unity log text can hold characters cp1252 cannot print
    if not argv or argv[0] not in COMMANDS:
        print(__doc__)
        return 2
    cmd, args = argv[0], argv[1:]
    if len(args) < REQUIRED_ARGS.get(cmd, 0):
        print(f"usage error: '{cmd}' needs an argument\n")
        print(__doc__)
        return 2
    try:
        return COMMANDS[cmd](session(), args)
    except McpError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
