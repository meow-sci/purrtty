#include "fixture.h"
#include <stdio.h>
#include <string.h>
#include <math.h>

static char diagnostic[2048];
static const char* fail(int line, const char* condition) {
    snprintf(diagnostic, sizeof(diagnostic), "fixture.cpp:%d: %s; log=[%s] debug=[%s]", line, condition,
             GImGui->LogBuffer.c_str(), GImGui->DebugLogBuf.c_str());
    return diagnostic;
}
#define CHECK(x) do { if (!(x)) return fail(__LINE__, #x); } while (0)

PURR_API ImGuiContext* purr_manual_fixture_create() {
    ImGuiContext* ctx = ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.LogFilename = nullptr;
    io.DisplaySize = ImVec2(1200, 900);
    io.DeltaTime = 1.0f / 60.0f;
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    io.Fonts->AddFontDefault();
    ctx->DebugLogFlags = ImGuiDebugLogFlags_None;
    return ctx;
}
PURR_API void purr_manual_fixture_destroy(ImGuiContext* ctx) {
    ImGui::DestroyContext(ctx);
}
static void begin_frame() {
    ImGui::NewFrame();
    ImGui::SetNextWindowPos(ImVec2(30, 30));
    ImGui::SetNextWindowSize(ImVec2(1000, 800));
    ImGui::Begin("Manual ABI fixture", nullptr, ImGuiWindowFlags_NoSavedSettings);
}
static void end_frame() {
    ImGui::End();
    ImGui::Render();
}
// Always unwind context/frame/logger even when a native observation fails.
struct Frame {
    Frame() { begin_frame(); }
    ~Frame() { if (GImGui->LogEnabled) ImGui::LogFinish(); end_frame(); }
};
// Observed native stacks, never the callback's reported Bool8, own cleanup.
// This guard runs before Frame even on CHECK's early diagnostic return. It avoids
// depending on upstream End() recovery, with or without upstream assertions.
struct TreeScope {
    ImGuiWindow* window;
    int depth, ids;
    explicit TreeScope(ImGuiWindow* value) : window(value),
        depth(value ? value->DC.TreeDepth : 0), ids(value ? value->IDStack.Size : 0) {}
    void restore() {
        if (!window || GImGui->CurrentWindow != window) return;
        while (window->DC.TreeDepth > depth) ImGui::TreePop();
        while (window->IDStack.Size > ids) ImGui::PopID();
    }
    ~TreeScope() { restore(); }
};
static bool log_equal(const char* expected) {
    const char* text = GImGui->LogBuffer.c_str();
    // LogRenderedText may begin with a newline when entering a tooltip window.
    while (*text == '\n' || *text == '\r') ++text;
    return strcmp(text, expected) == 0;
}

