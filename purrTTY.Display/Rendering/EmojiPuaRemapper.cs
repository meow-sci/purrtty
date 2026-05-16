using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using purrTTY.Logging;

namespace purrTTY.Display.Rendering;

/// <summary>
///     Workaround for KSA's bundled ImGui being built with 16-bit ImWchar. The
///     wrapper's PInvoke signatures confirm it (every glyph-lookup function takes
///     <c>ushort</c>: <c>IsGlyphInFont</c>, <c>FindGlyph</c>, <c>GetCharAdvance</c>,
///     <c>IsGlyphLoaded</c>, <c>AddRemapChar</c>). At that width the font loader
///     drops cmap entries for codepoints above U+FFFF during atlas baking, and the
///     text-rendering lookup would truncate them anyway, so supplementary-plane
///     emoji (😀 🚀 etc.) render as the fallback character.
///
///     This class rewrites NotoEmoji's <c>cmap</c> table at load time: existing
///     BMP entries are preserved, and each supplementary-plane (SMP) glyph gets an
///     additional Private Use Area (PUA) codepoint pointing at the same glyph ID.
///     The renderer (see <see cref="TerminalGridRenderer"/>) translates surrogate
///     pairs to the corresponding PUA codepoint before sending text to ImGui, so
///     the 16-bit lookup path can actually resolve the glyph.
///
///     PUA range: U+E100..U+EFFF (3840 slots, more than enough for every glyph in
///     NotoEmoji-Regular). Nerd Font icons live in scattered sub-ranges of the
///     same PUA block, but collisions are harmless: ImGui's merge semantics check
///     the primary terminal font first, so if Hack/JetBrains/etc. has e.g. the
///     Powerline arrow at U+E0A0, it wins and our emoji at that codepoint is
///     silently shadowed.
/// </summary>
public static class EmojiPuaRemapper
{
    /// <summary>First PUA codepoint assigned to a remapped SMP glyph.</summary>
    private const ushort PuaStart = 0xE100;

    /// <summary>Inclusive last PUA codepoint we'll use.</summary>
    private const ushort PuaEnd = 0xEFFF;

    private const uint TagCmap = 0x636D6170u; // 'cmap'
    private const uint TagHead = 0x68656164u; // 'head'

    /// <summary>SMP codepoint → PUA proxy codepoint. Populated by BuildRemappedFont.</summary>
    private static readonly Dictionary<uint, char> _smpToPua = new();

    // Pinned storage for the rewritten TTF bytes. ImGui's new atlas keeps the font
    // data pointer alive for the lifetime of the atlas (it loads glyphs lazily on
    // demand), so we cannot let the byte[] move under the GC. We pin once and hold
    // the handle for the mod's lifetime; the ~1 MB cost is negligible.
    private static byte[]? _pinnedBytes;
    private static GCHandle _pinHandle;

    /// <summary>Count of SMP codepoints successfully assigned a PUA proxy. For logging.</summary>
    public static int RemappedCount => _smpToPua.Count;

    /// <summary>
    ///     Returns the PUA proxy codepoint for an SMP emoji codepoint, or false if
    ///     that emoji wasn't present in NotoEmoji's cmap (or this class hasn't been
    ///     initialized).
    /// </summary>
    public static bool TryMapSmpToPua(uint smpCodepoint, out char puaChar)
    {
        return _smpToPua.TryGetValue(smpCodepoint, out puaChar);
    }

    /// <summary>
    ///     Reads NotoEmoji from disk, rewrites its cmap to expose SMP glyphs at PUA
    ///     codepoints, pins the resulting bytes, and returns the pointer + length
    ///     ready to feed into <c>AddFontFromMemoryTTF</c>. Populates the static
    ///     SMP→PUA mapping table as a side effect.
    /// </summary>
    public static (IntPtr ptr, int size) BuildAndPinRemappedFont(string ttfPath)
    {
        if (_pinHandle.IsAllocated)
        {
            _pinHandle.Free();
            _pinnedBytes = null;
        }

        _pinnedBytes = BuildRemappedFont(File.ReadAllBytes(ttfPath));
        _pinHandle = GCHandle.Alloc(_pinnedBytes, GCHandleType.Pinned);
        return (_pinHandle.AddrOfPinnedObject(), _pinnedBytes.Length);
    }

