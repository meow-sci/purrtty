using System.Runtime.InteropServices;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;
using Imports = Brutal.ImGuiApi.ImGui.PInvoke;
using InternalImports = Brutal.ImGuiApi.Internal.PInvoke;

// Caller owns the one native handle/resolver and contributor-identity checks.
// Requires the explicitly supplemental profile_fixture_* instrumentation in that same image.
internal static unsafe class FullProfileAbiChecks
{
    internal static string[] Run(nint nativeHandle)
    {
        var create = (delegate* unmanaged[Cdecl]<ImGuiContext*, nint>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_create");
        var destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_destroy");
        var obj = (delegate* unmanaged[Cdecl]<nint, int, void*>)NativeLibrary.GetExport(nativeHandle, "profile_fixture_object");
        ImGuiContext* context = Imports.CreateContext(null);
        Require(context != null, "real context lifetime");
        nint fixture = create(context);
        try
        {
            floatRect rect = new() { Min = new float2(-7.5f, 3.25f), Max = new float2(11.5f, 24.75f) };
            float2 center = InternalImports.ImRect_GetCenter_internal(&rect);
            Require(center.X == 2f && center.Y == 14f, "float2 return");
            floatRect bounds = InternalImports.ImGuiDockNode_Rect_internal((ImGuiDockNode*)obj(fixture, 2));
            Require(bounds.Min.X == -7.5f && bounds.Min.Y == 3.25f && bounds.Max.X == 11.5f && bounds.Max.Y == 24.75f, "floatRect return");
            float4 vector = InternalImports.ImRect_ToVec4_internal(&rect);
            Require(vector.X == -7.5f && vector.Y == 3.25f && vector.Z == 11.5f && vector.W == 24.75f, "float4 return");
            InternalImports.ImRect_Translate_internal(&rect, &rect.Min);
            Require(rect.Min.X == -15f && rect.Min.Y == 6.5f && rect.Max.X == -3.5f && rect.Max.Y == 31.25f, "managed Rect/Vec interior alias retains original native operation ordering");
            var texture = (ImTextureData*)obj(fixture, 0);
            nint identity = unchecked((nint)0xfedcba9876543210UL);
            Imports.ImTextureData_SetTexID(texture, (ImTextureID)identity);
            ImTextureRef reference = Imports.ImTextureData_GetTexRef(texture);
            Require((ImTextureData*)reference._TexData == texture && (nint)reference._TexID == 0, "ImTextureRef aggregate identity");
            Require((nint)Imports.ImTextureRef_GetTexID(&reference) == identity, "ImTextureRef high-bit identity resolution");
            ImTextureRef direct = new() { _TexID = (ImTextureID)identity };
            Require((nint)Imports.ImTextureRef_GetTexID(&direct) == identity, "direct high-bit texture scalar transport");
            ImGuiListClipperRange indices = InternalImports.ImGuiListClipperRange_FromIndices_internal(null, -3, 77);
            Require(indices.Min == -3 && indices.Max == 77 && !indices.PosToIndexConvert, "clipper indices return");
            ImGuiListClipperRange positions = InternalImports.ImGuiListClipperRange_FromPositions_internal(null, -3.75f, 11.5f, -7, 6);
            Require(positions.Min == -3 && positions.Max == 11 && positions.PosToIndexConvert && positions.PosToIndexOffsetMin == -7 && positions.PosToIndexOffsetMax == 6, "clipper mixed aggregate return");
            float4* color = Imports.GetStyleColorVec4(ImGuiCol.Text);
            Require(color == obj(fixture, 5) && color == Imports.GetStyleColorVec4(ImGuiCol.Text), "stable-reference original address");
            float4 saved = *color;
            *color = new float4(0.125f, 0.25f, 0.5f, 1f);
            Require(Imports.GetStyleColorVec4(ImGuiCol.Text)->Z == 0.5f, "reference writes remain live");
            *color = saved;
            Require(Imports.GetIO() == InternalImports.GetIO_internal(context), "stable IO reference identity");
            var storage = (ImGuiStorage*)obj(fixture, 1);
            byte noncanonical = 0x80;
            Imports.ImGuiStorage_SetBool(storage, 0xf1234567u, *(Bool8*)&noncanonical);
            Bool8 scalar = Imports.ImGuiStorage_GetBool(storage, 0xf1234567u, false);
            Require(*(byte*)&scalar == 1, "Bool8 scalar canonicalization");
            Bool8* cell = Imports.ImGuiStorage_GetBoolRef(storage, 0xf1234567u, false);
            Require(cell == Imports.ImGuiStorage_GetBoolRef(storage, 0xf1234567u, false), "Bool8 stable cell address");
            *cell = false;
            Require(!Imports.ImGuiStorage_GetBool(storage, 0xf1234567u, true), "Bool8 reference write");
            byte* neighbors = stackalloc byte[] { 0xa5, 0x80, 0x5a };
            byte* label = stackalloc byte[] { (byte)'x', 0 };
            Require(!Imports.Checkbox(label, (Bool8*)(neighbors + 1)), "real skip-items Checkbox result");
            Require(neighbors[0] == 0xa5 && neighbors[1] == 1 && neighbors[2] == 0x5a, "Bool8 pointer normalization and neighbors");
            return ["float2", "floatRect", "float4", "ImTextureRef/high-bit identity", "ImGuiListClipperRange/both factories", "stable references", "Bool8 scalar/pointer/canonical shared writes"];
        }
        finally
        {
            destroy(fixture);
            Imports.DestroyContext(context);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Full profile managed ABI: " + message);
    }
}
