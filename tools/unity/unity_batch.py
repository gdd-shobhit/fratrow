"""Run the Unity editor in batch mode on a project and summarise the result.

Use this when the editor is closed (the MCP helpers in this folder need it open).
The editor version comes from ProjectSettings/ProjectVersion.txt and is looked up
under the Unity Hub install folder (override with --editor or UNITY_EDITOR).

    python tools/unity/unity_batch.py --project <path> --execute FratRow.Art.Characters.Editor.CharacterTrioCheck.RunBatch
    python tools/unity/unity_batch.py --project <path> --tests EditMode

--execute runs a static editor method (it should call EditorApplication.Exit with
its own status; otherwise -quit ends the run). --tests runs the Test Runner and
summarises the NUnit results. Batch mode keeps graphics on so editor checks can
render (no -nographics). Logs go to <project>/Logs/batch-*.log unless --log is
given. Exit code: the editor's, or 3 when compile errors were logged, 4 when
tests failed.
"""
import argparse
import os
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

HUB_EDITORS = r"C:\Program Files\Unity\Hub\Editor"
ERROR_LINE = re.compile(r"error CS\d+|Compilation failed|Scripts have compiler errors|"
                        r"Exception:|\[[A-Za-z]+\].*(?:missing|not found|failed)", re.IGNORECASE)


def editor_path(project, explicit=None):
    if explicit or os.environ.get("UNITY_EDITOR"):
        return explicit or os.environ["UNITY_EDITOR"]
    with open(os.path.join(project, "ProjectSettings", "ProjectVersion.txt"), encoding="utf-8") as f:
        version = re.search(r"m_EditorVersion:\s*(\S+)", f.read()).group(1)
    path = os.path.join(HUB_EDITORS, version, "Editor", "Unity.exe")
    if not os.path.exists(path):
        sys.exit(f"Unity {version} not found at {path}; install it with Unity Hub or pass --editor")
    return path


def project_open(project):
    """True when another editor holds the project (Unity keeps Temp/UnityLockfile open)."""
    lock = os.path.join(project, "Temp", "UnityLockfile")
    if not os.path.exists(lock):
        return False
    try:
        with open(lock, "a"):
            return False
    except OSError:
        return True


def summarise_log(path, tail=25):
    if not os.path.exists(path):
        print(f"(no log at {path})")
        return 0
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    errors = [ln for ln in lines if ERROR_LINE.search(ln)]
    compile_errors = [ln for ln in errors if "error CS" in ln]
    print(f"--- log: {path} ({len(lines)} lines)")
    for ln in dict.fromkeys(errors):  # de-duplicated, in order
        print("  !", ln.strip()[:300])
    tagged = [ln for ln in lines if re.match(r"\[[A-Za-z]+\]", ln.strip())]
    if tagged:
        print("--- tagged messages (last %d):" % min(tail, len(tagged)))
        for ln in tagged[-tail:]:
            print("   ", ln.strip()[:300])
    return len(compile_errors)


def summarise_tests(path):
    if not os.path.exists(path):
        print(f"(no test results at {path})")
        return 1
    root = ET.parse(path).getroot()
    print(f"--- tests: total={root.get('total')} passed={root.get('passed')} failed={root.get('failed')} "
          f"skipped={root.get('skipped')} result={root.get('result')}")
    for case in root.iter("test-case"):
        if case.get("result") == "Failed":
            message = case.find("failure/message")
            print(f"  FAILED {case.get('fullname')}: {(message.text or '').strip()[:400] if message is not None else ''}")
    return int(root.get("failed") or 0)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", required=True)
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--execute", metavar="METHOD", help="static editor method, Namespace.Class.Method")
    mode.add_argument("--tests", choices=("EditMode", "PlayMode"))
    mode.add_argument("--import-only", action="store_true", help="open, import and compile, then quit")
    ap.add_argument("--editor")
    ap.add_argument("--log")
    ap.add_argument("--results", help="NUnit XML path for --tests")
    ap.add_argument("--timeout", type=int, default=3600)
    args = ap.parse_args()

    project = os.path.abspath(args.project)
    if project_open(project):
        sys.exit(f"{project} is open in another editor; close it or use the MCP helpers instead")
    stamp = time.strftime("%Y%m%d-%H%M%S")
    log = os.path.abspath(args.log or os.path.join(project, "Logs", f"batch-{stamp}.log"))
    os.makedirs(os.path.dirname(log), exist_ok=True)
    cmd = [editor_path(project, args.editor), "-batchmode", "-projectPath", project, "-logFile", log]
    results = None
    if args.execute:
        cmd += ["-executeMethod", args.execute, "-quit"]
    elif args.tests:
        results = os.path.abspath(args.results or os.path.join(project, "Logs", f"tests-{args.tests}-{stamp}.xml"))
        cmd += ["-runTests", "-testPlatform", args.tests, "-testResults", results]
    else:
        cmd += ["-quit"]

    print("$", " ".join(f'"{c}"' if " " in c else c for c in cmd), flush=True)
    start = time.time()
    try:
        code = subprocess.run(cmd, timeout=args.timeout).returncode
    except subprocess.TimeoutExpired:
        print(f"timed out after {args.timeout}s")
        code = 124
    print(f"--- editor exited with {code} after {time.time() - start:.0f}s")
    compile_errors = summarise_log(log)
    failed = summarise_tests(results) if results else 0
    if code == 0 and compile_errors:
        code = 3
    if code == 0 and failed:
        code = 4
    sys.exit(code)


if __name__ == "__main__":
    main()
