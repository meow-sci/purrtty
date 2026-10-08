#!/usr/bin/env python3
"""Explicit maintainer-only Dear ImGui native prototype builder.

Outputs remain under ImGuiPlayground/.tmp/native and are never promoted to runtimes/.
"""

from __future__ import annotations

import argparse
import copy
import ctypes
import hashlib
import importlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path, PurePosixPath
from typing import Any

# Both direct script execution and extraction as sibling folders use this local package.
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
process_utils = importlib.import_module("ImGuiPlayground.native.process_utils")

NATIVE_DIR = Path(__file__).resolve().parent
PROJECT_DIR = NATIVE_DIR.parent
LOCK_PATH = NATIVE_DIR / "source.lock.json"
EXPORTS_PATH = NATIVE_DIR / "exports.json"
EXPECTED_SCHEMA = "playground_imgui.native-source-lock"
EXPECTED_EXPORT_SCHEMA = "playground_imgui.brutal-subset"
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
COMMIT_PATTERN = re.compile(r"^[0-9a-f]{40}$")
SUPPORTED_TARGETS = {"osx-arm64", "linux-x64", "win-x64"}
MAX_ARCHIVE_BYTES = 8 * 1024 * 1024
MAX_EXTRACTED_BYTES = 24 * 1024 * 1024
MAX_ARCHIVE_MEMBERS = 2500
PROCESS_TIMEOUT_SECONDS = 600


class BuildError(RuntimeError):
    pass


def read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise BuildError(f"cannot read {path.name}: {error}") from error
    if not isinstance(value, dict):
        raise BuildError(f"{path.name} must contain a JSON object")
    return value


def require_nonempty_string(mapping: dict[str, Any], key: str, owner: str) -> str:
    value = mapping.get(key)
    if not isinstance(value, str) or not value:
        raise BuildError(f"{owner}.{key} is missing or invalid")
    return value


def validate_lock(lock: dict[str, Any]) -> None:
    if lock.get("schema") != EXPECTED_SCHEMA or lock.get("schemaVersion") != 1:
        raise BuildError("unsupported or missing source lock schema")
    upstream = lock.get("upstream")
    config = lock.get("configuration")
    toolchain = lock.get("toolchain")
    targets = lock.get("targets")
    if not isinstance(upstream, dict):
        raise BuildError("source lock is missing upstream metadata")
    if not isinstance(config, dict):
        raise BuildError("source lock is missing configuration metadata")
    if not isinstance(toolchain, dict):
        raise BuildError("source lock is missing toolchain metadata")
    if not isinstance(targets, dict):
        raise BuildError("source lock is missing target metadata")
    source_commit = require_nonempty_string(upstream, "sourceCommit", "upstream")
    tag_object = require_nonempty_string(upstream, "tagObject", "upstream")
    if not COMMIT_PATTERN.fullmatch(source_commit) or not COMMIT_PATTERN.fullmatch(tag_object):
        raise BuildError("upstream commit/tag-object pins must be full 40-character lowercase hashes")
    archive_sha = require_nonempty_string(upstream, "archiveSha256", "upstream")
    if not SHA256_PATTERN.fullmatch(archive_sha):
        raise BuildError("upstream.archiveSha256 must be a full lowercase SHA-256")
    archive_url = require_nonempty_string(upstream, "archiveUrl", "upstream")
    if not archive_url.endswith("/" + source_commit):
        raise BuildError("pinned archive URL must terminate in the pinned peeled source commit")
    if require_nonempty_string(upstream, "license", "upstream") != "MIT":
        raise BuildError("unexpected upstream license metadata")
    header = require_nonempty_string(config, "header", "configuration")
    if PurePosixPath(header).is_absolute() or ".." in PurePosixPath(header).parts:
        raise BuildError("configuration.header must be a safe path within native/")
    config_sha = require_nonempty_string(config, "sha256", "configuration")
    if not SHA256_PATTERN.fullmatch(config_sha):
        raise BuildError("configuration.sha256 must be a full lowercase SHA-256")
    if require_nonempty_string(toolchain, "zigVersion", "toolchain") != "0.17.0":
        raise BuildError("prototype builder expects its recorded Zig 0.17.0 toolchain pin")
    if not isinstance(targets, dict) or set(targets) != SUPPORTED_TARGETS:
        raise BuildError("source lock must describe exactly osx-arm64, linux-x64, and win-x64")
    for rid, expected_file in (("osx-arm64", "libimgui.dylib"), ("linux-x64", "libimgui.so"), ("win-x64", "imgui.dll")):
        target = targets.get(rid)
        if not isinstance(target, dict) or not COMMIT_PATTERN.fullmatch(source_commit):
            raise BuildError(f"source lock target is missing or invalid: {rid}")
        require_nonempty_string(target, "zigTarget", rid)
        if require_nonempty_string(target, "file", rid) != expected_file:
            raise BuildError(f"source lock has an unexpected output filename for {rid}")


