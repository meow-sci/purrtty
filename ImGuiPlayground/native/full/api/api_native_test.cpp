// Quiet native fixtures. This is not a managed-ABI or layout qualification suite.
#include "purr_abi.h"
#include <stdio.h>
#include <stdlib.h>

extern "C" {
ImGuiContext* CreateContext(ImFontAtlas*);
void DestroyContext(ImGuiContext*);
ImGuiIO* GetIO();
ImGuiIO* GetIO_internal(ImGuiContext*);
PurrVec4* GetStyleColorVec4(int32_t);
PurrVec4 ColorConvertU32ToFloat4(uint32_t);
void ColorConvertRGBtoHSV(float, float, float, float*, float*, float*);
void ImRect_Translate_internal(PurrRect*, PurrVec2*);
void ImRect_Expand_internal_0(PurrRect*, float);
PurrVec4 ImRect_ToVec4_internal(PurrRect*);
PurrClipperRange ImGuiListClipperRange_FromIndices_internal(PurrClipperRange*, int32_t, int32_t);
PurrClipperRange ImGuiListClipperRange_FromPositions_internal(PurrClipperRange*, float, float, int32_t, int32_t);
intptr_t ImTextureRef_GetTexID(PurrTextureRef*);
PurrTextureRef ImTextureData_GetTexRef(ImTextureData*);
void ImTextureData_SetTexID(ImTextureData*, intptr_t);
void ImDrawList_ResetForNewFrame(ImDrawList*);
void ImDrawList_AddPolyline(ImDrawList*, PurrVec2*, int32_t, uint32_t, int32_t, float);
void ImDrawList_AddCallback(ImDrawList*, intptr_t, void*, uintptr_t);
void ImGuiStorage_SetBool(ImGuiStorage*, uint32_t, uint8_t);
uint8_t ImGuiStorage_GetBool(ImGuiStorage*, uint32_t, uint8_t);
uint8_t* ImGuiStorage_GetBoolRef(ImGuiStorage*, uint32_t, uint8_t);
void GetAllocatorFunctions(intptr_t*, intptr_t*, void**);
void SetAllocatorFunctions(intptr_t, intptr_t, void*);
void* MemAlloc(uintptr_t);
void MemFree(void*);
void SetNextItemSelectionUserData(uint64_t);
int32_t TableGetColumnNextSortDirection_internal(ImGuiTableColumn*);
void TableSetColumnSortDirection_internal(int32_t, int32_t, uint8_t);
}

#define CHECK(condition) do { if (!(condition)) { fprintf(stderr, "api_native_test.cpp:%d: %s\n", __LINE__, #condition); return 1; } } while (0)

static unsigned allocations = 0;
static unsigned releases = 0;
static void* counting_allocate(size_t size, void* user) {
    if (user != &allocations) abort();
    ++allocations;
    return malloc(size);
}
static void counting_release(void* ptr, void* user) {
    if (user != &allocations) abort();
    ++releases;
    free(ptr);
}
static void draw_callback(const ImDrawList*, const ImDrawCmd*) {}

int bool_pointer_tests();