    /// <summary>
    ///     Core rewrite — exposed separately so tests can pass bytes directly.
    ///     Returns a fresh byte[] containing a TTF with the modified cmap.
    /// </summary>
    public static byte[] BuildRemappedFont(byte[] originalTtf)
    {
        _smpToPua.Clear();

        var span = (ReadOnlySpan<byte>)originalTtf;

        uint sfntVersion = ReadU32BE(span);
        ushort numTables = ReadU16BE(span[4..]);

        // Load every table into a dictionary so we can drop cmap with a new one
        // and rebuild the SFNT envelope from scratch.
        var tables = new SortedDictionary<uint, byte[]>(); // sorted by tag (SFNT requirement)
        for (int i = 0; i < numTables; i++)
        {
            var rec = span.Slice(12 + i * 16, 16);
            uint tag = ReadU32BE(rec);
            uint offset = ReadU32BE(rec[8..]);
            uint length = ReadU32BE(rec[12..]);
            byte[] data = new byte[length];
            span.Slice((int)offset, (int)length).CopyTo(data);
            tables[tag] = data;
        }

        if (!tables.TryGetValue(TagCmap, out var origCmap))
        {
            ModLog.Log.Warning("EmojiPuaRemapper: font has no cmap table; returning unchanged");
            return originalTtf;
        }

        // Pull out existing BMP entries (Format 4) and SMP entries (Format 12).
        var bmp = new SortedDictionary<ushort, uint>();
        var smp = new SortedDictionary<uint, uint>();
        ParseCmapSubtables(origCmap, bmp, smp);

        // Allocate PUA proxies for the SMP entries. Skips PUA slots already in use
        // by BMP mappings (defensive — NotoEmoji's BMP coverage doesn't touch the
        // E100..EFFF range in practice).
        AssignPuaCodepoints(smp, bmp);

        // Build one Format 4 subtable holding BMP + PUA mappings combined.
        byte[] newCmap = BuildCmapFormat4Wrapped(bmp);
        tables[TagCmap] = newCmap;

        byte[] result = WriteSfnt(sfntVersion, tables);
        ModLog.Log.Info(
            $"EmojiPuaRemapper: remapped {_smpToPua.Count} SMP emoji to PUA proxies " +
            $"(BMP entries: {bmp.Count - _smpToPua.Count}, new font size: {result.Length} bytes)");
        return result;
    }

    private static void ParseCmapSubtables(
        ReadOnlySpan<byte> cmap,
        SortedDictionary<ushort, uint> bmp,
        SortedDictionary<uint, uint> smp)
    {
        ushort numSubtables = ReadU16BE(cmap[2..]);
        for (int i = 0; i < numSubtables; i++)
        {
            var rec = cmap.Slice(4 + i * 8, 8);
            uint subOff = ReadU32BE(rec[4..]);
            var sub = cmap[(int)subOff..];
            ushort format = ReadU16BE(sub);
            switch (format)
            {
                case 4: ParseFormat4(sub, bmp); break;
                case 12: ParseFormat12(sub, smp); break;
                // Other formats (0, 2, 6, 13, 14) are ignored — Format 4 + Format 12
                // covers every modern emoji font's coverage.
            }
        }
    }

