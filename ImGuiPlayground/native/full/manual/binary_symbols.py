"""Inspect loaded-image symbol sections, not a name-only export success check."""
import struct


def inspect(data, rid, names):
    def unpack(fmt, offset):
        return struct.unpack_from('<' + fmt, data, offset)

    def string(offset):
        return data[offset:data.index(b'\0', offset)].decode('ascii')

    result = {}
    if rid == 'osx-arm64':
        if unpack('I', 0)[0] != 0xfeedfacf:
            raise ValueError('not little-endian Mach-O64')
        ncmds = unpack('I', 16)[0]
        offset = 32
        sections = []
        symtab = None
        for _ in range(ncmds):
            command, size = unpack('II', offset)
            if command == 0x19:
                initprot, nsects = unpack('II', offset + 60)
                for i in range(nsects):
                    sec = offset + 72 + i * 80
                    address, length = unpack('QQ', sec + 32)
                    sections.append((address, length, bool(initprot & 2), bool(initprot & 4)))
            if command == 2:
                symtab = unpack('IIII', offset + 8)
            offset += size
        if symtab is None:
            raise ValueError('no Mach-O symbol table')
        symoff, count, stroff, _ = symtab
        for i in range(count):
            name_offset, kind, section, _, address = unpack('IBBHQ', symoff + i * 16)
            if kind & 0xe0 or kind & 0x0e != 0x0e or not kind & 1:
                continue
            name = string(stroff + name_offset).removeprefix('_')
            if name in names:
                base, length, writable, executable = sections[section - 1]
                result[name] = {'address': address, 'writable': writable, 'executable': executable, 'sectionRemaining': base + length - address}
    elif rid == 'linux-x64':
        if data[:6] != b'\x7fELF\x02\x01':
            raise ValueError('not little-endian ELF64')
        shoff = unpack('Q', 40)[0]
        entsize, count = unpack('HH', 58)
        sections = [unpack('IIQQQQIIQQ', shoff + i * entsize) for i in range(count)]
        for sec in sections:
            if sec[1] != 11:  # SHT_DYNSYM
                continue
            strings = sections[sec[6]][4]
            for offset in range(sec[4], sec[4] + sec[5], sec[9]):
                name_offset, info, _, index, address, size = unpack('IBBHQQ', offset)
                name = string(strings + name_offset)
                if name in names:
                    flags = sections[index][2]
                    result[name] = {'address': address, 'writable': bool(flags & 1), 'executable': bool(flags & 4), 'bytes': size, 'type': info & 15}
    elif rid == 'win-x64':
        pe = unpack('I', 0x3c)[0]
        if data[pe:pe + 4] != b'PE\0\0':
            raise ValueError('not PE')
        count = unpack('H', pe + 6)[0]
        optional_size = unpack('H', pe + 20)[0]
        optional = pe + 24
        if unpack('H', optional)[0] != 0x20b:
            raise ValueError('not PE32+')
        sections = []
        for i in range(count):
            sec = optional + optional_size + i * 40
            virtual_size, rva, raw_size, raw_offset = unpack('IIII', sec + 8)
            flags = unpack('I', sec + 36)[0]
            sections.append((rva, max(virtual_size, raw_size), raw_offset, flags))

        def locate(rva):
            return next(s for s in sections if s[0] <= rva < s[0] + s[1])

        def file_offset(rva):
            s = locate(rva)
            return s[2] + rva - s[0]

        export_rva = unpack('I', optional + 112)[0]
        export = file_offset(export_rva)
        count = unpack('I', export + 24)[0]
        funcs, names_rva, ordinals = unpack('III', export + 28)
        for i in range(count):
            name = string(file_offset(unpack('I', file_offset(names_rva) + i * 4)[0]))
            if name in names:
                ordinal = unpack('H', file_offset(ordinals) + i * 2)[0]
                address = unpack('I', file_offset(funcs) + ordinal * 4)[0]
                sec = locate(address)
                result[name] = {'address': address, 'writable': bool(sec[3] & 0x80000000), 'executable': bool(sec[3] & 0x20000000), 'sectionRemaining': sec[0] + sec[1] - address}
    else:
        raise ValueError('unsupported binary target')
    if set(result) != set(names):
        raise ValueError('binary symbol section coverage')
    addresses = [result[name]['address'] for name in names if name.startswith('Platform_')]
    if len(addresses) != 3 or any(abs(a - b) < 8 for i, a in enumerate(addresses) for b in addresses[i + 1:]):
        raise ValueError('callback slots overlap or are missing')
    for name, symbol in result.items():
        slot = name.startswith('Platform_')
        if slot:
            if not symbol['writable'] or symbol['executable'] or symbol['address'] % 8:
                raise ValueError('not aligned writable slot: ' + name)
            if 'bytes' in symbol and (symbol['bytes'] != 8 or symbol['type'] != 1):
                raise ValueError('ELF slot type/size: ' + name)
            if symbol.get('sectionRemaining', 8) < 8:
                raise ValueError('slot outside section: ' + name)
        elif symbol['writable'] or not symbol['executable']:
            raise ValueError('not executable function: ' + name)
    return result
