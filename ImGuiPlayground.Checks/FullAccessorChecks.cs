using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using global::ImGuiPlayground;

/// <summary>Opt-in component checks. Run receives the caller's ONE already-owned native
/// handle. It installs no resolver and loads/frees no native library. Fixture exports
/// are deliberately separate from production accessors and required only by this test.
/// Returns serializable observations; the disposable harness owns output files.</summary>
internal static unsafe class FullAccessorChecks
{
    private static readonly Dictionary<string, string> AbiPins = new()
    {
        ["Brutal.ImGui"] = "b76777a4ef3399d6353b9dba1982e3afb84b5da2aa2500da1c062db0bfad827c",
        ["Brutal.Core.Common"] = "4cbae1473d6a3759345f1eec8e62e6c7da1e19f8d10db8d13c005203ec590de4",
        ["Brutal.Core.Numerics"] = "a217a6098116a1895e965b3a1d4fdee931e59a7a55f326c7d710b0d7eeccd17a"
    };
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Negative test accepted: " + message);
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    // Independent TEST oracle only. Production helper has no packed-storage masks.
    private static byte[] Apply(byte[] before, byte[] mask, long value)
    {
        byte[] expected = (byte[])before.Clone();
        int bit = 0;
        for (int i = 0; i < mask.Length; i++)
            for (int b = 0; b < 8; b++)
                if ((mask[i] & (1 << b)) != 0)
                {
                    int v = ((ulong)value & (1UL << bit++)) != 0 ? 1 << b : 0;
                    expected[i] = (byte)((expected[i] & ~(1 << b)) | v);
                }
        return expected;
    }
    private static long Decode(byte[] bytes, byte[] mask, int width, bool signed)
    {
        ulong value = 0;
        int bit = 0;
        for (int i = 0; i < mask.Length; i++)
            for (int b = 0; b < 8; b++)
                if ((mask[i] & (1 << b)) != 0)
                {
                    if ((bytes[i] & (1 << b)) != 0) value |= 1UL << bit;
                    bit++;
                }
        Require(bit == width, "mask population differs from effective width");
        if (signed && (value & (1UL << (width - 1))) != 0) value |= ulong.MaxValue << width;
        return unchecked((long)value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AllocationAudit
    {
        public ulong Allocations, Frees, Failed, NullFrees, Outstanding, Errors, Restored;
    }
    internal static object Run(nint alreadyOwnedNativeHandle, string manifestPath, string selectedManagedDirectory, string fixtureFilePath)
    {
        var begin = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_audit_begin");
        var end = (delegate* unmanaged[Cdecl]<AllocationAudit*, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_audit_end");
        Require(sizeof(AllocationAudit) == 56 && begin() == 0, "fresh-process allocator audit setup");
        object? core = null;
        AllocationAudit audit = default;
        Exception? failure = null;
        try { core = RunCore(alreadyOwnedNativeHandle, manifestPath, selectedManagedDirectory, fixtureFilePath); }
        catch (Exception error) { failure = error; }
        finally
        {
            // Always restores original callbacks AND user-data, even on failing tests.
            int status = end(&audit);
            if (status != 0 || audit.Errors != 0 || audit.Failed != 0 || audit.Outstanding != 0 || audit.Allocations != audit.Frees || audit.Restored != 1)
            {
                var cleanup = new InvalidOperationException($"native allocation audit failed: status={status}, allocations={audit.Allocations}, frees={audit.Frees}, outstanding={audit.Outstanding}, errors={audit.Errors}, restored={audit.Restored}");
                failure = failure is null ? cleanup : new AggregateException(failure, cleanup);
            }
        }
        if (failure is not null) throw new InvalidOperationException(audit.Restored == 1 ? "Accessor check failed (allocator callbacks restored before propagation)." : "Accessor check failed; native allocator restoration FAILED.", failure);
        var report = JsonSerializer.SerializeToNode(core, new JsonSerializerOptions { IncludeFields = true })!.AsObject();
        report["allocationAudit"] = JsonSerializer.SerializeToNode(new { audit.Allocations, audit.Frees, audit.Failed, audit.NullFrees, audit.Outstanding, audit.Errors, audit.Restored, conclusion = "zero outstanding addresses observed through ImGui allocator callbacks during the bounded fresh-process fixture interval; not a CRT/STB/OS/global allocation proof" });
        return report;
    }
    private static string ManifestIdentity(JsonElement root)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            void Write(JsonElement value, bool top = false)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        if (top && property.Name == "identity") continue;
                        writer.WritePropertyName(property.Name); Write(property.Value);
                    }
                    writer.WriteEndObject();
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) Write(item); writer.WriteEndArray();
                }
                else value.WriteTo(writer);
            }
            Write(root, true);
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }
    private static object RunCore(nint alreadyOwnedNativeHandle, string manifestPath, string selectedManagedDirectory, string fixtureFilePath)
    {
        Require(alreadyOwnedNativeHandle != 0, "caller must own one live native handle");
        var identities = new List<object>();
        var closure = new SortedDictionary<string, string>();
        void LoadClosure(Assembly assembly)
        {
            string name = assembly.GetName().Name!;
            if (closure.ContainsKey(name)) return;
            closure.Add(name, assembly.FullName!);
            // Actual runtime dependency failures are NEVER swallowed as metadata-only.
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies()) LoadClosure(Assembly.Load(reference));
        }
        foreach (var (name, expected) in AbiPins)
        {
            string selected = Path.Combine(selectedManagedDirectory, name + ".dll");
            string staged = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            Assembly loaded = Assembly.Load(name);
            Require(Hash(selected) == expected && Hash(staged) == expected && Hash(loaded.Location) == expected, "selected/staged/loaded identity mismatch: " + name);
            Require(Path.GetFullPath(loaded.Location) == Path.GetFullPath(staged), "ABI contributor not loaded from disposable stage");
            LoadClosure(loaded);
            identities.Add(new { name, expected, selected, staged, loaded = loaded.Location, mvid = loaded.ManifestModule.ModuleVersionId });
        }
        Require(Unsafe.SizeOf<Brutal.ImGuiApi.Internal.ImGuiStyleVarInfo>() == 6, "retain real CLR StyleVarInfo6 defect");
        Require(Unsafe.SizeOf<Brutal.ImGuiApi.Internal.ImFontAtlasRectEntry>() == 7, "retain real CLR RectEntry7 defect");
        int[] opaqueManagedSizes =
        [
            Unsafe.SizeOf<Brutal.ImGuiApi.ImBitArrayForNamedKeys>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImChunkStream<Brutal.ImGuiApi.Internal.ImGuiTableSettings>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImChunkStream<Brutal.ImGuiApi.Internal.ImGuiWindowSettings>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImDrawListSharedData>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImFileHandle>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImPool<Brutal.ImGuiApi.Internal.ImGuiMultiSelectState>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImPool<Brutal.ImGuiApi.Internal.ImGuiTabBar>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImPool<Brutal.ImGuiApi.Internal.ImGuiTable>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImSpan<Brutal.ImGuiApi.Internal.ImGuiTableCellData>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImSpan<Brutal.ImGuiApi.Internal.ImGuiTableColumn>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImSpan<short>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImStableVector<Brutal.ImGuiApi.ImFontBaked>>(),
            Unsafe.SizeOf<Brutal.ImGuiApi.ImStbTexteditState>()
        ];
        Require(opaqueManagedSizes.Length == 13 && opaqueManagedSizes.All(size => size == 1), "retain all13 actual unchanged CLR Size1 placeholders");
        Require(typeof(Brutal.ImGuiApi.ImFontGlyph).Assembly == Assembly.Load("Brutal.ImGui"), "actual unchanged BRUTAL type loaded");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = manifest.RootElement;
        Require(root.GetProperty("schema").GetString() == "purr.native-accessors" && root.GetProperty("version").GetInt32() == 1, "manifest schema mismatch");
        Require(root.GetProperty("identity").GetString() == NativeFieldAccessors.ExpectedIdentity && ManifestIdentity(root) == NativeFieldAccessors.ExpectedIdentity, "changed source/profile/accessor contract");
        var fields = root.GetProperty("fields").EnumerateArray().ToArray();
        Require(fields.Length == 47 && Enum.GetValues<NativePackedField>().Length == 47, "finite47 inventory omitted");
        Require(root.GetProperty("opaqueShapes").GetArrayLength() == 13 && Enum.GetValues<NativeOpaqueShape>().Length == 13, "finite13 opaque inventory omitted");
        // Creation must be the first production export binding, so missing-asset
        // negatives exercise the actual helper's clear unsupported-ABI exception.
        using var access = new NativeFieldAccessors(alreadyOwnedNativeHandle);
        var create = (delegate* unmanaged[Cdecl]<int, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_create");
        var destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_destroy");
        var pointer = (delegate* unmanaged[Cdecl]<nint, int, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_object");
        var strideOf = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_stride");
        var snapshot = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_snapshot");
        var measureMask = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_mask");
        var assign = (delegate* unmanaged[Cdecl]<nint, long, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_assign");
        var directRead = (delegate* unmanaged[Cdecl]<nint, long>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_field_read");
        var nativeGet = (delegate* unmanaged[Cdecl]<int, nint, int, long*, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_accessor_get");
        var nativeSet = (delegate* unmanaged[Cdecl]<int, nint, int, long, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_accessor_set");
        var observations = new List<object>();
        var negativeChecks = new List<string>();
        Reject<ArgumentNullException>(() => new NativeFieldAccessors(0), "null library");
        Reject<ArgumentException>(() => access.Describe((NativePackedField)47), "invalid field id");
        long untouched = 0x1234;
        Require(nativeGet(0, 0, 0, &untouched) == 1 && untouched == 0x1234, "native null get leaves output unchanged");
        Require(nativeSet(0, 0, 0, 0) == 1, "native null set");
        byte[] Snapshot(nint fixture, int length)
        {
            byte[] result = new byte[length];
            fixed (byte* p = result) Require(snapshot(fixture, p, length) == 0, "snapshot buffer");
            return result;
        }
        for (int id = 0; id < fields.Length; id++)
        {
            var definition = fields[id];
            var field = (NativePackedField)id;
            Require(definition.GetProperty("id").GetInt32() == id, "duplicate/reordered inventory");
            Require(field.ToString() == definition.GetProperty("nativeType").GetString() + "_" + definition.GetProperty("nativeMember").GetString(), "field source name changed");
            var info = access.Describe(field);
            Require((int)info.Record == definition.GetProperty("record").GetInt32() && info.OriginalBits == definition.GetProperty("originalWidthBits").GetInt32() && info.EffectiveBits == definition.GetProperty("effectiveWidthBits").GetInt32() && (info.Signed != 0) == definition.GetProperty("signed").GetBoolean(), "native field descriptor mismatch");
            Require(info.Minimum == definition.GetProperty("minimum").GetInt64() && info.Maximum == definition.GetProperty("maximum").GetInt64(), "field representational bounds mismatch");
            Require((info.ReadOnly != 0) == definition.GetProperty("readOnly").GetBoolean() && (info.Boolean != 0) == definition.GetProperty("boolean").GetBoolean(), "field readonly/boolean disposition changed");
            nint fixture = create(id);
            Require(fixture != 0, "real native construction failed");
            using var scope = access.CreateBorrowScope();
            try
            {
                int stride = strideOf(fixture);
                Require(stride > 0 && stride < 64 * 1024 * 1024, "unbounded/bad record size");
                nint first = pointer(fixture, 0), second = pointer(fixture, 1);
                Require(first != 0 && second - first == stride && pointer(fixture, -1) == 0 && pointer(fixture, 2) == 0, "native adjacent-record indexing");
                var view = scope.BorrowRecord(info.Record, first);
                var neighbor = scope.BorrowRecord(info.Record, second);
                long adjacentValue = neighbor.Get(field);
                byte[] mask = new byte[stride];
                fixed (byte* p = mask) Require(measureMask(fixture, p, mask.Length) == 0, "native assigned mask");
                Require(mask.Sum(b => BitOperations.PopCount((uint)b)) == info.EffectiveBits, "native mask width");
                var values = new List<long> { info.Minimum, info.Maximum, 0, 1, info.Signed != 0 ? -1 : info.Maximum / 2 };
                if (info.Maximum >= 2) values.Add(2); // All named sort/data-authority values.
                if (field == NativePackedField.ImGuiContext_ActiveIdMouseButton) values.AddRange([-1, 0, 1, 2, 3, 4]);
                if (field == NativePackedField.ImGuiBoxSelectState_KeyMods)
                {
                    int[] modifiers = [(int)Brutal.ImGuiApi.ImGuiKey.Mod_Ctrl, (int)Brutal.ImGuiApi.ImGuiKey.Mod_Shift, (int)Brutal.ImGuiApi.ImGuiKey.Mod_Alt, (int)Brutal.ImGuiApi.ImGuiKey.Mod_Super];
                    for (int combination = 0; combination < 16; combination++)
                    {
                        int modifierValue = 0;
                        for (int bit = 0; bit < 4; bit++) if ((combination & (1 << bit)) != 0) modifierValue |= modifiers[bit];
                        values.Add(modifierValue);
                    }
                }
                foreach (long value in values.Distinct())
                {
                    byte[] before = Snapshot(fixture, 2 * stride);
                    byte[] expected = Apply(before, mask, value);
                    if (info.ReadOnly != 0)
                    {
                        Reject<NotSupportedException>(() => view.Set(field, value), "readonly style member");
                        Require(before.SequenceEqual(Snapshot(fixture, 2 * stride)), "readonly write changed storage");
                        Require(assign(fixture, value) == 0, "isolated fixture-only assignment");
                    }
                    else view.Set(field, value);
                    byte[] after = Snapshot(fixture, 2 * stride);
                    Require(after.SequenceEqual(expected), $"mask/neighbor/ordinary adjacent field/adjacent RECORD preservation: {field}={value}");
                    Require(Decode(after, mask, info.EffectiveBits, info.Signed != 0) == value && view.Get(field) == value && directRead(fixture) == value, "signed/native/oracle read mismatch");
                    Require(neighbor.Get(field) == adjacentValue, "adjacent native record corrupted");
                    // A deliberately wrong mask must NOT pass the oracle, even when a
                    // mutation remains within the same containing packed word.
                    byte[] wrongMask = (byte[])mask.Clone();
                    int used = Array.FindIndex(wrongMask, b => b != 0);
                    wrongMask[used] ^= (byte)(wrongMask[used] & -wrongMask[used]);
                    Require(wrongMask.Sum(b => BitOperations.PopCount((uint)b)) != info.EffectiveBits, "wrong-mask negative not detected");
                }
                byte[] rejectedBefore = Snapshot(fixture, 2 * stride);
                if (info.ReadOnly == 0)
                {
                    Reject<ArgumentOutOfRangeException>(() => view.Set(field, info.Minimum - 1), "below representable bounds");
                    Reject<ArgumentOutOfRangeException>(() => view.Set(field, info.Maximum + 1), "above representable bounds");
                }
                var wrong = scope.BorrowRecord((NativeRecordKind)(((int)info.Record + 1) % 12), first);
                Reject<ArgumentException>(() => wrong.Get(field), "wrong native record kind");
                Reject<ArgumentException>(() => wrong.Set(field, 0), "wrong native write record kind");
                Require(rejectedBefore.SequenceEqual(Snapshot(fixture, 2 * stride)), "rejected write changed storage");
                scope.Invalidate();
                Reject<InvalidOperationException>(() => view.Get(field), "stale view before destroy");
                Reject<InvalidOperationException>(() => neighbor.Set(field, 0), "stale derived adjacent view");
                observations.Add(new { id, field = field.ToString(), stride, originalBits = info.OriginalBits, effectiveBits = info.EffectiveBits, signed = info.Signed != 0, readOnly = info.ReadOnly != 0, mask = mask.Select((b, offset) => new { offset, value = (int)b }).Where(x => x.value != 0).ToArray(), values = values.Distinct().ToArray(), completeRecordPreservation = true, adjacentRecordPreservation = true });
            }
            finally { scope.Invalidate(); destroy(fixture); }
        }
        negativeChecks.Add("all47: wrong-mask population, wrong native record, below/above range (44 mutable), readonly (3), stale view; complete neighboring storage and second RECORD checked");
        var fixtureStyle = (delegate* unmanaged[Cdecl]<int, int*, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_style");
        var styles = new List<NativeFieldAccessors.StyleSnapshot>();
        int* expectedStyle = stackalloc int[3];
        for (int i = 0; i < access.StyleCount; i++)
        {
            Require(fixtureStyle(i, expectedStyle) == 0, "independent native const style table");
            var actual = access.StyleAt(i);
            Require(actual == new NativeFieldAccessors.StyleSnapshot(expectedStyle[0], expectedStyle[1], expectedStyle[2]), "style native indexing (never CLR6 stride)");
            styles.Add(actual);
        }
        Require(styles.Count >= 3 && styles[0] != styles[1], "style second-record indexing evidence");
        Reject<ArgumentOutOfRangeException>(() => access.StyleAt(-1), "style lower bounds");
        Reject<ArgumentOutOfRangeException>(() => access.StyleAt(access.StyleCount), "style upper bounds");

        var containerCreate = (delegate* unmanaged[Cdecl]<byte*, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_containers_create");
        var containerDestroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_containers_destroy");
        var container = (delegate* unmanaged[Cdecl]<nint, int, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_container");
        var rects = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_rects");
        var text = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_text");
        var filePosition = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_file_position");
        var nonseekableFile = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_nonseekable_file");
        byte[] fileName = Encoding.UTF8.GetBytes(fixtureFilePath + '\0');
        nint containers;
        fixed (byte* name = fileName) containers = containerCreate(name);
        Require(containers != 0, "native fixture construction");
        var opaqueResults = new List<object>();
        using var containerScope = access.CreateBorrowScope();
        try
        {
            Reject<ArgumentNullException>(() => containerScope.BorrowOpaque(NativeOpaqueShape.ShortSpan, 0), "null native opaque pointer");
            var opaque = Enum.GetValues<NativeOpaqueShape>().ToDictionary(shape => shape, shape => containerScope.BorrowOpaque(shape, container(containers, (int)shape)));
            var keys = opaque[NativeOpaqueShape.NamedKeys];
            int begin = (int)Brutal.ImGuiApi.ImGuiKey.NamedKey_BEGIN;
            int end = (int)Brutal.ImGuiApi.ImGuiKey.NamedKey_END;
            foreach (int key in new[] { begin, begin + 1, end - 1 })
            {
                Require(!keys.TestNamedKey(key), "native named-key initialization");
                keys.SetNamedKey(key, true); Require(keys.TestNamedKey(key), "named-key set");
                keys.SetNamedKey(key, false); Require(!keys.TestNamedKey(key), "named-key clear");
            }
            keys.SetNamedKey(begin, true);
            Require(!keys.TestNamedKey(begin + 1), "named-key neighbor preservation");
            Reject<ArgumentOutOfRangeException>(() => keys.SetNamedKey(begin - 1, true), "named-key lower bound");
            Reject<ArgumentOutOfRangeException>(() => keys.TestNamedKey(end), "named-key upper bound");
            foreach (var shape in new[] { NativeOpaqueShape.TableSettings, NativeOpaqueShape.WindowSettings })
            {
                var stream = opaque[shape];
                Require(stream.Count == 2 && stream.At(0)!.Address != stream.At(1)!.Address, "real variable chunk iteration");
                // Actual unchanged ordinary setting ID field reads corroborate the native
                // pointer route, without any Size1 allocation/copy/element arithmetic.
                uint id0 = shape == NativeOpaqueShape.TableSettings ? ((Brutal.ImGuiApi.Internal.ImGuiTableSettings*)stream.At(0)!.Address)->ID : ((Brutal.ImGuiApi.Internal.ImGuiWindowSettings*)stream.At(0)!.Address)->ID;
                uint id1 = shape == NativeOpaqueShape.TableSettings ? ((Brutal.ImGuiApi.Internal.ImGuiTableSettings*)stream.At(1)!.Address)->ID : ((Brutal.ImGuiApi.Internal.ImGuiWindowSettings*)stream.At(1)!.Address)->ID;
                Require(id1 == id0 + 1 && id0 == (shape == NativeOpaqueShape.TableSettings ? 100 : 200), "actual native chunk contents");
            }
            foreach (var shape in new[] { NativeOpaqueShape.MultiSelectPool, NativeOpaqueShape.TabBarPool, NativeOpaqueShape.TablePool })
            {
                var pool = opaque[shape];
                Require(pool.Count == 3 && pool.AliveCount == 2 && pool.At(1) is null && pool.Lookup(20) is null && pool.Lookup(99) is null, "native pool tombstone/missing lookup");
                Require(pool.At(0)!.Address == pool.Lookup(10)!.Address && pool.At(2)!.Address == pool.Lookup(30)!.Address, "native pool map lookup alias");
                if (shape == NativeOpaqueShape.TablePool) Reject<InvalidOperationException>(() => pool.RecordAt(1), "pool hole not a record lifetime");
            }
            var cells = opaque[NativeOpaqueShape.CellSpan];
            Require(cells.CellAt(0).Color == 0x12345678 && cells.CellAt(0).Column == 3 && cells.CellAt(1).Color == 0xfedcba98 && cells.CellAt(1).Column == -1, "native cell snapshots");
            var columns = opaque[NativeOpaqueShape.ColumnSpan];
            Require(columns.RecordAt(0).Get(NativePackedField.ImGuiTableColumn_SortDirection) == 1 && columns.RecordAt(1).Get(NativePackedField.ImGuiTableColumn_SortDirection) == 2, "native column span indexing");
            columns.RecordAt(1).Set(NativePackedField.ImGuiTableColumn_SortDirection, 0);
            Require(columns.RecordAt(0).Get(NativePackedField.ImGuiTableColumn_SortDirection) == 1, "column span neighbor");
            var shorts = opaque[NativeOpaqueShape.ShortSpan];
            Require(shorts.ShortAt(0) == -123 && shorts.ShortAt(1) == 321, "native short span indexing");
            var baked = opaque[NativeOpaqueShape.BakedVector];
            Require(baked.Count == 33 && baked.RecordAt(0).Get(NativePackedField.ImFontBaked_MetricsTotalSurface) == 100 && baked.RecordAt(32).Get(NativePackedField.ImFontBaked_MetricsTotalSurface) == 132, "stable vector cross-block native indexing");
            var shared = opaque[NativeOpaqueShape.DrawListSharedData].SharedData();
            Require(shared.FontSize == 17.5f && shared.FontScale == 1.25f && shared.CurveTolerance == 0.75f && shared.CircleError == 0.25f && shared.InitialFlags == 1, "real shared-data snapshot");
            var edit = opaque[NativeOpaqueShape.TextEditState].TextEdit();
            Require(edit.Cursor == 7 && edit.SelectStart == 2 && edit.SelectEnd == 5 && edit.InsertMode == 1, "real configured STB snapshot");
            Require(filePosition(containers) == 2 && opaque[NativeOpaqueShape.FileHandle].FileSize() == (ulong)new FileInfo(fixtureFilePath).Length && filePosition(containers) == 2, "real borrowed ImFileGetSize preserves successful position");
            var pipeFile = containerScope.BorrowOpaque(NativeOpaqueShape.FileHandle, nonseekableFile(containers));
            Reject<IOException>(() => pipeFile.FileSize(), "real ImFileGetSize failure on a non-seekable native file (no fabricated size)");
            foreach (var shape in new[] { NativeOpaqueShape.TableSettings, NativeOpaqueShape.WindowSettings, NativeOpaqueShape.MultiSelectPool, NativeOpaqueShape.TabBarPool, NativeOpaqueShape.TablePool, NativeOpaqueShape.CellSpan, NativeOpaqueShape.ColumnSpan, NativeOpaqueShape.ShortSpan, NativeOpaqueShape.BakedVector })
            {
                var view = opaque[shape];
                Reject<ArgumentOutOfRangeException>(() => view.At(-1), "opaque lower bounds");
                Reject<ArgumentOutOfRangeException>(() => view.At(view.Count), "opaque upper bounds");
            }
            Reject<ArgumentException>(() => shorts.SharedData(), "opaque wrong shape");
            Reject<ArgumentException>(() => keys.At(0), "unsupported opaque route");
            var rectangles = containerScope.BorrowRectEntries(rects(containers));
            Require(rectangles.Count == 2 && rectangles.At(0) == new NativeFieldAccessors.RectSnapshot(19, 3, true) && rectangles.At(1) == new NativeFieldAccessors.RectSnapshot(-1, 17, false), "real RectEntry vector native4 indexing, not CLR7");
            rectangles.Set(1, NativePackedField.ImFontAtlasRectEntry_TargetIndex, -23);
            Require(rectangles.At(1).TargetIndex == -23 && rectangles.At(0) == new NativeFieldAccessors.RectSnapshot(19, 3, true), "RectEntry second-record write and neighbor");
            rectangles.Set(1, NativePackedField.ImFontAtlasRectEntry_TargetIndex, -1);
            Reject<ArgumentOutOfRangeException>(() => rectangles.At(-1), "rect lower bound");
            Reject<ArgumentOutOfRangeException>(() => rectangles.At(2), "rect upper bound");
            Reject<ArgumentOutOfRangeException>(() => rectangles.Set(1, NativePackedField.ImFontAtlasRectEntry_Generation, 512), "rect signed range");
            Reject<ArgumentException>(() => rectangles.Set(1, NativePackedField.ImFontGlyph_Codepoint, 1), "rect wrong native field");
            var buffer = containerScope.BorrowTextBuffer(text(containers));
            Require(buffer.CopyUtf8().Length == 0 && access.EmptyStringByte == 0, "native static EmptyString");
            buffer.AppendUtf8("first λ"u8); byte[] copy = buffer.CopyUtf8();
            buffer.AppendUtf8(" + second"u8);
            Require(Encoding.UTF8.GetString(copy) == "first λ" && Encoding.UTF8.GetString(buffer.CopyUtf8()) == "first λ + second" && access.EmptyStringByte == 0, "real TextBuffer append/copy static vs instance conflict");
            buffer.Clear(); Require(buffer.CopyUtf8().Length == 0 && Encoding.UTF8.GetString(copy) == "first λ", "owned text snapshot survives clear");
            var derived = baked.RecordAt(32);
            var address = baked.At(32)!;
            Exception? wrongThread = null;
            var otherThread = new Thread(() => { try { derived.Get(NativePackedField.ImFontBaked_WantDestroy); } catch (Exception e) { wrongThread = e; } });
            otherThread.Start(); otherThread.Join();
            Require(wrongThread is InvalidOperationException, "wrong thread rejected before dereference");
            containerScope.Invalidate();
            Reject<InvalidOperationException>(() => derived.Get(NativePackedField.ImFontBaked_WantDestroy), "derived view invalidation");
            Reject<InvalidOperationException>(() => _ = address.Address, "borrowed address invalidation");
            Reject<InvalidOperationException>(() => rectangles.At(0), "rect stale view");
            Reject<InvalidOperationException>(() => buffer.Clear(), "text stale view");
            foreach (var (shape, view) in opaque)
            {
                Reject<InvalidOperationException>(() => _ = view.Count, "all13 stale scopes before native access");
                opaqueResults.Add(new { shape = shape.ToString(), actualNativeRoute = true, invalidationChecked = true });
            }
            using var disposedScope = access.CreateBorrowScope();
            var disposedView = disposedScope.BorrowOpaque(NativeOpaqueShape.ShortSpan, container(containers, 10));
            disposedScope.Dispose();
            Reject<ObjectDisposedException>(() => disposedView.ShortAt(0), "disposed borrow before dereference");
            using var disposedHelper = new NativeFieldAccessors(alreadyOwnedNativeHandle);
            using var helperScope = disposedHelper.CreateBorrowScope();
            var helperView = helperScope.BorrowOpaque(NativeOpaqueShape.ShortSpan, container(containers, 10));
            disposedHelper.Dispose();
            Reject<ObjectDisposedException>(() => helperView.ShortAt(0), "disposed helper invalidates derived operation");
            Require(access.EmptyStringByte == 0, "disposing a helper must not free caller's native handle");
        }
        finally { containerScope.Invalidate(); containerDestroy(containers); }
        negativeChecks.Add("all13 opaque shapes, variable chunks, pool holes/alias lookup, native span and block32 indexing, checked bounds/shape/thread/disposed/generation; style readonly and RectEntry vector second-record semantics; TextBuffer owned copies");
        var counts = (delegate* unmanaged[Cdecl]<int*, int*, int>)NativeLibrary.GetExport(alreadyOwnedNativeHandle, "purr_fixture_counts");
        int created, destroyed;
        Require(counts(&created, &destroyed) == 0 && created == destroyed && created >= 95, "native fixture record/container destruction balance");
        return new { schema = "purr.accessor-managed-checks", version = 1, status = "passed", identity = NativeFieldAccessors.ExpectedIdentity, identities, loadedDependencyClosure = closure, bitfields = observations, opaqueShapes = opaqueResults, styles, negativeChecks, created, destroyed, rawAliasesStillUnsafe = true, actualBrutalSizes = new { styleVarInfo = 6, rectEntry = 7, opaqueShapes = opaqueManagedSizes }, scope = "macOS actual owned C# helper calls and real native fixture; no qualification of original raw aliases or all external managed PInvokes; Windows/Linux execution pending" };
    }
}
