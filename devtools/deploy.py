#!/usr/bin/env python3
"""
Deploy one commit to the demo server: run it from the repository root on the demo machine.

    python3 devtools/deploy.py <commit> <env-file> [--project NAME] [--wait SECONDS]

It builds the image `ai-hub:<commit>` from that commit (exported with `git archive`, so the working
tree is not touched), starts it with Compose as project `ai-hub-demo` (compose.yaml, its values from
<env-file>), and waits for `GET /health` on the file's AI_HUB_PORT to answer 200. When no 200 comes
within the wait, it starts the image that ran before and waits for it again. The data volume is never
removed. The environment file's path and values are never printed, and Docker's own output is not
echoed. The last line says what happened; the exit is 0 only for a healthy deploy.

`--project` names another Compose project (for the script's own tests).
Python 3, standard library only.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMPOSE_FILE = ROOT / "compose.yaml"
REPOSITORY = "ai-hub"
DEMO_PROJECT = "ai-hub-demo"
APP_SERVICE = "app"
DEFAULT_PORT = "8080"
DEFAULT_WAIT_SECONDS = 180
POLL_SECONDS = 2
REQUEST_TIMEOUT_SECONDS = 5
BUILD_ERROR_LINES = 20
# A Docker tag: what `ai-hub:<commit>` may carry.
TAG = re.compile(r"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")


class DeployFailed(Exception):
    pass


def say(message: str) -> None:
    print(message, flush=True)


def run(command: list[str], env: dict[str, str] | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)


def parse_arguments(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Deploy one commit to the demo server.")
    parser.add_argument("commit", nargs="?", help="the commit to deploy")
    parser.add_argument("env_file", nargs="?", help="the demo environment file (never printed)")
    parser.add_argument("--project", default=DEMO_PROJECT, help=f"the Compose project (default {DEMO_PROJECT})")
    parser.add_argument("--wait", type=int, default=DEFAULT_WAIT_SECONDS,
                        help=f"seconds to wait for /health (default {DEFAULT_WAIT_SECONDS})")
    arguments = parser.parse_args(argv)
    if not arguments.commit:
        raise DeployFailed("refused: no commit given — usage: deploy.py <commit> <env-file>")
    if not TAG.match(arguments.commit):
        raise DeployFailed("refused: the commit is not a valid image tag")
    if not arguments.env_file:
        raise DeployFailed("refused: no environment file given — usage: deploy.py <commit> <env-file>")
    if not Path(arguments.env_file).is_file():
        raise DeployFailed("refused: the environment file does not exist")
    if arguments.wait <= 0:
        raise DeployFailed("refused: --wait must be a positive number of seconds")
    return arguments


def read_port(env_file: str) -> str:
    for line in Path(env_file).read_text(encoding="utf-8").splitlines():
        key, separator, value = line.strip().partition("=")
        if separator and key.strip() == "AI_HUB_PORT":
            return value.strip().strip("\"'") or DEFAULT_PORT
    return DEFAULT_PORT


def running_image(project: str) -> str | None:
    """The tag of the image the project's app container runs now, or None."""
    ps = run(["docker", "ps", "-q",
              "--filter", f"label=com.docker.compose.project={project}",
              "--filter", f"label=com.docker.compose.service={APP_SERVICE}"])
    if ps.returncode != 0:
        raise DeployFailed("failed: Docker did not answer — is it running?")
    container = ps.stdout.split()
    if not container:
        return None
    inspect = run(["docker", "inspect", "--format", "{{.Config.Image}}", container[0]])
    image = inspect.stdout.strip()
    prefix = f"{REPOSITORY}:"
    return image[len(prefix):] if inspect.returncode == 0 and image.startswith(prefix) else None


def build(commit: str) -> None:
    say(f"build: {REPOSITORY}:{commit}")
    if run(["git", "rev-parse", "--verify", "--quiet", f"{commit}^{{commit}}"]).returncode != 0:
        raise DeployFailed(f"failed at build: there is no commit {commit}")
    with tempfile.TemporaryDirectory(prefix="ai-hub-deploy-") as folder:
        archive = Path(folder) / "source.tar"
        if run(["git", "archive", "--format=tar", "-o", str(archive), commit]).returncode != 0:
            raise DeployFailed(f"failed at build: commit {commit} could not be exported")
        source = Path(folder) / "source"
        with tarfile.open(archive) as tar:
            tar.extractall(source, filter="data")
        result = run(["docker", "build", "-t", f"{REPOSITORY}:{commit}", str(source)])
    if result.returncode != 0:
        for line in result.stderr.strip().splitlines()[-BUILD_ERROR_LINES:]:
            say(f"  {line}")
        raise DeployFailed(f"failed at build: docker build of {commit} exited {result.returncode}")


def start(project: str, env_file: str, tag: str) -> None:
    say(f"start: {REPOSITORY}:{tag} as {project}")
    env = {**os.environ, "AI_HUB_TAG": tag}
    result = run(["docker", "compose", "-p", project, "-f", str(COMPOSE_FILE), "--env-file", env_file, "up", "-d"], env)
    if result.returncode != 0:
        raise DeployFailed(f"failed at start: docker compose up exited {result.returncode}")


def healthy(port: str, wait_seconds: int) -> bool:
    say(f"wait: /health on port {port}, up to {wait_seconds}s")
    deadline = time.monotonic() + wait_seconds
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=REQUEST_TIMEOUT_SECONDS) as response:
                if response.status == 200:
                    return True
        except (urllib.error.URLError, ConnectionError, TimeoutError, OSError):
            pass  # Not up yet.
        time.sleep(POLL_SECONDS)
    return False


def deploy(arguments: argparse.Namespace) -> str:
    commit, env_file, project, wait = arguments.commit, arguments.env_file, arguments.project, arguments.wait
    port = read_port(env_file)
    previous = running_image(project)
    build(commit)
    start(project, env_file, commit)
    if healthy(port, wait):
        return f"deployed: {REPOSITORY}:{commit} is healthy on /health"
    unhealthy = f"/health did not answer 200 within {wait}s for {REPOSITORY}:{commit}"
    if previous is None:
        raise DeployFailed(f"failed: {unhealthy}; there was no previous image to restore")
    say(f"restore: {REPOSITORY}:{previous}")
    start(project, env_file, previous)
    if not healthy(port, wait):
        raise DeployFailed(f"failed: {unhealthy}; restored {REPOSITORY}:{previous}, but its /health did not answer 200 either")
    raise DeployFailed(f"failed: {unhealthy}; restored {REPOSITORY}:{previous}")


def main(argv: list[str]) -> int:
    try:
        say(deploy(parse_arguments(argv)))
        return 0
    except DeployFailed as failure:
        say(str(failure))
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
