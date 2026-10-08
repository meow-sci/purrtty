using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Imports = Brutal.ImGuiApi.ImGui.PInvoke;
using InternalImports = Brutal.ImGuiApi.Internal.PInvoke;

// No load/resolver ownership: the harness passes its already verified sole native image.
internal static unsafe class FullEnumAbiChecks
{
    internal static string[] Run(nint nativeHandle)
    {
        var create = (delegate* unmanaged[Cdecl]<ImGuiContext*, nint>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_create");
        var destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_destroy");
        var obj = (delegate* unmanaged[Cdecl]<nint, int, void*>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_object");
        var prepareSort = (delegate* unmanaged[Cdecl]<nint, int, void>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_sort_prepare");
        var observe = (delegate* unmanaged[Cdecl]<nint, int, int>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_observe");
        var keyEvent = (delegate* unmanaged[Cdecl]<nint, int, ulong>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_key_event");
        ImGuiContext* context = Imports.CreateContext(null);
        nint fixture = create(context);
        try
        {
            Imports.SetMouseCursor(ImGuiMouseCursor.None);
            Require(Imports.GetMouseCursor() == ImGuiMouseCursor.None, "negative mouse cursor sentinel");
            Imports.SetMouseCursor(ImGuiMouseCursor.Hand);
            Require(Imports.GetMouseCursor() == ImGuiMouseCursor.Hand, "named cursor");
            for (int i = 0; i < 3; ++i)
            {
                prepareSort(fixture, i);
                Require(InternalImports.TableGetColumnNextSortDirection_internal((ImGuiTableColumn*)obj(fixture, 3)) == (ImGuiSortDirection)i, "sort typed return");
                InternalImports.TableSetColumnSortDirection_internal(0, (ImGuiSortDirection)i, false);
                Require(observe(fixture, 0) == i && observe(fixture, 1) == 1, "sort typed input/engine dirty effect");
            }
            var draw = (ImDrawList*)obj(fixture, 4);
            float2* points = stackalloc float2[] { new(-5, 2), new(8, 2), new(8, 11) };
            Imports.ImDrawList_AddPolyline(draw, points, 3, 0xffffffffu, ImDrawFlags.Closed, 1f);
            Require(observe(fixture, 2) == 12 && observe(fixture, 3) == 18, "real Closed polyline topology");
            ImGuiKey chord = ImGuiKey.A | ImGuiKey.Mod_Ctrl | ImGuiKey.Mod_Shift;
            ImGuiInputFlags flags = ImGuiInputFlags.Repeat | ImGuiInputFlags.Tooltip;
            Imports.SetNextItemShortcut(chord, flags);
            Require(observe(fixture, 5) == (int)chord && observe(fixture, 6) == (int)flags && observe(fixture, 7) == 1, "real combined key/modifier/flags next-item state");
            int initial = observe(fixture, 4);
            Imports.ImGuiIO_AddKeyEvent(Imports.GetIO(), ImGuiKey.A, true);
            Imports.ImGuiIO_AddKeyEvent(Imports.GetIO(), ImGuiKey.Mod_Ctrl, true);
            Require(observe(fixture, 4) == initial + 2, "actual key/modifier queue effect");
            Require(keyEvent(fixture, initial) == ((ulong)ImGuiKey.A << 32 | 1), "named key event storage");
            // Native fixture disables the optional platform Ctrl/Super remapping.
            Require(keyEvent(fixture, initial + 1) == ((ulong)ImGuiKey.Mod_Ctrl << 32 | 1), "modifier event storage");
            return ["actual named/negative mouse cursors", "actual SortDirection 0/1/2 input/result/effect", "actual draw Closed flag topology", "actual key/modifier queue effects", "actual combined shortcut flags state"];
        }
        finally { destroy(fixture); Imports.DestroyContext(context); }
    }

    internal static int RunMatrix(nint nativeHandle, string matrixJson)
    {
        using JsonDocument document = JsonDocument.Parse(matrixJson);
        JsonElement root = document.RootElement;
        Require(root.GetProperty("schema").GetString() == "playground_imgui.profile-enum-matrix" && root.GetProperty("schemaVersion").GetInt32() == 1, "matrix schema");
        var expectedTypes = typeof(ImGui).Assembly.GetTypes().Where(t => t.IsEnum).ToDictionary(t => "Brutal.ImGui::" + t.FullName, StringComparer.Ordinal);
        var rows = root.GetProperty("types").EnumerateArray().ToArray();
        Require(rows.Length == 85 && expectedTypes.Count == 85 && rows.Select(r => r.GetProperty("type").GetString()!).ToHashSet().SetEquals(expectedTypes.Keys), "independent reflected enum inventory");
        int calls = 0;
        foreach (JsonElement row in rows)
        {
            Type type = expectedTypes[row.GetProperty("type").GetString()!];
            Require(Enum.GetUnderlyingType(type) == typeof(int), "actual managed backing");
            var names = Enum.GetNames(type).ToDictionary(name => name, name => Convert.ToInt32(Enum.Parse(type, name)), StringComparer.Ordinal);
            var values = row.GetProperty("values").EnumerateArray().ToArray();
            Require(values.Length == names.Count && values.Select(v => v.GetProperty("managed").GetString()!).ToHashSet().SetEquals(names.Keys), "independent reflected named-value inventory");
            var transport = (delegate* unmanaged[Cdecl]<int, int>)NativeLibrary.GetExport(nativeHandle, row.GetProperty("typedExport").GetString()!);
            var constants = (delegate* unmanaged[Cdecl]<int, int>)NativeLibrary.GetExport(nativeHandle, row.GetProperty("constantsExport").GetString()!);
            var raw = (delegate* unmanaged[Cdecl]<uint, uint>)NativeLibrary.GetExport(nativeHandle, row.GetProperty("rawBackingExport").GetString()!);
            var representation = (delegate* unmanaged[Cdecl]<uint>)NativeLibrary.GetExport(nativeHandle, row.GetProperty("representationExport").GetString()!);
            uint expectedRepresentation = checked((uint)row.GetProperty("width").GetInt32()) |
                (row.GetProperty("nativeRepresentation").GetString() == "int" ? 1u << 8 : 0) |
                (row.GetProperty("storageRepresentation").GetString() == "int" ? 1u << 9 : 0);
            Require(representation() == expectedRepresentation, "compiler-observed native signedness/width");
            foreach (JsonElement value in values)
            {
                int actual = names[value.GetProperty("managed").GetString()!];
                Require(value.GetProperty("value").GetInt32() == actual, "reflected named-value correspondence");
                Require(transport(actual) == actual, "typed native storage named transport");
                Require(constants(actual) == actual, "typed associated constants declaration named transport");
                calls += 2;
            }
            foreach (JsonElement combination in row.GetProperty("combinations").EnumerateArray())
            {
                int bits = 0;
                foreach (JsonElement name in combination.GetProperty("names").EnumerateArray())
                {
                    int bit = names[name.GetString()!];
                    Require(bit > 0 && (bit & (bit - 1)) == 0, "valid independent named flag bit");
                    bits |= bit;
                }
                Require(bits == combination.GetProperty("value").GetInt32() && transport(bits) == bits, "listed legitimate flag combination");
                ++calls;
            }
            uint[] patterns = [0, 0x7fffffff, 0x80000000, 0xffffffff];
            Require(root.GetProperty("rawPatterns").EnumerateArray().Select(v => v.GetUInt32()).SequenceEqual(patterns), "independent high-bit matrix inventory");
            foreach (uint bits in patterns)
            {
                Require(raw(bits) == bits, "underlying integer raw bit transport (not engine enum domain)");
                ++calls;
            }
        }
        return calls;
    }

    internal static string[] RunMatrixNegatives(nint nativeHandle, string matrixJson)
    {
        var mutations = new Dictionary<string, Action<JsonNode>>
        {
            ["type omission"] = root => root["types"]!.AsArray().RemoveAt(0),
            ["named-value omission"] = root => root["types"]![0]!["values"]!.AsArray().RemoveAt(0),
            ["named-value mutation"] = root => root["types"]![0]!["values"]![0]!["value"] = 12345,
            ["signedness mutation"] = root => root["types"]![0]!["nativeRepresentation"] = "unsigned int",
            ["high-bit omission"] = root => root["rawPatterns"]!.AsArray().RemoveAt(2),
            ["domain mutation"] = root => root["types"]![0]!["combinations"]![0]!["names"]![0] = "None"
        };
        foreach (var pair in mutations)
        {
            JsonNode root = JsonNode.Parse(matrixJson)!;
            pair.Value(root);
            bool rejected = false;
            try { RunMatrix(nativeHandle, root.ToJsonString()); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "matrix negative not rejected: " + pair.Key);
        }
        return mutations.Keys.ToArray();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Full enum managed ABI: " + message);
    }
}
