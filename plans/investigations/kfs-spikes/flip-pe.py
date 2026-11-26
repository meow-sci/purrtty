"""Set the PE machine field of an IL-only assembly COPY to ARM64 so it loads on Apple Silicon.

Usage: python3 flip-pe.py path/to/copy/of/KSA.dll      (never run this on the game install or the snapshot)
Spike 04 does the same thing in memory and needs no copy.
"""
import struct
import sys

path = sys.argv[1]
data = bytearray(open(path, "rb").read())
pe = struct.unpack_from("<I", data, 0x3C)[0]
before = struct.unpack_from("<H", data, pe + 4)[0]
struct.pack_into("<H", data, pe + 4, 0xAA64)
open(path, "wb").write(data)
print(f"{path}: machine {before:#06x} -> 0xaa64")
