#pragma once
#include <string.h>

__attribute__((noinline)) static ImGuiSortDirection LayoutSortDirectionRoundtrip(ImGuiSortDirection value)
{
    volatile ImGuiSortDirection observed = value;
    return observed;
}

// Only declaration-promotion domain checks, not general bitfield semantics.
static bool LayoutPromotionDomains()
{
#if LAYOUT_PATCHED
    ImGuiBoxSelectState box_object;
    volatile ImGuiBoxSelectState& box = box_object;
    for (int bits = 0; bits != 16; ++bits)
    {
        int keys = ((bits & 1) ? ImGuiMod_Ctrl : 0) | ((bits & 2) ? ImGuiMod_Shift : 0)
                 | ((bits & 4) ? ImGuiMod_Alt : 0) | ((bits & 8) ? ImGuiMod_Super : 0);
        box.KeyMods = keys;
        if (box.KeyMods != keys) return false;
    }
    ImGuiStackLevelInfo stack_object;
    volatile ImGuiStackLevelInfo& stack = stack_object;
    for (int type = ImGuiDataType_S8; type <= ImGuiDataType_ID; ++type)
    {
        stack.DataType = type;
        if (stack.DataType != type) return false;
    }
    ImGuiContext* context = ImGui::CreateContext();
    bool ok = true;
    for (int button = -1; button < ImGuiMouseButton_COUNT; ++button)
    {
        volatile ImGuiContext* observed = context;
        observed->ActiveIdMouseButton = button;
        ok &= observed->ActiveIdMouseButton == button;
    }
    context->ActiveIdMouseButton = -1;
    ImGui::DestroyContext(context);
    for (int value = ImGuiSortDirection_None; value <= ImGuiSortDirection_Descending; ++value)
    {
        ImGuiTableColumnSortSpecs sort;
        sort.SortDirection = LayoutSortDirectionRoundtrip(static_cast<ImGuiSortDirection>(value));
        volatile ImGuiTableColumnSortSpecs& observed = sort;
        ok &= static_cast<int>(observed.SortDirection) == value;
    }
    return ok;
#else
    return true;
#endif
}

// Allocation-boundary observation only: first/last changed byte after assigning
// two real bool members. No mask/shift/signedness claim for the 47-member manifest.
static int LayoutDockBoundary(bool last)
{
    alignas(ImGuiDockNode) unsigned char storage[sizeof(ImGuiDockNode)] = {};
    ImGuiDockNode& node = *IM_PLACEMENT_NEW(storage) ImGuiDockNode(0);
    node.IsVisible = false;
    node.WantHiddenTabBarToggle = false;
    unsigned char before[sizeof(node)], after[sizeof(node)];
    memcpy(before, &node, sizeof(node));
    if (last) node.WantHiddenTabBarToggle = true; else node.IsVisible = true;
    memcpy(after, &node, sizeof(node));
    int byte = -1;
    for (size_t i = 0; i < sizeof(node); ++i)
        if (before[i] != after[i]) { if (byte != -1) { byte = -2; break; } byte = static_cast<int>(i); }
    node.~ImGuiDockNode();
    return byte;
}

int main()
{
    if (!LayoutPromotionDomains()) return 2;
    ImGuiViewportP viewport;
    ImGuiViewport* base = &viewport;
    const ptrdiff_t base_offset = (const char*)base - (const char*)&viewport;
    if (sizeof(stbrp_context) > sizeof(stbrp_context_opaque) || alignof(stbrp_context) > alignof(ImFontAtlasBuilder)) return 3;
    printf("{\"schema\":\"playground_imgui.native-storage-measurements\",\"version\":1,\"pointerBytes\":%zu,\"entries\":[\n", sizeof(void*));
    for (size_t i = 0; i < playground_layout_entry_count; ++i)
    {
        LayoutEntry e = playground_layout_entries[i];
        if (e.type_id == LayoutDerivedId && e.field_id >= 0)
            e.offset = LayoutDerivedOffset(e.field_id, viewport);
        printf("%s[%lld,%lld,%lld,%lld,%lld,%lld,%lld,%lld]", i ? ",\n" : "",
               (long long)e.type_id, (long long)e.field_id, (long long)e.size,
               (long long)e.alignment, (long long)e.offset, (long long)e.array_count,
               (long long)e.element_stride, (long long)e.signedness);
    }
    printf("\n],\"baseSubobject\":{\"type\":\"ImGuiViewportP\",\"base\":\"ImGuiViewport\",\"offsetBytes\":%td,\"sizeBytes\":%zu,\"alignment\":%zu},", base_offset, sizeof(ImGuiViewport), alignof(ImGuiViewport));
    printf("\"rectPackContext\":{\"sizeBytes\":%zu,\"alignment\":%zu,\"capacityBytes\":%zu},", sizeof(stbrp_context), alignof(stbrp_context), sizeof(stbrp_context_opaque));
    printf("\"dockBoolBoundary\":{\"IsVisibleByte\":%d,\"WantHiddenTabBarToggleByte\":%d},", LayoutDockBoundary(false), LayoutDockBoundary(true));
    printf("\"bitfieldDeclaredTypes\":[");
    for (size_t i = 0; i < sizeof(LayoutBitfieldDeclaredTypes)/sizeof(LayoutBitfieldDeclaredTypes[0]); ++i)
        printf("%s[%lld,%lld,%lld]", i ? "," : "", (long long)LayoutBitfieldDeclaredTypes[i][0],
               (long long)LayoutBitfieldDeclaredTypes[i][1], (long long)LayoutBitfieldDeclaredTypes[i][2]);
    printf("],\"enumValues\":[");
    for (size_t i = 0; i < sizeof(LayoutEnumValues)/sizeof(LayoutEnumValues[0]); ++i)
        printf("%s[%lld,%lld,%lld]", i ? "," : "", (long long)LayoutEnumValues[i][0],
               (long long)LayoutEnumValues[i][1], (long long)LayoutEnumValues[i][2]);
#if LAYOUT_PATCHED
    printf("],\"promotionDomainsPassed\":true}\n");
#else
    printf("],\"promotionDomainsPassed\":null}\n");
#endif
    return ferror(stdout) ? 4 : 0;
}