def validate_export_manifest(manifest: dict[str, Any]) -> None:
    if manifest.get("schema") != EXPECTED_EXPORT_SCHEMA or manifest.get("schemaVersion") != 1:
        raise BuildError("unsupported or missing export manifest schema")
    names: list[str] = []
    for key in ("managedImports", "dataExports", "extraFunctions"):
        items = manifest.get(key)
        if not isinstance(items, list):
            raise BuildError(f"exports.json.{key} must be an array")
        for item in items:
            if not isinstance(item, dict):
                raise BuildError(f"exports.json.{key} contains a non-object entry")
            name = require_nonempty_string(item, "name", f"exports.json.{key} entry")
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
                raise BuildError(f"invalid export symbol in manifest: {name!r}")
            names.append(name)
    if len(names) != len(set(names)):
        raise BuildError("exports.json contains duplicate exported names")
    excluded = manifest.get("intentionallyNotCovered")
    if not isinstance(excluded, list) or not excluded:
        raise BuildError("exports.json must state the intentionally uncovered API surface")


def verify_config_pin(lock: dict[str, Any], native_dir: Path = NATIVE_DIR) -> Path:
    config = lock["configuration"]
    header = native_dir / PurePosixPath(config["header"])
    if not header.is_file() or header.is_symlink():
        raise BuildError(f"pinned config header is missing or not a regular file: {header}")
    actual = hashlib.sha256(header.read_bytes()).hexdigest()
    if actual != config["sha256"]:
        raise BuildError(f"config SHA-256 mismatch for {header.name}: expected {config['sha256']}, got {actual}")
    return header


def project_scratch(project_dir: Path = PROJECT_DIR) -> Path:
    # Check each owned component BEFORE mkdir/resolve can follow a redirect (including
    # dangling symlinks). A symlink elsewhere inside the repo is not an allowed scratch.
    project = project_dir.resolve(strict=True)
    scratch = project
    for name in (".tmp", "native"):
        scratch = scratch / name
        if scratch.is_symlink():
            raise BuildError("native scratch components may not be symlinks")
        scratch.mkdir(exist_ok=True)
        if scratch.resolve(strict=True) != scratch:
            raise BuildError("native scratch is not the exact standalone .tmp/native directory")
    return scratch


def safe_child(root: Path, *parts: str) -> Path:
    if root.is_symlink():
        raise BuildError("scratch root may not be a symlink")
    base = root.resolve(strict=True)
    candidate = base.joinpath(*parts)
    if any(part in ("", ".", "..") or "/" in part or "\\" in part for part in parts):
        raise BuildError("unsafe scratch path component")
    resolved = candidate.resolve(strict=False)
    try:
        resolved.relative_to(base)
    except ValueError as error:
        raise BuildError("scratch path escapes the standalone .tmp/native directory") from error
    for parent in (candidate, *candidate.parents):
        if parent == base.parent:
            break
        if parent.is_symlink():
            raise BuildError("scratch paths may not traverse symlinks")
    return candidate


