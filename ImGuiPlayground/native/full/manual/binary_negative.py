"""Test-only mutations of real image symbol locations, preserving export names."""
import struct


def relocate(data, rid, target, replacement):
    """Redirect one symbol's location/section to an existing opposite-kind symbol.

    No fabricated image/measurement: inputs are the actual compiled fixture.
    Names remain intact, so name-only gates still pass on the mutated image.
    The mutated image is NEVER loaded/executed.
    """
    changed = bytearray(data)

    def unpack(fmt, offset):
        return struct.unpack_from('<' + fmt, data, offset)

    def string(offset):
        return data[offset:data.index(b'\0', offset)].decode('ascii')

    entries = {}
    if rid == 'osx-arm64':
        offset = 32
        for _ in range(unpack('I', 16)[0]):
            command, size = unpack('II', offset)
            if command == 2:
                symoff, count, stroff, _ = unpack('IIII', offset + 8)
                for i in range(count):
                    position = symoff + i * 16
                    name, kind = unpack('IB', position)
                    if not kind & 0xe0 and kind & 0x0e == 0x0e and kind & 1:
                        entries[string(stroff + name).removeprefix('_')] = position
            offset += size
        a, b = entries[target], entries[replacement]
        changed[a + 5] = data[b + 5]  # actual section index
        changed[a + 8:a + 16] = data[b + 8:b + 16]  # actual address
    elif rid == 'linux-x64':
        shoff = unpack('Q', 40)[0]
        entsize, count = unpack('HH', 58)
        sections = [unpack('IIQQQQIIQQ', shoff + i * entsize) for i in range(count)]
        for sec in sections:
            if sec[1] == 11:
                strings = sections[sec[6]][4]
                for position in range(sec[4], sec[4] + sec[5], sec[9]):
                    entries[string(strings + unpack('I', position)[0])] = position
        a, b = entries[target], entries[replacement]
        changed[a + 6:a + 16] = data[b + 6:b + 16]  # section and value; keep name/type
    elif rid == 'win-x64':
        pe = unpack('I', 0x3c)[0]
        optional = pe + 24
        sections = []
        for i in range(unpack('H', pe + 6)[0]):
            pos = optional + unpack('H', pe + 20)[0] + i * 40
            virtual_size, rva, raw_size, raw = unpack('IIII', pos + 8)
            sections.append((rva, max(virtual_size, raw_size), raw))

        def file_offset(rva):
            sec = next(s for s in sections if s[0] <= rva < s[0] + s[1])
            return sec[2] + rva - sec[0]

        export = file_offset(unpack('I', optional + 112)[0])
        funcs, names, ordinals = unpack('III', export + 28)
        for i in range(unpack('I', export + 24)[0]):
            name = string(file_offset(unpack('I', file_offset(names) + i * 4)[0]))
            ordinal = unpack('H', file_offset(ordinals) + i * 2)[0]
            entries[name] = file_offset(funcs) + ordinal * 4
        a, b = entries[target], entries[replacement]
        changed[a:a + 4] = data[b:b + 4]  # export RVA
    else:
        raise ValueError('unsupported binary target')
    if changed == data:
        raise ValueError('symbol-kind mutation did not change image')
    return bytes(changed)