    private static void ParseFormat4(ReadOnlySpan<byte> sub, SortedDictionary<ushort, uint> bmp)
    {
        ushort segCountX2 = ReadU16BE(sub[6..]);
        int segCount = segCountX2 / 2;

        int endOff = 14;
        int startOff = endOff + segCount * 2 + 2; // +2 for reservedPad
        int deltaOff = startOff + segCount * 2;
        int rangeOff = deltaOff + segCount * 2;

        for (int i = 0; i < segCount; i++)
        {
            ushort endCount = ReadU16BE(sub[(endOff + i * 2)..]);
            ushort startCount = ReadU16BE(sub[(startOff + i * 2)..]);
            short idDelta = (short)ReadU16BE(sub[(deltaOff + i * 2)..]);
            ushort idRangeOffset = ReadU16BE(sub[(rangeOff + i * 2)..]);

            if (startCount == 0xFFFF) continue; // sentinel

            for (uint c = startCount; c <= endCount; c++)
            {
                uint glyphID;
                if (idRangeOffset == 0)
                {
                    glyphID = (uint)((c + idDelta) & 0xFFFF);
                }
                else
                {
                    int absOff = rangeOff + i * 2 + idRangeOffset + (int)((c - startCount) * 2);
                    if (absOff + 2 > sub.Length) continue;
                    ushort raw = ReadU16BE(sub[absOff..]);
                    if (raw == 0) continue;
                    glyphID = (uint)((raw + idDelta) & 0xFFFF);
                }
                if (glyphID != 0)
                {
                    bmp[(ushort)c] = glyphID;
                }
            }
        }
    }

    private static void ParseFormat12(ReadOnlySpan<byte> sub, SortedDictionary<uint, uint> smp)
    {
        uint numGroups = ReadU32BE(sub[12..]);
        for (uint g = 0; g < numGroups; g++)
        {
            var grp = sub.Slice(16 + (int)g * 12, 12);
            uint startCharCode = ReadU32BE(grp);
            uint endCharCode = ReadU32BE(grp[4..]);
            uint startGlyphID = ReadU32BE(grp[8..]);

            for (uint c = startCharCode; c <= endCharCode; c++)
            {
                if (c <= 0xFFFF) continue; // BMP entries handled via Format 4 already
                uint glyphID = startGlyphID + (c - startCharCode);
                if (glyphID != 0)
                {
                    smp[c] = glyphID;
                }
            }
        }
    }

    private static void AssignPuaCodepoints(
        SortedDictionary<uint, uint> smp,
        SortedDictionary<ushort, uint> bmp)
    {
        ushort nextPua = PuaStart;
        foreach (var (smpCp, glyphID) in smp)
        {
            // Skip PUA slots already occupied by real BMP mappings.
            while (nextPua <= PuaEnd && bmp.ContainsKey(nextPua))
            {
                if (nextPua == PuaEnd) { nextPua++; break; }
                nextPua++;
            }
            if (nextPua > PuaEnd)
            {
                ModLog.Log.Warning(
                    $"EmojiPuaRemapper: PUA range exhausted after {_smpToPua.Count} emoji; " +
                    "remaining SMP glyphs not remapped");
                break;
            }

            bmp[nextPua] = glyphID;
            _smpToPua[smpCp] = (char)nextPua;
            if (nextPua == PuaEnd) break;
            nextPua++;
        }
    }

    private sealed class Format4Segment
    {
        public ushort StartCount;
        public ushort EndCount;
        public short IdDelta;
        public bool UseIdRangeOffset;
        public readonly List<uint> GlyphIDs = new();

        /// <summary>
        ///     True if all glyph IDs in this segment form a linear sequence with a
        ///     delta that fits in <see cref="short"/> (so we can use the cheaper
        ///     idDelta encoding instead of idRangeOffset + glyphIdArray).
        /// </summary>
        public bool TryLinearDelta(out short delta)
        {
            delta = 0;
            if (GlyphIDs.Count == 0) return false;
            uint first = GlyphIDs[0];
            for (int i = 1; i < GlyphIDs.Count; i++)
            {
                if (GlyphIDs[i] != first + (uint)i) return false;
            }
            long d = (long)first - (long)StartCount;
            if (d < short.MinValue || d > short.MaxValue) return false;
            delta = (short)d;
            return true;
        }
    }

