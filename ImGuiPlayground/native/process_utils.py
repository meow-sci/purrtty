"""Bounded process-tree lifetime for opt-in native build/prototype tools only."""
from __future__ import annotations

import contextlib
import os
import signal
import subprocess
from collections.abc import Iterator
from typing import Any


def kill_process_tree(process: subprocess.Popen[Any]) -> None:
    """Children inherit a dedicated process group; never signal the caller's group."""
    if os.name == "nt":
        try:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                           timeout=10, check=False)
        except (OSError, subprocess.TimeoutExpired):
            process.kill()
    else:
        with contextlib.suppress(ProcessLookupError):
            os.killpg(process.pid, signal.SIGKILL)
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=10)


@contextlib.contextmanager
def managed_process(command: list[str], **kwargs: Any) -> Iterator[subprocess.Popen[Any]]:
    """Reap the process tree on timeout, Ctrl-C, SystemExit or any other exception."""
    process = subprocess.Popen(command, start_new_session=(os.name != "nt"),
                               creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
                               **kwargs)
    try:
        yield process
    except BaseException:
        kill_process_tree(process)
        raise


def run(command: list[str], *, timeout: float, capture_output: bool = False,
        check: bool = False, **kwargs: Any) -> subprocess.CompletedProcess[Any]:
    if capture_output:
        kwargs.update(stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    with managed_process(command, **kwargs) as process:
        stdout, stderr = process.communicate(timeout=timeout)
        result = subprocess.CompletedProcess(command, process.returncode, stdout, stderr)
        if check and result.returncode != 0:
            raise subprocess.CalledProcessError(result.returncode, command, stdout, stderr)
        return result
