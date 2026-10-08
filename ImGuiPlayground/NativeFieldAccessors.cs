using System.Runtime.InteropServices;
using System.Text;

namespace ImGuiPlayground;

public enum NativeRecordKind
{
    ImFontGlyph = 0,
    ImFontBaked = 1,
    ImGuiStyleVarInfo = 2,
    ImGuiBoxSelectState = 3,
    ImGuiDockNode = 4,
    ImGuiStackLevelInfo = 5,
    ImGuiContext = 6,
    ImGuiWindow = 7,
    ImGuiTableColumn = 8,
    ImGuiTable = 9,
    ImGuiTableColumnSettings = 10,
    ImFontAtlasRectEntry = 11,
}

public enum NativePackedField
{
    ImFontGlyph_Colored = 0,
    ImFontGlyph_Visible = 1,
    ImFontGlyph_SourceIdx = 2,
    ImFontGlyph_Codepoint = 3,
    ImFontBaked_MetricsTotalSurface = 4,
    ImFontBaked_WantDestroy = 5,
    ImFontBaked_LoadNoFallback = 6,
    ImFontBaked_LoadNoRenderOnLayout = 7,
    ImGuiStyleVarInfo_Count = 8,
    ImGuiStyleVarInfo_DataType = 9,
    ImGuiStyleVarInfo_Offset = 10,
    ImGuiBoxSelectState_KeyMods = 11,
    ImGuiDockNode_AuthorityForPos = 12,
    ImGuiDockNode_AuthorityForSize = 13,
    ImGuiDockNode_AuthorityForViewport = 14,
    ImGuiDockNode_IsVisible = 15,
    ImGuiDockNode_IsFocused = 16,
    ImGuiDockNode_IsBgDrawnThisFrame = 17,
    ImGuiDockNode_HasCloseButton = 18,
    ImGuiDockNode_HasWindowMenuButton = 19,
    ImGuiDockNode_HasCentralNodeChild = 20,
    ImGuiDockNode_WantCloseAll = 21,
    ImGuiDockNode_WantLockSizeOnce = 22,
    ImGuiDockNode_WantMouseMove = 23,
    ImGuiDockNode_WantHiddenTabBarUpdate = 24,
    ImGuiDockNode_WantHiddenTabBarToggle = 25,
    ImGuiStackLevelInfo_DataType = 26,
    ImGuiContext_ActiveIdMouseButton = 27,
    ImGuiWindow_SetWindowPosAllowFlags = 28,
    ImGuiWindow_SetWindowSizeAllowFlags = 29,
    ImGuiWindow_SetWindowCollapsedAllowFlags = 30,
    ImGuiWindow_SetWindowDockAllowFlags = 31,
    ImGuiWindow_DockIsActive = 32,
    ImGuiWindow_DockNodeIsVisible = 33,
    ImGuiWindow_DockTabIsVisible = 34,
    ImGuiWindow_DockTabWantClose = 35,
    ImGuiTableColumn_SortDirection = 36,
    ImGuiTableColumn_SortDirectionsAvailCount = 37,
    ImGuiTableColumn_SortDirectionsAvailMask = 38,
    ImGuiTable_RowFlags = 39,
    ImGuiTable_LastRowFlags = 40,
    ImGuiTableColumnSettings_SortDirection = 41,
    ImGuiTableColumnSettings_IsEnabled = 42,
    ImGuiTableColumnSettings_IsStretch = 43,
    ImFontAtlasRectEntry_TargetIndex = 44,
    ImFontAtlasRectEntry_Generation = 45,
    ImFontAtlasRectEntry_IsUsed = 46,
}

public enum NativeOpaqueShape
{
    NamedKeys = 0,
    TableSettings = 1,
    WindowSettings = 2,
    DrawListSharedData = 3,
    FileHandle = 4,
    MultiSelectPool = 5,
    TabBarPool = 6,
    TablePool = 7,
    CellSpan = 8,
    ColumnSpan = 9,
    ShortSpan = 10,
    BakedVector = 11,
    TextEditState = 12,
}