    private static byte[] BuildCmapFormat4Wrapped(SortedDictionary<ushort, uint> mappings)
    {
        // Group consecutive-codepoint mappings into segments.
        var segments = new List<Format4Segment>();
        Format4Segment? cur = null;
        foreach (var (cp, gid) in mappings)
        {
            if (cur == null || cp != cur.EndCount + 1)
            {
                cur = new Format4Segment { StartCount = cp, EndCount = cp };
                cur.GlyphIDs.Add(gid);
                segments.Add(cur);
            }
            else
            {
                cur.EndCount = cp;
                cur.GlyphIDs.Add(gid);
            }
        }

        foreach (var s in segments)
        {
            if (s.TryLinearDelta(out short delta))
            {
                s.IdDelta = delta;
                s.UseIdRangeOffset = false;
            }
            else
            {
                s.IdDelta = 0;
                s.UseIdRangeOffset = true;
            }
        }

        // Mandatory terminator segment.
        var sentinel = new Format4Segment
        {
            StartCount = 0xFFFF,
            EndCount = 0xFFFF,
            IdDelta = 1,
            UseIdRangeOffset = false,
        };
        sentinel.GlyphIDs.Add(0);
        segments.Add(sentinel);

        int segCount = segments.Count;
        int segCountX2 = segCount * 2;
        int log2Sc = (int)Math.Floor(Math.Log2(segCount));
        int searchRange = (1 << log2Sc) * 2;
        int entrySelector = log2Sc;
        int rangeShift = segCountX2 - searchRange;

        int glyphIdArrayLength = 0;
        foreach (var s in segments)
        {
            if (s.UseIdRangeOffset)
            {
                glyphIdArrayLength += s.GlyphIDs.Count;
            }
        }

        const int headerSize = 14;
        int segArraysSize = segCount * 2 * 4 + 2; // 4 segCount-sized uint16 arrays + reservedPad
        int subtableLength = headerSize + segArraysSize + glyphIdArrayLength * 2;

        if (subtableLength > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"cmap Format 4 subtable would be {subtableLength} bytes, exceeding the 65535-byte uint16 length limit");
        }

        byte[] sub = new byte[subtableLength];
        var s2 = sub.AsSpan();
        WriteU16BE(s2, 4);
        WriteU16BE(s2[2..], (ushort)subtableLength);
        WriteU16BE(s2[4..], 0);                 // language
        WriteU16BE(s2[6..], (ushort)segCountX2);
        WriteU16BE(s2[8..], (ushort)searchRange);
        WriteU16BE(s2[10..], (ushort)entrySelector);
        WriteU16BE(s2[12..], (ushort)rangeShift);

        int endOff = 14;
        int startOff = endOff + segCount * 2 + 2;
        int deltaOff = startOff + segCount * 2;
        int rangeOff = deltaOff + segCount * 2;
        int glyphArrOff = rangeOff + segCount * 2;

        int glyphPos = 0;
        for (int i = 0; i < segCount; i++)
        {
            var s = segments[i];
            WriteU16BE(s2[(endOff + i * 2)..], s.EndCount);
            WriteU16BE(s2[(startOff + i * 2)..], s.StartCount);
            WriteU16BE(s2[(deltaOff + i * 2)..], (ushort)s.IdDelta);

            if (s.UseIdRangeOffset)
            {
                // Spec: glyphID = *(&idRangeOffset[i] + idRangeOffset[i]/2 + (c - startCount[i]))
                // So idRangeOffset[i] (in bytes) = (segCount - i) * 2 + glyphPos * 2
                int off = (segCount - i) * 2 + glyphPos * 2;
                WriteU16BE(s2[(rangeOff + i * 2)..], (ushort)off);
                foreach (uint g in s.GlyphIDs)
                {
                    WriteU16BE(s2[(glyphArrOff + glyphPos * 2)..], (ushort)g);
                    glyphPos++;
                }
            }
            else
            {
                WriteU16BE(s2[(rangeOff + i * 2)..], 0);
            }
        }