static const char* exercise_case(PurrFormatExercise call, int id, int variant) {
    Frame frame;
    ImGuiContext& g = *GImGui;
    ImGuiWindow* window = g.CurrentWindow;
    g.DebugLogBuf.clear();
    g.DebugLogIndex.clear();
    g.LogBuffer.clear();
    const bool inactive = variant == 1 && (id == PurrSetItemTooltip || id == PurrLogText);
    if (id != PurrDebugLog && !(id == PurrLogText && inactive)) ImGui::LogToBuffer(0);
    if (id == PurrSetItemTooltip) {
        // Use real ItemAdd/Button + IsItemHovered logic, with deterministic native
        // input state. This fixture is not a backend hover-timing qualification.
        g.Style.HoverFlagsForTooltipMouse = ImGuiHoveredFlags_None;
        ImGui::Button("hover target");
        g.HoveredWindow = inactive ? nullptr : window;
        g.IO.MousePos = g.LastItemData.Rect.GetCenter();
        if (!inactive) g.LastItemData.StatusFlags |= ImGuiItemStatusFlags_HoveredRect;
        else g.LastItemData.StatusFlags &= ~ImGuiItemStatusFlags_HoveredRect;
        CHECK(ImGui::IsItemHovered(ImGuiHoveredFlags_ForTooltip) == !inactive);
        g.LogBuffer.clear();
        g.LogLineFirstItem = true;
    }
    const bool tree = id >= PurrTreeNode1 && id <= PurrTreeNodeEx2;
    if (tree) ImGui::SetNextItemOpen(variant == 0, ImGuiCond_Always);
    struct ColorGuard { uint32_t before; PurrVec4 color; uint32_t after; } color =
        {0x1234abcd, {0.25f, 0.5f, 0.75f, 1.0f}, 0xfedcba98};
    const char* string_id = "identity%%###manual-id";
    void* payload = id == PurrTextColored ? static_cast<void*>(&color.color) :
                    (id == PurrTreeNode1 || id == PurrTreeNodeEx1) ?
                    const_cast<char*>(string_id) : static_cast<void*>(&color);
    ImGuiID expected_id = (id == PurrTreeNode1 || id == PurrTreeNodeEx1) ?
                         window->GetID(string_id) : window->GetID(payload);
    int depth = window->DC.TreeDepth;
    int ids = window->IDStack.Size;
    ImVec2 cursor = window->DC.CursorPos;
    int vertices = window->DrawList->VtxBuffer.Size;
    int style_depth = g.ColorStack.Size;
    float wrap = window->DC.TextWrapPos;
    int tooltip_count = g.TooltipOverrideCount;
    TreeScope tree_scope(tree ? window : nullptr);
    uint8_t result = call(id, "manual %% value", payload, ImGuiTreeNodeFlags_SpanAvailWidth);
    CHECK(color.before == 0x1234abcd && color.after == 0xfedcba98);
    CHECK(color.color.x == 0.25f && color.color.y == 0.5f && color.color.z == 0.75f && color.color.w == 1.0f);
    CHECK(g.CurrentWindow == window && g.ColorStack.Size == style_depth && window->DC.TextWrapPos == wrap);
    if (tree) {
        CHECK(result == (variant == 0 ? 1 : 0));
        CHECK(g.LastItemData.ID == expected_id);
        CHECK(window->DC.TreeDepth == depth + (result ? 1 : 0));
        CHECK(window->IDStack.Size == ids + (result ? 1 : 0));
        tree_scope.restore();
        CHECK(window->DC.TreeDepth == depth && window->IDStack.Size == ids);
    }
    if (id == PurrDebugLog) {
        char expected[128];
        snprintf(expected, sizeof(expected), "[%05d] manual %% value", g.FrameCount);
        CHECK(strcmp(g.DebugLogBuf.c_str(), expected) == 0);
        CHECK(!g.LogEnabled && g.LogFile == nullptr);
    } else if (inactive) {
        CHECK(g.LogBuffer.empty());
        CHECK(g.TooltipOverrideCount == tooltip_count);
    } else if (id == PurrLabelText) {
        CHECK(log_equal("manual % value label%%"));
    } else if (tree) {
        CHECK(log_equal("> manual % value"));
    } else {
        CHECK(log_equal("manual % value"));
    }
    if (id == PurrSetTooltip || (id == PurrSetItemTooltip && !inactive)) {
        CHECK(g.TooltipOverrideCount == tooltip_count);
        bool rendered = false;
        for (ImGuiWindow* w : g.Windows)
            if ((w->Flags & ImGuiWindowFlags_Tooltip) && w->LastFrameActive == g.FrameCount && w->DrawList->VtxBuffer.Size > 0) rendered = true;
        CHECK(rendered);
        // A second tooltip in the SAME frame replaces the first, exercising the
        // real native override path rather than only observing logger output.
        call(id, "manual %% value", payload, ImGuiTreeNodeFlags_SpanAvailWidth);
        CHECK(g.TooltipOverrideCount == tooltip_count + 1);
    }
    if (id == PurrTextColored || id == PurrTextDisabled) {
        ImU32 expected = id == PurrTextColored ? ImGui::GetColorU32(purr::to_native(color.color)) : ImGui::GetColorU32(ImGuiCol_TextDisabled);
        CHECK(window->DrawList->VtxBuffer.Size > vertices);
        for (int i = vertices; i < window->DrawList->VtxBuffer.Size; ++i) CHECK(window->DrawList->VtxBuffer[i].col == expected);
    }
    if (id == PurrTextAligned) {
        float text_width = ImGui::CalcTextSize("manual % value").x;
        CHECK(fabsf(g.LastItemData.Rect.Min.x - cursor.x - floorf((300.0f - text_width) * 0.5f)) < 0.01f);
    }
    if (id != PurrDebugLog) CHECK(g.LogFile == nullptr);
    return nullptr;
}
PURR_API const char* purr_manual_fixture_formats(PurrFormatExercise call) {
    for (int id = PurrBulletText; id <= PurrTextAligned; ++id) {
        if (const char* error = exercise_case(call, id, 0)) return error;
        if (id == PurrSetItemTooltip || id == PurrLogText || (id >= PurrTreeNode1 && id <= PurrTreeNodeEx2))
            if (const char* error = exercise_case(call, id, 1)) return error;
    }
    return nullptr;
}

static void with_real_list(PurrTextVExercise call, const char* fmt, ...) {
    va_list original;
    va_start(original, fmt);
    // Consumers may consume their list. Each call receives a fresh compiler
    // va_copy. The producer owns va_end, stack/register-save areas, and strings.
    for (int i = 0; i < 2; ++i) {
        va_list copy;
        va_copy(copy, original);
        call(fmt, copy);
        va_end(copy);
    }
    va_end(original);
}
PURR_API const char* purr_manual_fixture_textv(PurrTextVExercise call) {
    Frame frame;
    GImGui->LogBuffer.clear();
    ImGui::LogToBuffer(-1);
    with_real_list(call, "int=%d float=%.*f string=%*s hex=%llx %%", -37, 3, 12.625, 7, "mixed", 0x123456789abcdef0ULL);
    CHECK(log_equal("int=-37 float=12.625 string=  mixed hex=123456789abcdef0 %\nint=-37 float=12.625 string=  mixed hex=123456789abcdef0 %"));
    return nullptr;
}
PURR_API ImGuiViewport* purr_manual_fixture_viewport() { return ImGui::GetMainViewport(); }
PURR_API PurrVec2 purr_manual_fixture_invoke(PurrNativeViewportVector call, ImGuiViewport* viewport) {
    return purr::from_native(call(viewport));
}
