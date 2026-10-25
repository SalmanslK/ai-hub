#!/usr/bin/env python3
"""
Every check before a merge, in one command: run it from the repository root.

    python3 devtools/checks/run-checks.py [--steps <file.json>]

The steps run in order and the first failure stops the run: both builds (back-end warnings are
errors), the back-end fast and integration suites, the front-end suite, lint, types, every
generator's --check and the factory self-tests. Each prints one line — PASS or FAIL and, for a
suite, its executed / passed / failed / skipped counts. A suite's results go through the skip gate
(check-test-results.py): one that skipped a test fails as skipped, one that ran nothing or wrote no
results fails as empty. The last line names the failed step and the exit is 1; else it says every
check passed and the exit is 0.

`--steps` runs a JSON list of steps instead of the built-in one, each
`{"name", "command": [...], "folder", "results"?}` — for the script's own tests.
Python 3, standard library only.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHECKS = ROOT / "devtools" / "checks"
FACTORY = ROOT / "devtools" / "factory" / "factory.py"
BACKEND = ROOT / "backend"
FRONTEND = ROOT / "frontend"


def load_skip_gate():
    spec = importlib.util.spec_from_file_location("check_test_results", CHECKS / "check-test-results.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


gate = load_skip_gate()


@dataclass(frozen=True)
class Step:
    name: str
    commands: list[list[str]]
    folder: Path
    results: Path | None = None


def dotnet_suite(name: str, project: str, results: Path) -> Step:
    return Step(name, [["dotnet", "test", project, "--no-build", "--logger", "trx;LogFileName=results.trx",
                        "--results-directory", str(results)]], BACKEND, results)


def built_in_steps(results: Path) -> list[Step]:
    python = sys.executable
    return [
        Step("backend build", [["dotnet", "build", "App.slnx"]], BACKEND),
        Step("frontend build", [["npm", "run", "build"]], FRONTEND),
        dotnet_suite("backend fast suite", "tests/App.Tests", results / "backend-fast"),
        dotnet_suite("backend integration suite", "tests/App.IntegrationTests", results / "backend-integration"),
        Step("frontend suite", [["npx", "--no", "vitest", "run", "--reporter=default", "--reporter=junit",
                                 f"--outputFile.junit={results / 'frontend' / 'results.xml'}"]],
             FRONTEND, results / "frontend"),
        Step("lint", [["npm", "run", "lint"]], FRONTEND),
        Step("types", [["npm", "run", "typecheck"]], FRONTEND),
        Step("generated docs", [[python, str(CHECKS / "gen-task-rollup.py"), "--check"],
                                [python, str(FACTORY), "index", "--check"]], ROOT),
        Step("factory self-tests", [[python, str(FACTORY), "verify", "--all"],
                                    [python, str(CHECKS / "check-book.py")],
                                    [python, str(CHECKS / "check-rules.py")],
                                    [python, str(CHECKS / "check-code-rules.py")]], ROOT),
    ]


def steps_from(path: Path) -> list[Step]:
    listed = json.loads(path.read_text(encoding="utf-8"))
    return [Step(entry["name"], [entry["command"]], Path(entry["folder"]),
                 Path(entry["results"]) if entry.get("results") else None) for entry in listed]


def run_commands(step: Step) -> tuple[bool, str]:
    """Runs the step's commands in turn, stopping at the first that fails: (all passed, what they wrote)."""
    written = []
    for command in step.commands:
        tool = shutil.which(command[0]) or command[0]
        try:
            done = subprocess.run([tool, *command[1:]], cwd=step.folder, capture_output=True, text=True,
                                  encoding="utf-8", errors="replace")
        except OSError as error:
            return False, f"{command[0]} did not start: {error}"
        written.append(done.stdout + done.stderr)
        if done.returncode != 0:
            return False, "".join(written)
    return True, "".join(written)


def suite_counts(results: Path):
    """The suite's summed counts and the reason it is not a pass (gate.EMPTY, gate.SKIPPED or None)."""
    files = gate.results_files(str(results))
    if not files:
        return None, gate.EMPTY
    try:
        found = [gate.counts(path) for path in files]
    except ValueError:
        return None, gate.EMPTY
    total = gate.Counts(*map(sum, zip(*found)))
    return total, gate.problem(total)


def check(step: Step) -> str | None:
    """Runs one step and prints its line; returns why it failed, or None when it passed."""
    passed, written = run_commands(step)
    reason = None if passed else "failed"
    detail = ""
    if step.results is not None:
        total, problem = suite_counts(step.results)
        if total is not None:
            detail = (f" — {total.executed} executed, {total.passed} passed, {total.failed} failed, "
                      f"{total.skipped} skipped")
        if problem == gate.EMPTY:
            reason = "empty: no tests executed, or no results written"
        elif problem == gate.SKIPPED:
            reason = "skipped tests — a skip is a failure here"
        elif total.failed:
            reason = "failed"
    print(f"{'FAIL' if reason else 'PASS'}  {step.name}{detail}", flush=True)
    if reason and written.strip():
        print("\n".join("    " + line for line in written.strip().splitlines()), flush=True)
    return reason


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Run every check before a merge.")
    parser.add_argument("--steps", type=Path, help="a JSON list of steps to run instead of the built-in one")
    args = parser.parse_args(argv)
    with tempfile.TemporaryDirectory(prefix="run-checks-") as results:
        steps = steps_from(args.steps) if args.steps else built_in_steps(Path(results))
        for step in steps:
            reason = check(step)
            if reason:
                print(f"Checks failed at step '{step.name}' ({reason}).")
                return 1
    print(f"All {len(steps)} checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