int main() {
    CHECK(bool_pointer_tests() == 0);
    PurrRect rect = {{1, 2}, {3, 4}};
    // The argument aliases target.Min. Upstream consumes the updated Min for
    // Max's addition; a per-argument copy would produce the wrong (4,6) Max.
    ImRect_Translate_internal(&rect, &rect.Min);
    CHECK(rect.Min.x == 2 && rect.Min.y == 4 && rect.Max.x == 5 && rect.Max.y == 8);
    ImRect_Expand_internal_0(&rect, 2);
    PurrVec4 bounds = ImRect_ToVec4_internal(&rect);
    CHECK(bounds.x == 0 && bounds.y == 2 && bounds.z == 7 && bounds.w == 10);

    PurrClipperRange indices = ImGuiListClipperRange_FromIndices_internal(nullptr, -3, 77);
    CHECK(indices.Min == -3 && indices.Max == 77 && indices.PosToIndexConvert == 0);
    PurrClipperRange positions = ImGuiListClipperRange_FromPositions_internal(nullptr, 3.75f, 11.5f, -7, 6);
    CHECK(positions.Min == 3 && positions.Max == 11 && positions.PosToIndexConvert == 1);
    CHECK(positions.PosToIndexOffsetMin == -7 && positions.PosToIndexOffsetMax == 6);

    PurrVec4 color = ColorConvertU32ToFloat4(0x80402010u);
    CHECK(color.x == 16.0f/255 && color.y == 32.0f/255 && color.z == 64.0f/255 && color.w == 128.0f/255);
    float actual = 0, expected = 0;
    ImGui::ColorConvertRGBtoHSV(0.2f, 0.7f, 0.4f, expected, expected, expected);
    ColorConvertRGBtoHSV(0.2f, 0.7f, 0.4f, &actual, &actual, &actual);
    CHECK(actual == expected);

    uint8_t boolean = 0x80;
    {
        CHECK(purr::bool_inout(nullptr) == nullptr && purr::bool_output(nullptr) == nullptr);
        bool* first = purr::bool_inout(&boolean);
        bool* second = purr::bool_inout(&boolean);
        CHECK(first == second && reinterpret_cast<uint8_t*>(first) == &boolean && *first);
        CHECK(boolean == 1);
        *second = false;
        CHECK(boolean == 0);
        CHECK(purr::bits<uint8_t>(false) == 0 && purr::bits<uint8_t>(true) == 1);
    }
    CHECK(boolean == 0);

    const uint64_t texture_bits = UINT64_C(0xfedcba9876543210);
    PurrTextureRef texture = {nullptr, purr::bits<intptr_t>(texture_bits)};
    CHECK(purr::bits<uint64_t>(ImTextureRef_GetTexID(&texture)) == texture_bits);
    ImTextureData data;
    ImTextureData_SetTexID(&data, texture.TexID);
    PurrTextureRef native_texture = ImTextureData_GetTexRef(&data);
    CHECK(native_texture.TexData == &data && native_texture.TexID == 0);
    CHECK(purr::bits<uint64_t>(ImTextureRef_GetTexID(&native_texture)) == texture_bits);

    ImGuiStorage storage;
    ImGuiStorage_SetBool(&storage, 0xf1234567u, 0x80);
    CHECK(ImGuiStorage_GetBool(&storage, 0xf1234567u, 0) == 1);
    uint8_t* stable_boolean = ImGuiStorage_GetBoolRef(&storage, 0xf1234567u, 0);
    CHECK(stable_boolean == ImGuiStorage_GetBoolRef(&storage, 0xf1234567u, 0));
    *stable_boolean = 0;
    CHECK(ImGuiStorage_GetBool(&storage, 0xf1234567u, 0) == 0);

    ImGuiContext* context = CreateContext(nullptr);
    CHECK(context && GetIO() == &context->IO && GetIO_internal(context) == &context->IO);
    CHECK(GetStyleColorVec4(ImGuiCol_Text) == reinterpret_cast<PurrVec4*>(&context->Style.Colors[ImGuiCol_Text]));
    SetNextItemSelectionUserData(texture_bits);
    CHECK(purr::bits<uint64_t>(context->NextItemData.SelectionUserData) == texture_bits);
    {
        ImGuiTableColumn column;
        ImGuiTable table;
        table.Columns = ImSpan<ImGuiTableColumn>(&column, 1);
        table.ColumnsCount = 1;
        table.Flags = ImGuiTableFlags_Sortable | ImGuiTableFlags_SortTristate;
        context->CurrentTable = &table;
        column.SortDirectionsAvailMask = 7;
        column.SortDirectionsAvailCount = 1;
        for (int32_t direction = 0; direction != 3; ++direction) {
            column.SortDirectionsAvailList = static_cast<ImU8>(direction);
            column.SortOrder = -1;
            CHECK(TableGetColumnNextSortDirection_internal(&column) == direction);
            TableSetColumnSortDirection_internal(0, direction, 0);
            CHECK(column.SortDirection == direction);
        }
        context->CurrentTable = nullptr;
    }
    {
        ImDrawListSharedData shared;
        ImDrawList draw_list(&shared);
        ImDrawList_ResetForNewFrame(&draw_list);
        draw_list.Flags = ImDrawListFlags_None;
        const PurrVec2 points[] = {{0, 0}, {10, 0}, {10, 10}};
        ImDrawList_AddPolyline(&draw_list, const_cast<PurrVec2*>(points), 3, 0xffffffffu, 0, 1.0f);
        CHECK(draw_list.VtxBuffer.Size == 8 && draw_list.IdxBuffer.Size == 12);
        int payload = 123;
        ImDrawList_AddCallback(&draw_list, purr::bits<intptr_t>(&draw_callback), &payload, sizeof(payload));
        CHECK(draw_list.CmdBuffer[draw_list.CmdBuffer.Size-2].UserCallback == &draw_callback);
        CHECK(draw_list.CmdBuffer[draw_list.CmdBuffer.Size-2].UserCallbackDataSize == sizeof(payload));
        ImDrawList_AddCallback(&draw_list, -8, nullptr, 0);
        CHECK(draw_list.CmdBuffer[draw_list.CmdBuffer.Size-2].UserCallback == ImDrawCallback_ResetRenderState);
    }
    DestroyContext(context);

    intptr_t allocate = 0, release = 0;
    void* user = nullptr;
    GetAllocatorFunctions(&allocate, &release, &user);
    void* aliased = reinterpret_cast<void*>(uintptr_t(1));
    GetAllocatorFunctions(reinterpret_cast<intptr_t*>(&aliased), reinterpret_cast<intptr_t*>(&aliased), &aliased);
    CHECK(aliased == user); // Last native output wins, even across callback/data types.
    SetAllocatorFunctions(purr::bits<intptr_t>(&counting_allocate), purr::bits<intptr_t>(&counting_release), &allocations);
    void* block = MemAlloc(32);
    CHECK(block && allocations == 1);
    MemFree(block);
    CHECK(releases == 1);
    SetAllocatorFunctions(allocate, release, user);
    return 0;
}
