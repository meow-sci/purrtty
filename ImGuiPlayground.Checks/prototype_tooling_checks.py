#!/usr/bin/env python3
"""Quiet, compiler/game-free regression checks for opt-in prototype tooling."""
from __future__ import annotations

import contextlib
import importlib
import importlib.util
import io
import os
import socket
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
builder = importlib.import_module("ImGuiPlayground.native.build")
processes = importlib.import_module("ImGuiPlayground.native.process_utils")
spec = importlib.util.spec_from_file_location("prototype_runner", Path(__file__).with_name("prototype_runner.py"))
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load the sibling prototype runner.")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ToolingChecks(unittest.TestCase):
    def symlink(self, link: Path, target: Path, directory: bool = False) -> None:
        try:
            link.symlink_to(target, target_is_directory=directory)
        except OSError as error:
            self.skipTest(f"Symlink creation unavailable on this host: {error}")

    def test_redirected_scratch(self) -> None:
        with tempfile.TemporaryDirectory(prefix="imgui scratch paths ") as temporary:
            root = Path(temporary).resolve()
            project, victim = root / "ImGuiPlayground", root / "unrelated"
            project.mkdir()
            victim.mkdir()
            (project / ".tmp").mkdir()
            self.symlink(project / ".tmp" / "native", victim, True)
            with self.assertRaises(builder.BuildError):
                builder.project_scratch(project)
            self.assertEqual(list(victim.iterdir()), [])
            (project / ".tmp" / "native").unlink()
            (project / ".tmp").rmdir()
            self.symlink(project / ".tmp", victim, True)
            with self.assertRaises(builder.BuildError):
                builder.project_scratch(project)
            self.assertEqual(list(victim.iterdir()), [])

    def test_metadata_and_dangling_leaf_redirects(self) -> None:
        with tempfile.TemporaryDirectory(prefix="imgui metadata paths ") as temporary:
            root = Path(temporary).resolve()
            target = root / "target"
            target.mkdir()
            sentinel = root / "sentinel"
            sentinel.write_text("unchanged", encoding="utf-8")
            self.symlink(target / "build.json", sentinel)
            self.symlink(target / "dependencies.txt", root / "absent")
            for name in ("build.json", "dependencies.txt"):
                with self.assertRaises(builder.BuildError):
                    builder.safe_child(target, name)
            with self.assertRaises(builder.BuildError):
                builder.dependency_report(root / "unused", "osx-arm64", target)
            self.assertEqual(sentinel.read_text(encoding="utf-8"), "unchanged")
            self.assertFalse((root / "absent").exists())

    def exercise_process_tree(self, mode: str, tool: str) -> None:
        # The grandchild handshake ensures it exists BEFORE testing the timeout/interrupt.
        # No sleeps or PID polling: an EOF on its socket proves its connection was closed.
        with socket.socket() as listener, tempfile.TemporaryDirectory(prefix="imgui process checks ") as temporary:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(10)
            grandchild = (
                "import socket,sys; c=socket.create_connection(('127.0.0.1',int(sys.argv[1])),10);"
                "c.sendall(b'R'); c.settimeout(30); c.recv(1)"
            )
            child = (
                "import subprocess,sys; p=subprocess.Popen([sys.executable,'-c',sys.argv[1],sys.argv[2]]);"
                "p.wait(timeout=30)"
            )
            command = [sys.executable, "-c", child, grandchild, str(listener.getsockname()[1])]
            connection: socket.socket | None = None
            actual_factory = processes.subprocess.Popen
            spawned = []

            def factory(*args, **kwargs):
                nonlocal connection
                process = actual_factory(*args, **kwargs)
                spawned.append(process)
                # Do not intercept taskkill, which the Windows cleanup branch also starts.
                if args[0] != command:
                    return process
                try:
                    connection, _ = listener.accept()
                    connection.settimeout(10)
                    self.assertEqual(connection.recv(1), b"R")
                except BaseException:
                    processes.kill_process_tree(process)
                    raise
                if mode == "interrupt":
                    # Inject exactly once; the real wait must remain available for reaping.
                    original_wait = process.wait

                    def interrupted_wait(*wait_args, **wait_kwargs):
                        process.wait = original_wait
                        raise KeyboardInterrupt()

                    process.wait = interrupted_wait
                return process

            try:
                with mock.patch.object(processes.subprocess, "Popen", side_effect=factory):
                    error = KeyboardInterrupt if mode == "interrupt" else (builder.BuildError if tool == "builder" else runner.RunnerError)
                    with self.assertRaises(error):
                        if tool == "builder":
                            builder.run_logged(command, Path(temporary) / "child.log", timeout=0.05)
                        else:
                            runner.run_child(command, Path(temporary), os.environ.copy(), Path(temporary) / "child.log", [], timeout=0.05)
                self.assertIsNotNone(spawned[0].returncode)
                if connection is None:
                    raise AssertionError("Missing grandchild handshake socket.")
                # Windows may reset the killed descendant's socket instead of FIN.
                with contextlib.suppress(ConnectionResetError):
                    self.assertEqual(connection.recv(1), b"")
            finally:
                for process in spawned:
                    if process.poll() is None:
                        processes.kill_process_tree(process)
                if connection is not None:
                    connection.close()

    def test_timeout_and_interrupt_trees(self) -> None:
        for tool in ("builder", "runner"):
            for mode in ("timeout", "interrupt"):
                with self.subTest(tool=tool, mode=mode):
                    self.exercise_process_tree(mode, tool)


if __name__ == "__main__":
    log = io.StringIO()
    result = unittest.TextTestRunner(stream=log).run(unittest.defaultTestLoader.loadTestsFromTestCase(ToolingChecks))
    if not result.wasSuccessful():
        sys.stderr.write(log.getvalue())
        sys.exit(1)
