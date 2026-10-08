#include "fixture.h"
#include <stdio.h>
#include <string.h>

static uint8_t exercise(int32_t id, const char* fmt, void* payload, int32_t flags) {
    switch (id) {
    case PurrBulletText: BulletText(fmt); break;
    case PurrDebugLog: DebugLog(fmt); break;
    case PurrLabelText: LabelText("label%%", fmt); break;
    case PurrLogText: LogText(fmt); break;
    case PurrSetItemTooltip: SetItemTooltip(fmt); break;
    case PurrSetTooltip: SetTooltip(fmt); break;
    case PurrText: Text(fmt); break;
    case PurrTextColored: TextColored(static_cast<PurrVec4*>(payload), fmt); break;
    case PurrTextDisabled: TextDisabled(fmt); break;
    case PurrTextWrapped: TextWrapped(fmt); break;
    case PurrTreeNode1: return TreeNode_1(static_cast<const char*>(payload), fmt);
    case PurrTreeNode2: return TreeNode_2(payload, fmt);
    case PurrTreeNodeEx1: return TreeNodeEx_1(static_cast<const char*>(payload), flags, fmt);
    case PurrTreeNodeEx2: return TreeNodeEx_2(payload, flags, fmt);
    case PurrTextAligned: TextAligned_internal(0.5f, 300.0f, fmt); break;
    }
    return 0;
}
static ImGuiViewport* seen;
static PurrVec2 callback(ImGuiViewport* viewport) { seen = viewport; return {-17.25f, 98.5f}; }
static PurrVec2 replacement(ImGuiViewport* viewport) { seen = viewport; return {35.125f, -0.75f}; }
static uint8_t wrong_format(int32_t id, const char*, void* payload, int32_t flags) {
    return exercise(id, "manual %%%% value", payload, flags);
}
static int wrong_tree_case;
static uint8_t wrong_tree_value;
static uint8_t wrong_tree_result(int32_t id, const char* fmt, void* payload, int32_t flags) {
    uint8_t actual = exercise(id, fmt, payload, flags);
    return id == wrong_tree_case && actual == wrong_tree_value ? uint8_t(!actual) : actual;
}
#define REQUIRE(x) do { if (!(x)) { fprintf(stderr, "native_test.cpp:%d: %s\n", __LINE__, #x); return 1; } } while (0)
int main() {
    ImGuiContext* ctx = purr_manual_fixture_create();
    const char* error = purr_manual_fixture_formats(exercise);
    if (error) fprintf(stderr, "%s\n", error);
    REQUIRE(!error);
    error = purr_manual_fixture_textv(&TextV);
    if (error) fprintf(stderr, "%s\n", error);
    REQUIRE(!error);
    REQUIRE(purr_manual_fixture_formats(wrong_format) != nullptr);
    for (wrong_tree_case = PurrTreeNode1; wrong_tree_case <= PurrTreeNodeEx2; ++wrong_tree_case) {
        for (wrong_tree_value = 0; wrong_tree_value <= 1; ++wrong_tree_value) {
            error = purr_manual_fixture_formats(wrong_tree_result);
            REQUIRE(error && strstr(error, "result == (variant == 0 ? 1 : 0)") != nullptr);
            REQUIRE(purr_manual_fixture_formats(exercise) == nullptr);
        }
    }
    // These eight real-endpoint wrong-result cases must return their ORIGINAL
    // mismatch diagnostic, leave the fixture reusable, and permit destruction.
    purr_manual_fixture_destroy(ctx);
    ctx = purr_manual_fixture_create();
    REQUIRE(purr_manual_fixture_textv(&TextV) == nullptr);
    PurrManagedViewportVector* slots[] = {
        &Platform_GetWindowFramebufferScale_ManagedFunctionPointer,
        &Platform_GetWindowPos_ManagedFunctionPointer,
        &Platform_GetWindowSize_ManagedFunctionPointer
    };
    PurrNativeViewportVector getters[] = {
        Get_GetWindowFramebufferScale_InteropPointer(), Get_GetWindowPos_InteropPointer(), Get_GetWindowSize_InteropPointer()
    };
    ImGuiViewport* viewport = purr_manual_fixture_viewport();
    for (int i = 0; i < 3; ++i) {
        REQUIRE(getters[i] && !*slots[i]);
        *slots[i] = callback;
        for (int j = 0; j < 3; ++j)
            if (j != i) REQUIRE(slots[j] != slots[i] && getters[j] != getters[i] && !*slots[j]);
        PurrVec2 value = purr_manual_fixture_invoke(getters[i], viewport);
        REQUIRE(value.x == -17.25f && value.y == 98.5f && seen == viewport);
        *slots[i] = replacement;
        value = purr_manual_fixture_invoke(getters[i], viewport);
        REQUIRE(value.x == 35.125f && value.y == -0.75f && seen == viewport);
        *slots[i] = nullptr;
        REQUIRE(!*slots[i]); // Never invoke an inactive trampoline.
    }
    purr_manual_fixture_destroy(ctx);
    return 0;
}