def verify_archive(archive: Path, expected_sha: str) -> None:
    if not archive.is_file() or archive.stat().st_size > MAX_ARCHIVE_BYTES:
        raise BuildError("pinned upstream archive is missing or exceeds the size limit")
    actual = hashlib.sha256(archive.read_bytes()).hexdigest()
    if actual != expected_sha:
        raise BuildError(f"upstream archive SHA-256 mismatch: expected {expected_sha}, got {actual}")


def download_archive(url: str, archive: Path, log_path: Path) -> None:
    curl = shutil.which("curl")
    if not curl:
        raise BuildError("curl is required only for explicit source acquisition")
    archive.parent.mkdir(parents=True, exist_ok=True)
    command = [curl, "--fail", "--location", "--silent", "--show-error", "--connect-timeout", "10", "--max-time", "90", "--output", str(archive), url]
    try:
        with log_path.open("w", encoding="utf-8") as log:
            process = process_utils.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=100, check=False, text=True)
    except subprocess.TimeoutExpired as error:
        raise BuildError(f"source fetch timed out; diagnostics: {log_path}") from error
    if process.returncode != 0:
        raise BuildError(f"source fetch failed with exit code {process.returncode}; diagnostics: {log_path}")


def remove_cache_dir(path: Path) -> None:
    try:
        shutil.rmtree(path)
    except OSError as error:
        raise BuildError(f"cannot remove scratch cache {path}: {error}") from error


def extract_verified_archive(archive: Path, source_dir: Path, source_commit: str) -> None:
    expected_root = f"imgui-{source_commit}"
    if source_dir.exists() or source_dir.is_symlink():
        if source_dir.is_symlink() or source_dir.resolve(strict=False).parent != source_dir.parent.resolve(strict=True):
            raise BuildError("refusing to remove an unsafe cached source path")
        remove_cache_dir(source_dir)
    stage = source_dir.with_name(source_dir.name + ".extracting")
    if stage.exists() or stage.is_symlink():
        if stage.is_symlink() or stage.resolve(strict=False).parent != stage.parent.resolve(strict=True):
            raise BuildError("refusing to remove an unsafe extraction staging path")
        remove_cache_dir(stage)
    stage.mkdir(parents=True)
    total = 0
    count = 0
    try:
        with tarfile.open(archive, "r:gz") as bundle:
            members = bundle.getmembers()
            if len(members) > MAX_ARCHIVE_MEMBERS:
                raise BuildError("pinned archive contains too many entries")
            prefix = expected_root + "/"
            for member in members:
                if member.name == expected_root:
                    continue
                if not member.name.startswith(prefix):
                    raise BuildError(f"unexpected upstream archive path: {member.name}")
                path = PurePosixPath(member.name)
                relative = path.relative_to(expected_root)
                if relative.is_absolute() or ".." in relative.parts or not relative.parts:
                    raise BuildError(f"unsafe upstream archive path: {member.name}")
                if member.issym() or member.islnk() or member.isdev() or not (member.isdir() or member.isfile()):
                    raise BuildError(f"unsupported upstream archive entry: {member.name}")
                count += 1
                total += member.size
                if count > MAX_ARCHIVE_MEMBERS or total > MAX_EXTRACTED_BYTES:
                    raise BuildError("pinned source exceeds extraction limits")
                destination = stage.joinpath(*relative.parts)
                if member.isdir():
                    destination.mkdir(parents=True, exist_ok=True)
                else:
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    source = bundle.extractfile(member)
                    if source is None:
                        raise BuildError(f"missing data for archive member: {member.name}")
                    with source, destination.open("wb") as output:
                        shutil.copyfileobj(source, output)
        header = stage / "imgui.h"
        if not header.is_file() or '#define IMGUI_VERSION       "1.92.2"' not in header.read_text(encoding="utf-8"):
            raise BuildError("archive contents do not match the pinned Dear ImGui 1.92.2 source contract")
        source_dir.parent.mkdir(parents=True, exist_ok=True)
        os.replace(stage, source_dir)
    except (tarfile.TarError, OSError) as error:
        raise BuildError(f"cannot extract verified source archive: {error}") from error
    finally:
        if stage.exists() and not stage.is_symlink():
            shutil.rmtree(stage)