/// <summary>Representationally safe operations on LIVE correctly typed native objects.
/// The caller owns native library/context/thread/lifetimes and must invalidate scopes
/// BEFORE external destruction or structural mutation. This cannot detect stale pointers
/// supplied by a caller, and does not validate live-engine domains or invariants.
/// Never frees the injected library handle or creates a competing resolver/context.
/// Dispose before the owner unloads the library. All operations are thread-affine.</summary>
public sealed unsafe class NativeFieldAccessors : IDisposable
{
    public const string ExpectedIdentity = "e86ced3fb638c94c285378697c77e6e723bd75864c337e2b557449b4e4677f72";
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool disposed;
    private readonly delegate* unmanaged[Cdecl]<int, FieldInfo*, int> info;
    private readonly delegate* unmanaged[Cdecl]<int, nint, int, long*, int> get;
    private readonly delegate* unmanaged[Cdecl]<int, nint, int, long, int> set;
    private readonly delegate* unmanaged[Cdecl]<int> styleCount;
    private readonly delegate* unmanaged[Cdecl]<int, Triple*, int> style;
    private readonly delegate* unmanaged[Cdecl]<nint, int*, int> rectCount;
    private readonly delegate* unmanaged[Cdecl]<nint, int, Triple*, int> rect;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int, long, int> rectSet;
    private readonly delegate* unmanaged[Cdecl]<byte*, int> emptyString;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int*, int> textCopy;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int> textAppend;
    private readonly delegate* unmanaged[Cdecl]<nint, int> textClear;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int, int*, int> key;
    private readonly delegate* unmanaged[Cdecl]<int, nint, int*, int> count;
    private readonly delegate* unmanaged[Cdecl]<int, nint, int, nint*, int> at;
    private readonly delegate* unmanaged[Cdecl]<int, nint, uint, nint*, int> lookup;
    private readonly delegate* unmanaged[Cdecl]<int, nint, int*, int> alive;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int*, int> shortAt;
    private readonly delegate* unmanaged[Cdecl]<nint, int, CellSnapshot*, int> cellAt;
    private readonly delegate* unmanaged[Cdecl]<nint, SharedSnapshot*, int> shared;
    private readonly delegate* unmanaged[Cdecl]<nint, TextEditSnapshot*, int> textedit;
    private readonly delegate* unmanaged[Cdecl]<nint, ulong*, int> fileSize;

    [StructLayout(LayoutKind.Sequential)]
    public struct FieldInfo
    {
        public NativeRecordKind Record;
        public int OriginalBits, EffectiveBits, Signed, ReadOnly, Boolean;
        public readonly long Minimum => Signed != 0 ? -(1L << (EffectiveBits - 1)) : 0;
        public readonly long Maximum => (1L << (EffectiveBits - Signed)) - 1;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Triple { public int A, B, C; }
    [StructLayout(LayoutKind.Sequential)]
    public struct SharedSnapshot
    {
        public float FontSize, FontScale, CurveTolerance, CircleError;
        public uint InitialFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct TextEditSnapshot { public int Cursor, SelectStart, SelectEnd, InsertMode; }
    [StructLayout(LayoutKind.Sequential)]
    public struct CellSnapshot { public uint Color; public int Column; }
    public readonly record struct StyleSnapshot(int Count, int DataType, int Offset);
    public readonly record struct RectSnapshot(int TargetIndex, int Generation, bool IsUsed);

