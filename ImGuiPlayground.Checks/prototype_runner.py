#!/usr/bin/env python3
"""Explicit staged runner for the maintainer-only managed/native ABI prototype.

Ordinary builds still stage production assets; only disposable copies receive the prototype.
It copies each ordinary app output to disposable scratch, replaces only that copy's
ImGui library, then runs isolated ABI, existing-checks, and hello-world processes.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any

CHECKS_DIR = Path(__file__).resolve().parent
PROJECT_DIR = CHECKS_DIR.parent / "ImGuiPlayground"
# Shared cancellation policy belongs to the opt-in prototype, not the ordinary host.
sys.path.insert(0, str(PROJECT_DIR.parent))
process_utils = importlib.import_module("ImGuiPlayground.native.process_utils")
CHECKS_PROJECT = CHECKS_DIR / "ImGuiPlayground.Checks.csproj"
HOST_PROJECT = PROJECT_DIR / "ImGuiPlayground.csproj"
TIMEOUT_SECONDS = 240
SUPPORTED = {
    "osx-arm64": ("Darwin", {"arm64", "aarch64"}, "libimgui.dylib"),
    "linux-x64": ("Linux", {"x86_64", "amd64"}, "libimgui.so"),
    "win-x64": ("Windows", {"x86_64", "amd64"}, "imgui.dll"),
}


class RunnerError(RuntimeError):
    pass


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_json(path: Path) -> dict[str, Any]:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > 4 * 1024 * 1024:
        raise RunnerError(f"Required regular metadata file missing/unsafe/oversized: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RunnerError(f"Cannot parse metadata {path}: {error}") from error
    if not isinstance(value, dict):
        raise RunnerError(f"Metadata root must be a JSON object: {path}")
    return value


def host_rid() -> str:
    system = platform.system()
    machine = platform.machine().lower()
    for rid, (expected_system, machines, _) in SUPPORTED.items():
        if system == expected_system and machine in machines:
            return rid
    raise RunnerError(f"Managed ABI execution is supported only on macOS arm64, Linux x64, Windows x64; got {system}/{machine}.")


def assert_target_host(rid: str) -> None:
    current = host_rid()
    if rid != current:
        raise RunnerError(f"Refusing to execute foreign target {rid!r} on {current!r}. Cross-build/static audits are not native execution; run this verifier on the target host.")


def ensure_no_symlinks(root: Path) -> None:
    if root.is_symlink():
        raise RunnerError(f"Refusing symlinked path: {root}")
    for current, directories, files in os.walk(root, followlinks=False):
        for name in directories + files:
            candidate = Path(current, name)
            if candidate.is_symlink():
                raise RunnerError(f"Refusing to copy a symlinked build-output path: {candidate}")


def copy_tree(source: Path, destination: Path) -> None:
    if not source.is_dir():
        raise RunnerError(f"Expected ordinary build output directory: {source}")
    ensure_no_symlinks(source)
    shutil.copytree(source, destination, symlinks=False)
    ensure_no_symlinks(destination)


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def run_child(command: list[str], cwd: Path, environment: dict[str, str], log_path: Path,
              records: list[dict[str, Any]], timeout: int = TIMEOUT_SECONDS) -> None:
    started = time.monotonic()
    try:
        with log_path.open("wb") as log, process_utils.managed_process(
                command, cwd=cwd, env=environment, stdout=log, stderr=subprocess.STDOUT) as process:
            code = process.wait(timeout=timeout)
    except subprocess.TimeoutExpired as error:
        elapsed = round(time.monotonic() - started, 3)
        records.append({"command": command, "result": "timed-out", "seconds": elapsed,
                        "timeoutSeconds": timeout, "log": str(log_path)})
        raise RunnerError(f"Child command exceeded {timeout}s; retained log: {log_path}") from error
    except KeyboardInterrupt:
        records.append({"command": command, "result": "interrupted", "log": str(log_path)})
        raise
    except OSError as error:
        records.append({"command": command, "result": "not-started", "error": str(error), "log": str(log_path)})
        raise RunnerError(f"Could not start child command {command!r}: {error}; see {log_path}") from error
    elapsed = round(time.monotonic() - started, 3)
    records.append({"command": command, "result": "passed" if code == 0 else "failed",
                    "exitCode": code, "seconds": elapsed, "log": str(log_path)})
    if code != 0:
        raise RunnerError(f"Child command exited {code}; retained output: {log_path}")


def run(args: argparse.Namespace) -> int:
    rid = args.target_rid or host_rid()
    if rid not in SUPPORTED:
        raise RunnerError(f"Unsupported target RID {rid!r}; supported: {', '.join(SUPPORTED)}")
    assert_target_host(rid)

    game_dir = os.environ.get("KSAFolder") or os.environ.get("KSA_DLL_DIR", "")  # noqa: SIM112 - exact MSBuild property name
    if not game_dir:
        raise RunnerError("Set KSAFolder or KSA_DLL_DIR to the explicit external KSA assembly directory before running this verifier.")
    game_path = Path(game_dir).expanduser().resolve(strict=True)
    if not game_path.is_dir():
        raise RunnerError("KSAFolder/KSA_DLL_DIR must name a directory containing external managed assemblies.")
    for name in ("Brutal.ImGui.dll", "Brutal.Glfw.dll", "Brutal.Core.Common.dll", "Brutal.Core.Strings.dll", "Brutal.Core.Numerics.dll"):
        if not (game_path / name).is_file():
            raise RunnerError(f"Required external managed assembly missing under KSAFolder/KSA_DLL_DIR: {name}")

    artifact_name = SUPPORTED[rid][2]
    artifact_dir = PROJECT_DIR / ".tmp" / "native" / rid
    artifact = artifact_dir / artifact_name
    build_record_path = artifact_dir / "build.json"
    build_record = load_json(build_record_path)
    if build_record.get("schema") != "playground_imgui.native-build-record" or build_record.get("schemaVersion") != 1:
        raise RunnerError("Unsupported/missing frozen native build-record schema.")
    artifact_hash = sha256(artifact)
    if artifact.name != build_record.get("artifact") or artifact_hash != build_record.get("sha256"):
        raise RunnerError("Native artifact identity/hash differs from frozen build.json; no staged process was started.")
    if build_record.get("rid") != rid:
        raise RunnerError("Frozen native build record target RID differs from requested target.")

    native_dir = PROJECT_DIR / "native"
    metadata_sources = {
        "exports.json": native_dir / "exports.json",
        "source.lock.json": native_dir / "source.lock.json",
        "playground_imgui_config.h": native_dir / "include" / "playground_imgui_config.h",
        "build.json": build_record_path,
    }
    environment = os.environ.copy()
    environment["KSA_DLL_DIR"] = str(game_path)
    if environment.get("KSAFolder"):
        environment["KSAFolder"] = str(game_path)
    dotnet = environment.get("DOTNET_HOST_PATH") or shutil.which("dotnet")
    if not dotnet:
        raise RunnerError("dotnet host was not found.")

    temp_root = Path(tempfile.mkdtemp(prefix="imgui-playground-abi-"))
    if temp_root.is_symlink() or not temp_root.is_dir():
        raise RunnerError("System temporary directory returned an unsafe staging directory.")
    try:
        checks_output = CHECKS_DIR / "bin" / args.configuration / "net10.0"
        host_output = PROJECT_DIR / "bin" / args.configuration / "net10.0"
        staged_checks = temp_root / "Checks"
        staged_host = temp_root / "Host"
        staged_metadata = temp_root / "metadata"
        staged_metadata.mkdir()
        records: list[dict[str, Any]] = []
        state: dict[str, Any] = {
            "schema": "playground_imgui.staged-runner-report",
            "schemaVersion": 1,
            "targetRid": rid,
            "nativeArtifact": {"path": str(artifact.resolve()), "sha256": artifact_hash},
            "managedAssemblySource": str((game_path / "Brutal.ImGui.dll").resolve()),
            "status": "running",
            "commands": records,
            "stageDirectory": str(temp_root),
        }
        state_path = temp_root / "runner-results.json"
        try:
            write_json(state_path, state)
            run_child([dotnet, "build", str(HOST_PROJECT), "--nologo", "-v", "quiet", "-c", args.configuration],
                      PROJECT_DIR, environment, temp_root / "build-host.log", records)
            write_json(state_path, state)
            run_child([dotnet, "build", str(CHECKS_PROJECT), "--nologo", "-v", "quiet", "-c", args.configuration],
                      CHECKS_DIR, environment, temp_root / "build-checks.log", records)
            write_json(state_path, state)

            copy_tree(checks_output, staged_checks)
            copy_tree(host_output, staged_host)
            for filename, source in metadata_sources.items():
                if source.is_symlink() or not source.is_file():
                    raise RunnerError(f"Missing/unsafe native prototype identity file: {source}")
                shutil.copyfile(source, staged_metadata / filename)
            managed_assembly = staged_checks / "Brutal.ImGui.dll"
            if not managed_assembly.is_file():
                raise RunnerError("Ordinary checks output lacks its external Brutal.ImGui.dll copy.")
            managed_hash = sha256(managed_assembly)

            staged_native = staged_checks / artifact_name
            if not staged_native.is_file():
                raise RunnerError(f"Ordinary checks output lacks its native ImGui file: {staged_native}")
            shutil.copyfile(artifact, staged_native)
            if sha256(staged_native) != artifact_hash:
                raise RunnerError("Staged ImGui replacement did not preserve the frozen artifact hash.")
            staged_host_native = staged_host / artifact_name
            if not staged_host_native.is_file():
                raise RunnerError(f"Ordinary host output lacks its native ImGui file: {staged_host_native}")
            shutil.copyfile(artifact, staged_host_native)
            if sha256(staged_host_native) != artifact_hash:
                raise RunnerError("Staged host ImGui replacement did not preserve the frozen artifact hash.")

            abi_report = temp_root / "managed-abi-report.json"
            check_dll = staged_checks / "ImGuiPlayground.Checks.dll"
            host_dll = staged_host / "ImGuiPlayground.dll"
            if not check_dll.is_file() or not host_dll.is_file():
                raise RunnerError("Ordinary staged output is missing its application DLL.")
            run_child([dotnet, str(check_dll), "--prototype-abi", str(staged_native), str(staged_metadata),
                       str(abi_report), rid, managed_hash], staged_checks, environment,
                      temp_root / "abi-verifier.log", records, timeout=90)
            write_json(state_path, state)
            if not abi_report.is_file():
                raise RunnerError("ABI verifier exited successfully without producing its machine-readable report.")
            abi_summary = load_json(abi_report)
            native_identity = abi_summary.get("nativeArtifact", {})
            managed_identity = abi_summary.get("managedAssembly", {})
            if abi_summary.get("schema") != "playground_imgui.managed-abi-report" or abi_summary.get("schemaVersion") != 1:
                raise RunnerError("ABI report schema/version is invalid; refusing to continue staged checks.")
            if abi_summary.get("status") != "passed":
                raise RunnerError("ABI report does not state status=passed; refusing to continue staged checks.")
            if native_identity.get("targetRid") != rid or native_identity.get("sha256") != artifact_hash:
                raise RunnerError("ABI report native target/artifact identity differs from the staged file.")
            if managed_identity.get("sha256") != managed_hash:
                raise RunnerError("ABI report managed binding identity differs from the staged Brutal.ImGui.dll.")
            gate = abi_summary.get("safeHostPublicSubsetGate", {})
            passed, full_binding = gate.get("passed"), abi_summary.get("fullBindingCompatible")
            if not isinstance(passed, bool) or not passed or not isinstance(full_binding, bool) or full_binding:
                raise RunnerError("ABI report is missing the successful subset gate or falsely claims full-binding compatibility.")

            run_child([dotnet, str(check_dll)], staged_checks, environment,
                      temp_root / "existing-render-input-checks.log", records, timeout=180)
            write_json(state_path, state)

            capture_path = temp_root / "hello-world.png"
            run_child([dotnet, str(host_dll), "--capture", str(capture_path)], staged_host, environment,
                      temp_root / "hello-world-capture.log", records, timeout=90)
            if not capture_path.is_file() or capture_path.stat().st_size < 32:
                raise RunnerError("Hello-world capture process passed without a plausible PNG artifact.")
            write_json(state_path, state)

            state["status"] = "passed"
            state["managedAssemblySha256"] = managed_hash
            state["abiReportSha256"] = sha256(abi_report)
            state["captureSha256"] = sha256(capture_path)
            write_json(state_path, state)
            if args.report:
                requested_report = Path(args.report).expanduser()
                report_parent = requested_report.parent.resolve(strict=True)
                if not report_parent.is_dir():
                    raise RunnerError("Optional report parent must already exist and resolve to a directory.")
                report_path = report_parent / requested_report.name
                if report_path.is_symlink():
                    raise RunnerError("Optional report target must not be a symlink.")
                shutil.copyfile(abi_report, report_path)
            return 0
        except BaseException as error:
            state["status"] = "failed"
            state["failure"] = str(error)
            write_json(state_path, state)
            if isinstance(error, KeyboardInterrupt):
                print(f"Interrupted; staged outputs and logs retained at: {temp_root}", file=sys.stderr)
                raise
            raise RunnerError(f"{error}\nStaged outputs, command logs, and failure report retained at: {temp_root}") from error
    finally:
        if (temp_root / "runner-results.json").exists():
            try:
                state = load_json(temp_root / "runner-results.json")
            except RunnerError:
                state = {"status": "failed"}
            if state.get("status") == "passed":
                shutil.rmtree(temp_root)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target-rid", choices=sorted(SUPPORTED), help="must match the native current host; foreign targets are refused")
    parser.add_argument("--configuration", default="Debug", choices=("Debug", "Release"))
    parser.add_argument("--report", help="optional path for the successful managed ABI JSON report; parent must already exist")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    try:
        return run(parse_args(argv))
    except KeyboardInterrupt:
        print("prototype runner: interrupted; child tree terminated", file=sys.stderr)
        return 130
    except (RunnerError, OSError, ValueError) as error:
        print(f"prototype runner: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