def run_logged(command: list[str], log_path: Path, timeout: int = PROCESS_TIMEOUT_SECONDS) -> None:
    try:
        with log_path.open("w", encoding="utf-8") as log:
            process = process_utils.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=False, text=True)
    except subprocess.TimeoutExpired as error:
        raise BuildError(f"native compile timed out; diagnostics: {log_path}") from error
    if process.returncode != 0:
        raise BuildError(f"native compile failed with exit code {process.returncode}; diagnostics: {log_path}")


def target_triple(lock: dict[str, Any], rid: str) -> str:
    return lock["targets"][rid]["zigTarget"]


def macos_sdk_path() -> str:
    xcrun = shutil.which("xcrun")
    if not xcrun:
        raise BuildError("osx-arm64 source build requires xcrun from Xcode or Command Line Tools")
    try:
        result = process_utils.run([xcrun, "--sdk", "macosx", "--show-sdk-path"], capture_output=True, text=True, timeout=15, check=False)
    except subprocess.TimeoutExpired as error:
        raise BuildError("xcrun SDK discovery timed out") from error
    sdk = result.stdout.strip()
    if result.returncode != 0 or not sdk or not Path(sdk).is_dir():
        raise BuildError(f"could not locate the macOS SDK with xcrun: {result.stderr.strip()}")
    return sdk


def dependency_report(artifact: Path, rid: str, output_dir: Path) -> list[str]:
    report_path = safe_child(output_dir, "dependencies.txt")
    if rid == "osx-arm64":
        inspector = shutil.which("otool")
        command = [inspector, "-L", str(artifact)] if inspector else None
    else:
        inspector = shutil.which("objdump")
        command = [inspector, "-p", str(artifact)] if inspector else None
    if command is None:
        report_path.write_text("Dependency inspector unavailable; cross-target dependencies not audited.\n", encoding="utf-8")
        return []
    try:
        result = process_utils.run(command, capture_output=True, text=True, timeout=20, check=False)
    except subprocess.TimeoutExpired:
        report_path.write_text("Dependency inspection timed out; see build metadata.\n", encoding="utf-8")
        return []
    raw = result.stdout + result.stderr
    report_path.write_text(raw, encoding="utf-8")
    if result.returncode != 0:
        return []
    if rid == "osx-arm64":
        lines = [
            line.strip().split(" (compatibility version", 1)[0]
            for line in result.stdout.splitlines()[1:]
            if line.strip() and not line.strip().startswith("@rpath/libimgui.dylib")
        ]
    else:
        lines = []
        for line in result.stdout.splitlines():
            fields = line.strip().split()
            if len(fields) == 2 and fields[0] == "NEEDED":
                lines.append(fields[1])
            elif line.strip().startswith("DLL Name:"):
                lines.append(line.split(":", 1)[1].strip())
    return lines


def host_matches_target(rid: str) -> bool:
    machine = platform.machine().lower()
    return (rid == "osx-arm64" and sys.platform == "darwin" and machine in ("arm64", "aarch64")) or (
        rid == "linux-x64" and sys.platform.startswith("linux") and machine in ("x86_64", "amd64")
    ) or (rid == "win-x64" and sys.platform == "win32" and machine in ("x86_64", "amd64"))


class Vec2(ctypes.Structure):
    _fields_ = [("x", ctypes.c_float), ("y", ctypes.c_float)]


class Vec4(ctypes.Structure):
    _fields_ = [("x", ctypes.c_float), ("y", ctypes.c_float), ("z", ctypes.c_float), ("w", ctypes.c_float)]


class FloatRect(ctypes.Structure):
    _fields_ = [("Min", Vec2), ("Max", Vec2)]