    /// <summary>Explicit creation fails on ordinary assets without this exact supplemental ABI.
    /// The handle must stay loaded until this helper and all its borrowers are disposed.</summary>
    public NativeFieldAccessors(nint alreadyOwnedLibrary)
    {
        if (alreadyOwnedLibrary == 0) throw new ArgumentNullException(nameof(alreadyOwnedLibrary));
        nint Export(string name)
        {
            if (!NativeLibrary.TryGetExport(alreadyOwnedLibrary, name, out nint pointer))
                throw new NotSupportedException($"Native field accessors are unavailable: missing {name}.");
            return pointer;
        }
        var identity = (delegate* unmanaged[Cdecl]<byte*>)Export("purr_accessors_identity");
        byte* identityBytes = identity();
        if (identityBytes == null || Encoding.ASCII.GetString(new ReadOnlySpan<byte>(identityBytes, 64)) != ExpectedIdentity || identityBytes[64] != 0)
            throw new NotSupportedException("Native field accessor source/profile/ABI identity mismatch.");
        if (sizeof(FieldInfo) != 24 || sizeof(Triple) != 12 || sizeof(SharedSnapshot) != 20 || sizeof(TextEditSnapshot) != 16 || sizeof(CellSnapshot) != 8 || Marshal.OffsetOf<SharedSnapshot>(nameof(SharedSnapshot.InitialFlags)) != 16)
            throw new NotSupportedException("Native field accessor DTO layout mismatch.");
        info = (delegate* unmanaged[Cdecl]<int, FieldInfo*, int>)Export("purr_accessor_field_info");
        get = (delegate* unmanaged[Cdecl]<int, nint, int, long*, int>)Export("purr_accessor_get");
        set = (delegate* unmanaged[Cdecl]<int, nint, int, long, int>)Export("purr_accessor_set");
        styleCount = (delegate* unmanaged[Cdecl]<int>)Export("purr_accessor_style_count");
        style = (delegate* unmanaged[Cdecl]<int, Triple*, int>)Export("purr_accessor_style");
        rectCount = (delegate* unmanaged[Cdecl]<nint, int*, int>)Export("purr_accessor_rect_count");
        rect = (delegate* unmanaged[Cdecl]<nint, int, Triple*, int>)Export("purr_accessor_rect");
        rectSet = (delegate* unmanaged[Cdecl]<nint, int, int, long, int>)Export("purr_accessor_rect_set");
        emptyString = (delegate* unmanaged[Cdecl]<byte*, int>)Export("purr_accessor_empty_string");
        textCopy = (delegate* unmanaged[Cdecl]<nint, byte*, int, int*, int>)Export("purr_accessor_text_copy");
        textAppend = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)Export("purr_accessor_text_append");
        textClear = (delegate* unmanaged[Cdecl]<nint, int>)Export("purr_accessor_text_clear");
        key = (delegate* unmanaged[Cdecl]<nint, int, int, int*, int>)Export("purr_accessor_named_key");
        count = (delegate* unmanaged[Cdecl]<int, nint, int*, int>)Export("purr_accessor_count");
        at = (delegate* unmanaged[Cdecl]<int, nint, int, nint*, int>)Export("purr_accessor_at");
        lookup = (delegate* unmanaged[Cdecl]<int, nint, uint, nint*, int>)Export("purr_accessor_lookup");
        alive = (delegate* unmanaged[Cdecl]<int, nint, int*, int>)Export("purr_accessor_pool_alive");
        shortAt = (delegate* unmanaged[Cdecl]<nint, int, int*, int>)Export("purr_accessor_short");
        cellAt = (delegate* unmanaged[Cdecl]<nint, int, CellSnapshot*, int>)Export("purr_accessor_cell");
        shared = (delegate* unmanaged[Cdecl]<nint, SharedSnapshot*, int>)Export("purr_accessor_shared");
        textedit = (delegate* unmanaged[Cdecl]<nint, TextEditSnapshot*, int>)Export("purr_accessor_textedit");
        fileSize = (delegate* unmanaged[Cdecl]<nint, ulong*, int>)Export("purr_accessor_file_size");
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Native accessor used from a different thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    private static void Status(int status)
    {
        switch (status)
        {
            case 0: return;
            case 1: throw new ArgumentNullException("nativePointer");
            case 2: throw new ArgumentException("Field, record or opaque shape/operation mismatch.");
            case 3: throw new ArgumentOutOfRangeException("valueOrIndex", "Native representational/index bounds exceeded.");
            case 4: throw new NotSupportedException("Native const style storage is read-only.");
            case 5: throw new ArgumentException("Native copy buffer too small.");
            case 6: throw new IOException("ImFileGetSize failed; native file position may have changed.");
            default: throw new InvalidOperationException($"Unknown native accessor status {status}.");
        }
    }
    public void Dispose() { if (disposed) return; Check(); disposed = true; }
    public BorrowScope CreateBorrowScope() { Check(); return new(this); }
    public FieldInfo Describe(NativePackedField field)
    {
        Check(); FieldInfo value; Status(info((int)field, &value)); return value;
    }
    public int StyleCount { get { Check(); return styleCount(); } }
    public StyleSnapshot StyleAt(int index)
    {
        Check(); Triple value; Status(style(index, &value)); return new(value.A, value.B, value.C);
    }
    public byte EmptyStringByte { get { Check(); byte value; Status(emptyString(&value)); return value; } }