        // Wrap subtable in a cmap table with a single Microsoft/Unicode-BMP encoding record.
        const int cmapHeaderSize = 4 + 8;
        byte[] cmap = new byte[cmapHeaderSize + subtableLength];
        var cs = cmap.AsSpan();
        WriteU16BE(cs, 0);                          // version
        WriteU16BE(cs[2..], 1);                     // numTables
        WriteU16BE(cs[4..], 3);                     // platformID = Microsoft
        WriteU16BE(cs[6..], 1);                     // encodingID = Unicode BMP
        WriteU32BE(cs[8..], (uint)cmapHeaderSize);  // offset to subtable
        sub.CopyTo(cmap.AsSpan(cmapHeaderSize));
        return cmap;
    }

    private static byte[] WriteSfnt(uint sfntVersion, SortedDictionary<uint, byte[]> tables)
    {
        int numTables = tables.Count;
        const int headerSize = 12;
        int dirSize = numTables * 16;
        int dataStart = headerSize + dirSize;

        // Compute offsets (each table padded to 4-byte boundary).
        var offsets = new Dictionary<uint, int>();
        int currentOffset = dataStart;
        foreach (var (tag, data) in tables)
        {
            offsets[tag] = currentOffset;
            int padded = (data.Length + 3) & ~3;
            currentOffset += padded;
        }

        byte[] result = new byte[currentOffset];
        var rs = result.AsSpan();

        int log2 = (int)Math.Floor(Math.Log2(numTables));
        int sr = (1 << log2) * 16;
        int es = log2;
        int rsh = numTables * 16 - sr;

        WriteU32BE(rs, sfntVersion);
        WriteU16BE(rs[4..], (ushort)numTables);
        WriteU16BE(rs[6..], (ushort)sr);
        WriteU16BE(rs[8..], (ushort)es);
        WriteU16BE(rs[10..], (ushort)rsh);

        int recOff = headerSize;
        foreach (var (tag, data) in tables)
        {
            int off = offsets[tag];
            data.CopyTo(rs[off..]);
            uint checksum = ComputeChecksum(data);
            WriteU32BE(rs[recOff..], tag);
            WriteU32BE(rs[(recOff + 4)..], checksum);
            WriteU32BE(rs[(recOff + 8)..], (uint)off);
            WriteU32BE(rs[(recOff + 12)..], (uint)data.Length);
            recOff += 16;
        }

        // head.checkSumAdjustment must be (0xB1B0AFBA - whole-file checksum) AFTER
        // first zeroing the field. See OpenType spec, head table description.
        if (offsets.TryGetValue(TagHead, out int headOff))
        {
            int csaOff = headOff + 8;
            WriteU32BE(rs[csaOff..], 0);
            uint fileSum = ComputeChecksum(result);
            WriteU32BE(rs[csaOff..], 0xB1B0AFBAu - fileSum);
        }

        return result;
    }

    /// <summary>
    ///     OpenType table checksum: sum of uint32 big-endian words, mod 2^32.
    ///     Trailing bytes are zero-padded to a uint32 boundary before summing.
    /// </summary>
    private static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        while (i + 4 <= data.Length)
        {
            sum += ReadU32BE(data[i..]);
            i += 4;
        }
        if (i < data.Length)
        {
            uint tail = 0;
            for (int j = 0; j < 4; j++)
            {
                tail <<= 8;
                if (i + j < data.Length) tail |= data[i + j];
            }
            sum += tail;
        }
        return sum;
    }

    private static uint ReadU32BE(ReadOnlySpan<byte> s) => BinaryPrimitives.ReadUInt32BigEndian(s);
    private static ushort ReadU16BE(ReadOnlySpan<byte> s) => BinaryPrimitives.ReadUInt16BigEndian(s);
    private static void WriteU32BE(Span<byte> s, uint v) => BinaryPrimitives.WriteUInt32BigEndian(s, v);
    private static void WriteU16BE(Span<byte> s, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(s, v);
}