def verify_host_library(artifact: Path, export_manifest: dict[str, Any], source_commit: str, rid: str) -> dict[str, Any]:
    library = ctypes.CDLL(str(artifact))
    missing: list[str] = []
    for section in ("managedImports", "dataExports", "extraFunctions"):
        for item in export_manifest[section]:
            name = item["name"]
            try:
                getattr(library, name)
            except AttributeError:
                missing.append(name)
    if missing:
        raise BuildError("native library is missing declared exports: " + ", ".join(missing))

    library.GetVersion.restype = ctypes.c_char_p
    version = (library.GetVersion() or b"").decode("ascii")
    if version != "1.92.2":
        raise BuildError(f"loaded native core reports unexpected ImGui version {version!r}")
    library.playground_imgui_probe_upstream_commit.restype = ctypes.c_char_p
    reported_commit = (library.playground_imgui_probe_upstream_commit() or b"").decode("ascii")
    if reported_commit != source_commit:
        raise BuildError(f"loaded probe reports upstream commit {reported_commit}, expected {source_commit}")
    library.playground_imgui_probe_target_rid.restype = ctypes.c_char_p
    reported_rid = (library.playground_imgui_probe_target_rid() or b"").decode("ascii")
    if reported_rid != rid:
        raise BuildError(f"loaded probe reports target {reported_rid}, expected {rid}")
    library.playground_imgui_probe_manifest_utf8.restype = ctypes.c_char_p
    encoded_manifest = library.playground_imgui_probe_manifest_utf8()
    try:
        probe = json.loads(encoded_manifest.decode("utf-8"))
    except (AttributeError, UnicodeDecodeError, json.JSONDecodeError) as error:
        raise BuildError(f"loaded ABI manifest is not valid UTF-8 JSON: {error}") from error
    if probe.get("schema") != "playground_imgui.native-abi" or probe.get("schemaVersion") != 1:
        raise BuildError("loaded ABI manifest has an unexpected protocol/schema")
    if probe.get("upstream", {}).get("sourceCommit") != source_commit or probe.get("targetRid") != rid:
        raise BuildError("loaded ABI manifest has mismatched source or RID metadata")
    types = probe.get("types")
    if not isinstance(types, list):
        raise BuildError("loaded ABI manifest has no native type layout array")
    layouts = {item.get("name"): item for item in types if isinstance(item, dict)}
    required_layouts = {
        "ImGuiIO", "ImGuiStyle", "ImVec2", "ImVec4", "PlaygroundImVec2", "PlaygroundImVec4", "PlaygroundFloatRect",
        "ImVector<ImWchar>", "ImTextureRef",
        "ImDrawVert", "ImDrawCmd", "ImDrawData", "ImDrawList", "ImTextureData", "ImFont", "ImFontAtlas",
        "ImVector<ImDrawVert>", "ImVector<ImDrawIdx>", "ImVector<ImDrawCmd>", "ImVector<ImDrawList*>", "ImVector<ImTextureData*>",
        "ImFontBaked", "ImFontGlyph", "ImFontConfig", "ImGuiPlatformIO", "ImRect",
        "ImGuiContext", "ImGuiDockNode", "ImGuiBoxSelectState", "ImGuiStackLevelInfo"
    }
    if not required_layouts.issubset(layouts):
        raise BuildError("loaded ABI layout is missing required public/internal prototype types")
    for layout in layouts.values():
        size = layout.get("sizeBytes")
        alignment = layout.get("alignmentBytes")
        fields = layout.get("fields")
        if not isinstance(size, int) or size <= 0 or not isinstance(alignment, int) or alignment <= 0 or not isinstance(fields, list):
            raise BuildError(f"invalid native layout metadata for {layout.get('name')!r}")
        for field in fields:
            if not isinstance(field, dict):
                raise BuildError(f"invalid native field metadata for {layout.get('name')!r}")
            offset, width, field_alignment = field.get("offsetBytes"), field.get("sizeBytes"), field.get("alignmentBytes")
            if not isinstance(offset, int) or not isinstance(width, int) or not isinstance(field_alignment, int):
                raise BuildError(f"invalid native field widths for {layout.get('name')!r}")
            if offset < 0 or width <= 0 or field_alignment <= 0 or offset + width > size:
                raise BuildError(f"native field does not fit its measured {layout.get('name')} object")
    if not isinstance(probe.get("bitFieldsNotIndividuallyAddressable"), list):
        raise BuildError("native ABI manifest must explicitly identify bitfield measurement limits")

    contains = library.playground_imgui_abi_rect_contains
    contains.argtypes = [FloatRect, Vec2]
    contains.restype = ctypes.c_uint8
    rect = FloatRect(Vec2(1.0, 2.0), Vec2(9.0, 10.0))
    if contains(rect, Vec2(3.0, 4.0)) != 1 or contains(rect, Vec2(9.0, 4.0)) != 0:
        raise BuildError("loaded native rect/POD/bool ABI exercise failed")
    rect_to_vec4 = library.playground_imgui_abi_rect_to_vec4
    rect_to_vec4.argtypes = [FloatRect]
    rect_to_vec4.restype = Vec4
    converted = rect_to_vec4(rect)
    if (converted.x, converted.y, converted.z, converted.w) != (1.0, 2.0, 9.0, 10.0):
        raise BuildError("loaded native rect-to-float4 return ABI exercise failed")
    color = library.ColorConvertU32ToFloat4
    color.argtypes = [ctypes.c_uint32]
    color.restype = Vec4
    converted_color = color(0x80402010)
    expected_color = (16 / 255.0, 32 / 255.0, 64 / 255.0, 128 / 255.0)
    actual_color = (converted_color.x, converted_color.y, converted_color.z, converted_color.w)
    if any(abs(actual_color[index] - expected_color[index]) > 0.001 for index in range(len(expected_color))):
        raise BuildError("loaded native core float4 return conversion failed")
    allocate = library.MemAlloc
    allocate.argtypes = [ctypes.c_size_t]
    allocate.restype = ctypes.c_void_p
    release = library.MemFree
    release.argtypes = [ctypes.c_void_p]
    block = allocate(32)
    if not block:
        raise BuildError("ImGui allocator probe returned null")
    release(block)

    return {
        "loaded": True,
        "version": version,
        "exportsFound": len(export_manifest["managedImports"]) + len(export_manifest["dataExports"]) + len(export_manifest["extraFunctions"]),
        "manifestTypes": len(probe.get("types", [])),
        "nativeExercises": ["rect argument + one-byte bool result", "float4 return from rect conversion", "float4 return from upstream color conversion", "ImGui MemAlloc/MemFree"],
        "runtimeMeaning": "host macOS ctypes load and native calls only; not a managed BRUTAL ABI certification"
    }


