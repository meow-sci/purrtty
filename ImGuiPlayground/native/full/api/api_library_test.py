#!/usr/bin/env python3
"""Quiet smoke test of the actual macOS linked artifact, in a bounded child."""
from __future__ import annotations

import ctypes
import json
import sys
from pathlib import Path


def main():
    artifact, manifest = (Path(x) for x in sys.argv[1:])
    coverage = json.loads(manifest.read_text())
    library = ctypes.CDLL(str(artifact.resolve()))
    for row in coverage['implemented']:
        getattr(library, row['entryPoint'])
    library.GetVersion.restype = ctypes.c_char_p
    assert library.GetVersion() == b'1.92.2'
    library.CreateContext.argtypes = [ctypes.c_void_p]
    library.CreateContext.restype = ctypes.c_void_p
    library.DestroyContext.argtypes = [ctypes.c_void_p]
    library.GetIO.restype = ctypes.c_void_p
    library.GetIO_internal.argtypes = [ctypes.c_void_p]
    library.GetIO_internal.restype = ctypes.c_void_p
    context = library.CreateContext(None)
    assert context
    try:
        assert library.GetIO() == library.GetIO_internal(context)
    finally:
        library.DestroyContext(context)
    library.MemAlloc.argtypes = [ctypes.c_size_t]
    library.MemAlloc.restype = ctypes.c_void_p
    library.MemFree.argtypes = [ctypes.c_void_p]
    block = library.MemAlloc(32)
    assert block
    library.MemFree(block)


if __name__ == '__main__':
    main()
