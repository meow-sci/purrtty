// Supplemental test instrumentation only. Never a production component export.
#include "../api/purr_abi.h"

struct ProfileFixture {
    ImGuiContext* context;
    ImTextureData texture;
    ImGuiStorage storage;
    ImGuiDockNode dock;
    ImGuiTableColumn column;
    ImGuiTable table;
    ImGuiWindow window;
    ImDrawListSharedData shared;
    ImDrawList draw;
    explicit ProfileFixture(ImGuiContext* c) : context(c), dock(0x1234), window(c, "profile-fixture"), draw(&shared) {
        dock.Pos = ImVec2(-7.5f, 3.25f);
        dock.Size = ImVec2(19.0f, 21.5f);
        table.Columns = ImSpan<ImGuiTableColumn>(&column, 1);
        table.ColumnsCount = 1;
        table.Flags = ImGuiTableFlags_Sortable | ImGuiTableFlags_SortTristate;
        column.SortDirectionsAvailMask = 7;
        column.SortDirectionsAvailCount = 1;
        c->CurrentTable = &table;
        c->IO.ConfigMacOSXBehaviors = false; // Explicit fixture choice: no Ctrl/Super remapping.
        window.SkipItems = true;
        c->CurrentWindow = &window;
        draw._ResetForNewFrame();
        draw.Flags = ImDrawListFlags_None;
    }
    ~ProfileFixture() { context->CurrentTable = nullptr; context->CurrentWindow = nullptr; }
};
PURR_API ProfileFixture* profile_fixture_create(ImGuiContext* c) { return IM_NEW(ProfileFixture)(c); }
PURR_API void profile_fixture_destroy(ProfileFixture* p) { IM_DELETE(p); }
PURR_API void* profile_fixture_object(ProfileFixture* p, int32_t index) {
    switch(index) {
    case 0: return &p->texture;
    case 1: return &p->storage;
    case 2: return &p->dock;
    case 3: return &p->column;
    case 4: return &p->draw;
    case 5: return &p->context->Style.Colors[ImGuiCol_Text];
    default: return nullptr;
    }
}
PURR_API void profile_fixture_sort_prepare(ProfileFixture* p, int32_t direction) {
    p->column.SortDirectionsAvailList = static_cast<ImU8>(direction);
    p->column.SortOrder = -1;
    p->table.IsSortSpecsDirty = false;
}
PURR_API int32_t profile_fixture_observe(ProfileFixture* p, int32_t index) {
    switch(index) {
    case 0: return p->column.SortDirection;
    case 1: return p->table.IsSortSpecsDirty ? 1 : 0;
    case 2: return p->draw.VtxBuffer.Size;
    case 3: return p->draw.IdxBuffer.Size;
    case 4: return p->context->InputEventsQueue.Size;
    case 5: return p->context->NextItemData.Shortcut;
    case 6: return p->context->NextItemData.ShortcutFlags;
    case 7: return (p->context->NextItemData.HasFlags & ImGuiNextItemDataFlags_HasShortcut) != 0;
    default: return -1;
    }
}
PURR_API uint64_t profile_fixture_key_event(ProfileFixture* p, int32_t index) {
    if (index < 0 || index >= p->context->InputEventsQueue.Size) return UINT64_MAX;
    const ImGuiInputEvent& event = p->context->InputEventsQueue[index];
    return (uint64_t(event.Key.Key) << 32) | (event.Key.Down ? 1u : 0u);
}