def build_target(rid: str, lock: dict[str, Any], export_manifest: dict[str, Any], zig_path: str, zig_version: str, source_dir: Path, config_header: Path, scratch: Path) -> Path:
    if rid not in SUPPORTED_TARGETS:
        raise BuildError(f"unsupported RID {rid!r}; supported targets are {', '.join(sorted(SUPPORTED_TARGETS))}")
    target_info = lock["targets"][rid]
    output_dir = safe_child(scratch, rid)
    output_dir.mkdir(parents=True, exist_ok=True)
    stage_dir = safe_child(output_dir.resolve(strict=True), "staging")
    stage_dir.mkdir(parents=True, exist_ok=True)
    final_artifact = safe_child(output_dir.resolve(strict=True), target_info["file"])
    stage_artifact = safe_child(stage_dir.resolve(strict=True), target_info["file"])
    if stage_artifact.exists():
        stage_artifact.unlink()

    adapter = NATIVE_DIR / "src" / "PlaygroundBrutalAdapter.cpp"
    compile_sources = [source_dir / name for name in lock["build"]["sourceFiles"] if name != "src/PlaygroundBrutalAdapter.cpp"]
    compile_sources.append(adapter)
    missing_sources = [str(path) for path in compile_sources if not path.is_file()]
    if missing_sources:
        raise BuildError("pinned source tree lacks build inputs: " + ", ".join(missing_sources))
    command = [
        zig_path, "c++", "-std=c++11", "-O2", "-DNDEBUG", "-fPIC", "-fvisibility=hidden",
        "-fno-exceptions", "-fno-rtti", "-fno-threadsafe-statics", "-nostdlib++",
        "-target", target_triple(lock, rid), "-I", str(source_dir), "-include", str(config_header),
        f'-DPLAYGROUND_IMGUI_TARGET_RID="{rid}"',
    ]
    if rid == "win-x64":
        command.append("-DPLAYGROUND_IMGUI_WINDOWS=1")
    if rid == "osx-arm64":
        command.extend(["-isysroot", macos_sdk_path(), "-mmacosx-version-min=11.0", "-dynamiclib", "-Wl,-install_name,@rpath/libimgui.dylib"])
    elif rid == "linux-x64":
        command.extend(["-shared", "-Wl,-soname,libimgui.so"])
    else:
        command.append("-shared")
    command.extend([str(path) for path in compile_sources])
    command.extend(["-o", str(stage_artifact)])

    log_path = safe_child(output_dir.resolve(strict=True), "build.log")
    run_logged(command, log_path)
    if not stage_artifact.is_file() or stage_artifact.stat().st_size == 0:
        raise BuildError(f"compiler returned success without a native output; diagnostics: {log_path}")
    expected_arch = {"osx-arm64": "arm64", "linux-x64": "x86-64", "win-x64": "x86-64"}[rid]
    inspector = shutil.which("file")
    if inspector:
        file_result = process_utils.run([inspector, str(stage_artifact)], capture_output=True, text=True, timeout=15, check=False)
        file_description = (file_result.stdout or file_result.stderr).strip()
        if file_result.returncode != 0 or expected_arch not in file_description:
            raise BuildError(f"native output architecture check failed for {rid}: {file_description}; diagnostics: {log_path}")
    else:
        file_description = "file utility unavailable; output format/architecture not inspected"

    dependencies = dependency_report(stage_artifact, rid, output_dir)
    sha = hashlib.sha256(stage_artifact.read_bytes()).hexdigest()
    os.replace(stage_artifact, final_artifact)
    metadata: dict[str, Any] = {
        "schema": "playground_imgui.native-build-record",
        "schemaVersion": 1,
        "rid": rid,
        "zigTarget": target_triple(lock, rid),
        "artifact": final_artifact.name,
        "sha256": sha,
        "bytes": final_artifact.stat().st_size,
        "fileDescription": file_description,
        "sourceCommit": lock["upstream"]["sourceCommit"],
        "sourceArchiveSha256": lock["upstream"]["archiveSha256"],
        "configSha256": lock["configuration"]["sha256"],
        "zigVersion": zig_version,
        "command": command,
        "nativeDependenciesInspected": dependencies,
        "hostArtifactLoadTested": False,
        "targetRuntimeValidated": False,
        "brutalCompatibilityClaim": "bounded subset prototype only"
    }
    safe_child(output_dir, "build.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(f"built {rid}: {final_artifact} ({metadata['bytes']} bytes, SHA-256 {sha})")
    print(f"  dependencies inspected: {', '.join(dependencies) if dependencies else 'see dependencies.txt; no target execution inferred'}")
    if host_matches_target(rid):
        load_result = verify_host_library(final_artifact, export_manifest, lock["upstream"]["sourceCommit"], rid)
        metadata["hostLoadCheck"] = load_result
        metadata["hostArtifactLoadTested"] = True
        safe_child(output_dir, "build.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        print(f"  host load: {load_result['version']}; {load_result['exportsFound']} exports; {load_result['manifestTypes']} native layouts measured")
    else:
        metadata["hostLoadCheck"] = {"loaded": False, "reason": "cross target; no foreign execution was attempted"}
        safe_child(output_dir, "build.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    return final_artifact


def run_self_tests(lock: dict[str, Any]) -> None:
    validate_lock(lock)
    validate_export_manifest(read_json(EXPORTS_PATH))
    verify_config_pin(lock)
    with tempfile.TemporaryDirectory(prefix="imgui-native-self-test-", dir=project_scratch()) as temporary:
        temp = Path(temporary)
        good_archive = temp / "good.tar.gz"
        good_archive.write_bytes(b"pinned-test-data")
        digest = hashlib.sha256(good_archive.read_bytes()).hexdigest()
        verify_archive(good_archive, digest)
        try:
            verify_archive(good_archive, "0" * 64)
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject a bad archive pin")
        try:
            safe_child(temp, "..", "escape")
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject a traversal path")
        try:
            safe_child(temp, "/absolute")
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject an absolute path")
        missing_commit = copy.deepcopy(lock)
        missing_commit["upstream"].pop("sourceCommit")
        try:
            validate_lock(missing_commit)
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject a missing source pin")
        mismatched = copy.deepcopy(lock)
        mismatched["configuration"]["sha256"] = "0" * 64
        try:
            verify_config_pin(mismatched)
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject a changed configuration")
        try:
            build_target("freebsd-x64", lock, {}, "zig", "0.17.0", temp, Path(__file__), temp)
        except BuildError:
            pass
        else:
            raise BuildError("self-test failed to reject an unsupported target")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--target", choices=sorted(SUPPORTED_TARGETS), help="build one explicitly selected RID")
    group.add_argument("--all", action="store_true", help="source-build all three RIDs in sequence")
    group.add_argument("--self-test", action="store_true", help="run bounded builder validation without fetching/compiling")
    parser.add_argument("--zig", default="zig", help="Zig 0.17.0 executable on PATH (default: zig)")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    try:
        args = parse_args(argv)
        lock = read_json(LOCK_PATH)
        validate_lock(lock)
        export_manifest = read_json(EXPORTS_PATH)
        validate_export_manifest(export_manifest)
        config_header = verify_config_pin(lock)
        if args.self_test:
            run_self_tests(lock)
            return 0
        zig_path = shutil.which(args.zig)
        if not zig_path:
            raise BuildError(f"Zig compiler not found: {args.zig}")
        try:
            result = process_utils.run([zig_path, "version"], capture_output=True, text=True, timeout=15, check=False)
        except subprocess.TimeoutExpired as error:
            raise BuildError("Zig version check timed out") from error
        zig_version = result.stdout.strip()
        expected_version = lock["toolchain"]["zigVersion"]
        if result.returncode != 0 or zig_version != expected_version:
            raise BuildError(f"expected Zig {expected_version}, found {zig_version or result.stderr.strip()}")
        scratch = project_scratch()
        archive = safe_child(scratch, f"imgui-{lock['upstream']['sourceCommit']}.tar.gz")
        fetch_log = safe_child(scratch, "source-fetch.log")
        if not archive.exists():
            download_archive(lock["upstream"]["archiveUrl"], archive, fetch_log)
        verify_archive(archive, lock["upstream"]["archiveSha256"])
        source_root = safe_child(scratch, "source")
        if source_root.is_symlink():
            raise BuildError("refusing a symlinked native source cache")
        source_root.mkdir(parents=True, exist_ok=True)
        source_dir = safe_child(source_root.resolve(strict=True), lock["upstream"]["sourceCommit"])
        extract_verified_archive(archive, source_dir, lock["upstream"]["sourceCommit"])
        targets = sorted(SUPPORTED_TARGETS) if args.all else [args.target]
        for rid in targets:
            build_target(rid, lock, export_manifest, zig_path, zig_version, source_dir, config_header, scratch)
        print("native prototype build complete; artifacts are scratch outputs only and were not promoted")
        return 0
    except KeyboardInterrupt:
        print("native build: interrupted; child tree terminated, scratch diagnostics retained", file=sys.stderr)
        return 130
    except BuildError as error:
        print(f"native build: {error}", file=sys.stderr)
        return 2
    except OSError as error:
        print(f"native build: operating system error: {error}", file=sys.stderr)
        return 2
    except Exception as error:
        print(f"native build: unexpected failure: {type(error).__name__}: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