    /// <summary>Explicit validity token, not a native-liveness detector. Invalidate BEFORE
    /// external container change/destruction; all prior derived views then reject each
    /// operation without dereferencing. New views may be borrowed only after the caller
    /// establishes their new native lifetimes. Dispose does not destroy native objects.</summary>
    public sealed class BorrowScope : IDisposable
    {
        internal readonly NativeFieldAccessors Owner;
        private ulong generation;
        private bool disposed;
        internal BorrowScope(NativeFieldAccessors owner) => Owner = owner;
        internal void Check(ulong version)
        {
            Owner.Check();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (version != generation) throw new InvalidOperationException("Borrowed native view was invalidated.");
        }
        private void Current() => Check(generation);
        public void Invalidate() { Current(); generation = checked(generation + 1); }
        public void Dispose()
        {
            if (disposed) return;
            if (Environment.CurrentManagedThreadId != Owner.thread) throw new InvalidOperationException("Borrow scope disposed from a different thread.");
            // Only invalidates a managed token; valid even if its helper was disposed first.
            disposed = true;
        }
        public RecordView BorrowRecord(NativeRecordKind kind, nint nativeObject)
        {
            Current();
            if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            return new(this, generation, nativeObject, kind);
        }
        public RectEntriesView BorrowRectEntries(nint nativeVector) { Current(); return new(this, generation, nativeVector); }
        public TextBufferView BorrowTextBuffer(nint nativeTextBuffer) { Current(); return new(this, generation, nativeTextBuffer); }
        public OpaqueView BorrowOpaque(NativeOpaqueShape shape, nint nativeObjectOrFileHandle)
        {
            Current();
            if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
            return new(this, generation, nativeObjectOrFileHandle, shape);
        }
    }
    public abstract class BorrowedView
    {
        internal readonly BorrowScope Scope;
        internal readonly ulong Generation;
        internal readonly nint Pointer;
        internal NativeFieldAccessors Owner => Scope.Owner;
        internal BorrowedView(BorrowScope scope, ulong version, nint pointer)
        {
            if (pointer == 0) throw new ArgumentNullException(nameof(pointer));
            Scope = scope; Generation = version; Pointer = pointer;
        }
        internal void Check() => Scope.Check(Generation);
    }
    /// <summary>Checked borrowed address for interop with caller-owned native APIs. Extracting
    /// Address is an explicit escape: do not cache it or dereference after invalidation.
    /// No CLR ref/span, copying, allocation or managed element pointer arithmetic is provided.</summary>
    public sealed class BorrowedAddress : BorrowedView
    {
        internal BorrowedAddress(BorrowScope s, ulong g, nint p) : base(s, g, p) { }
        public nint Address { get { Check(); return Pointer; } }
    }
    public sealed class RecordView : BorrowedView
    {
        public NativeRecordKind Kind { get; }
        internal RecordView(BorrowScope s, ulong g, nint p, NativeRecordKind kind) : base(s, g, p) => Kind = kind;
        public long Get(NativePackedField field)
        {
            Check(); long value; Status(Owner.get((int)Kind, Pointer, (int)field, &value)); return value;
        }
        /// <summary>Validates representation, NOT live engine enum/domain/cross-field invariants.
        /// Domain restrictions per field are in the bound accessor manifest. Bool requires0/1;
        /// signed fields retain sentinels. Native const StyleVarInfo storage rejects writes.</summary>
        public void Set(NativePackedField field, long value)
        {
            Check(); Status(Owner.set((int)Kind, Pointer, (int)field, value));
        }
    }
    public sealed class RectEntriesView : BorrowedView
    {
        internal RectEntriesView(BorrowScope s, ulong g, nint p) : base(s, g, p) { }
        public int Count { get { Check(); int value; Status(Owner.rectCount(Pointer, &value)); return value; } }
        public RectSnapshot At(int index)
        {
            Check(); Triple value; Status(Owner.rect(Pointer, index, &value)); return new(value.A, value.B, value.C != 0);
        }
        public void Set(int index, NativePackedField field, long value)
        {
            Check(); Status(Owner.rectSet(Pointer, index, (int)field, value));
        }
    }
    /// <summary>No dependent borrowed byte pointers are exposed, so Append/Clear cannot
    /// invalidate managed snapshots. All returned bytes are owned managed copies.</summary>
    public sealed class TextBufferView : BorrowedView
    {
        internal TextBufferView(BorrowScope s, ulong g, nint p) : base(s, g, p) { }
        public byte[] CopyUtf8()
        {
            Check(); int size = 0;
            int status = Owner.textCopy(Pointer, null, 0, &size);
            if (status != 5) Status(status);
            byte[] bytes = new byte[size];
            fixed (byte* destination = bytes) Status(Owner.textCopy(Pointer, destination, size, &size));
            return bytes;
        }
        public void AppendUtf8(ReadOnlySpan<byte> bytes)
        {
            Check(); fixed (byte* source = bytes) Status(Owner.textAppend(Pointer, source, bytes.Length));
        }
        public void Clear() { Check(); Status(Owner.textClear(Pointer)); }
    }
    /// <summary>Closed native shape operations. Only the routes listed in opaque-routes
    /// of manifest.json are supported; wrong shape/operation fails before native dereference.
    /// Pool Count is MAP SLOT count, including removed entries; At returns null for holes.</summary>
    public sealed class OpaqueView : BorrowedView
    {
        public NativeOpaqueShape Shape { get; }
        internal OpaqueView(BorrowScope s, ulong g, nint p, NativeOpaqueShape shape) : base(s, g, p) => Shape = shape;
        private void Require(NativeOpaqueShape shape)
        {
            Check(); if (Shape != shape) throw new ArgumentException("Wrong native opaque shape for operation.");
        }
        public int Count { get { Check(); int value; Status(Owner.count((int)Shape, Pointer, &value)); return value; } }
        public int AliveCount { get { Check(); int value; Status(Owner.alive((int)Shape, Pointer, &value)); return value; } }
        public BorrowedAddress? At(int index)
        {
            Check(); nint value; Status(Owner.at((int)Shape, Pointer, index, &value));
            return value == 0 ? null : new(Scope, Generation, value);
        }
        public BorrowedAddress? Lookup(uint key)
        {
            Check(); nint value; Status(Owner.lookup((int)Shape, Pointer, key, &value));
            return value == 0 ? null : new(Scope, Generation, value);
        }
        public RecordView RecordAt(int index)
        {
            Check();
            NativeRecordKind kind = Shape switch
            {
                NativeOpaqueShape.ColumnSpan => NativeRecordKind.ImGuiTableColumn,
                NativeOpaqueShape.BakedVector => NativeRecordKind.ImFontBaked,
                NativeOpaqueShape.TablePool => NativeRecordKind.ImGuiTable,
                _ => throw new ArgumentException("This shape has no packed-field record view.")
            };
            nint value; Status(Owner.at((int)Shape, Pointer, index, &value));
            if (value == 0) throw new InvalidOperationException("Removed pool entry has no live record.");
            return new(Scope, Generation, value, kind);
        }
        public short ShortAt(int index)
        {
            Require(NativeOpaqueShape.ShortSpan); int value; Status(Owner.shortAt(Pointer, index, &value)); return checked((short)value);
        }
        public CellSnapshot CellAt(int index)
        {
            Require(NativeOpaqueShape.CellSpan); CellSnapshot value; Status(Owner.cellAt(Pointer, index, &value)); return value;
        }
        public bool TestNamedKey(int namedKey)
        {
            Require(NativeOpaqueShape.NamedKeys); int value = 0; Status(Owner.key(Pointer, namedKey, 0, &value)); return value != 0;
        }
        public void SetNamedKey(int namedKey, bool value)
        {
            Require(NativeOpaqueShape.NamedKeys); int nativeValue = value ? 1 : 0; Status(Owner.key(Pointer, namedKey, 1, &nativeValue));
        }
        public SharedSnapshot SharedData()
        {
            Require(NativeOpaqueShape.DrawListSharedData); SharedSnapshot value; Status(Owner.shared(Pointer, &value)); return value;
        }
        public TextEditSnapshot TextEdit()
        {
            Require(NativeOpaqueShape.TextEditState); TextEditSnapshot value; Status(Owner.textedit(Pointer, &value)); return value;
        }
        /// <summary>Real ImFileGetSize. Successful calls restore position. On failure throws
        /// IOException and native position may have changed. Never closes/owns the handle.</summary>
        public ulong FileSize()
        {
            Require(NativeOpaqueShape.FileHandle); ulong value; Status(Owner.fileSize(Pointer, &value)); return value;
        }
    }
}
