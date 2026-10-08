// Actual exported Bool8 forwarding regressions, alongside direct upstream calls.
#include "purr_abi.h"
#include <stdio.h>

extern "C" {
uint8_t Begin(uint8_t*, uint8_t*, int32_t);
uint8_t BeginPopupModal(uint8_t*, uint8_t*, int32_t);
void SetNextWindowSizeConstraints(PurrVec2*, PurrVec2*, intptr_t, void*);
uint8_t ButtonBehavior_internal(PurrRect*, uint32_t, uint8_t*, uint8_t*, int32_t);
void TabItemLabelAndCloseButton_internal(ImDrawList*, PurrRect*, int32_t, PurrVec2, uint8_t*, uint32_t, uint32_t, uint8_t, uint8_t*, uint8_t*);
}

#define CHECK(condition) do { if (!(condition)) { fprintf(stderr, "bool_pointer_test.cpp:%d: %s\n", __LINE__, #condition); return 1; } } while (0)

struct CallbackAlias {
    uint8_t* cell;
    unsigned calls;
    bool observed_noncanonical;
};
static void close_alias(ImGuiSizeCallbackData* data) {
    CallbackAlias* state = static_cast<CallbackAlias*>(data->UserData);
    ++state->calls;
    state->observed_noncanonical |= *state->cell > 1;
    *state->cell = 0; // Canonical callback write through the ORIGINAL cell address.
}
static ImGuiContext* start_frame() {
    ImGuiContext* ctx = ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.DisplaySize = ImVec2(800, 600);
    io.DeltaTime = 1.0f / 60;
    io.BackendFlags |= ImGuiBackendFlags_RendererHasTextures;
    io.Fonts->AddFontDefault();
    ImGui::NewFrame();
    return ctx;
}
struct Outcome { uint8_t open; bool visible; unsigned calls; bool noncanonical; };
static Outcome callback_case(bool wrapped, bool modal) {
    ImGuiContext* ctx = start_frame();
    uint8_t open = wrapped ? 0x80 : 1;
    CallbackAlias state = {&open, 0, false};
    if (modal) {
        ImGui::Begin("Popup parent");
        ImGui::OpenPopup("Alias modal");
    }
    PurrVec2 low = {50, 50}, high = {1000, 1000};
    SetNextWindowSizeConstraints(&low, &high, purr::bits<intptr_t>(&close_alias), &state);
    ImGui::SetNextWindowSize(ImVec2(200, 100));
    const int flags = ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoDocking;
    uint8_t name[] = "Alias window";
    uint8_t popup[] = "Alias modal";
    bool visible;
    if (modal) {
        visible = wrapped ? ::BeginPopupModal(popup, &open, flags) != 0
                          : ImGui::BeginPopupModal("Alias modal", reinterpret_cast<bool*>(&open), flags);
        // BeginPopupModal re-reads p_open after the nested Begin callback closes
        // it, then balances its own EndPopup. Only end explicitly if still open.
        if (visible) ImGui::EndPopup();
        ImGui::End();
    } else {
        visible = wrapped ? ::Begin(name, &open, flags) != 0
                          : ImGui::Begin("Alias window", reinterpret_cast<bool*>(&open), flags);
        ImGui::End();
    }
    ImGui::EndFrame();
    ImGui::DestroyContext(ctx);
    return {open, visible, state.calls, state.observed_noncanonical};
}

int bool_pointer_tests() {
    for (int modal = 0; modal != 2; ++modal) {
        Outcome direct = callback_case(false, modal != 0);
        Outcome wrapped = callback_case(true, modal != 0);
        CHECK(direct.calls > 0 && direct.calls == wrapped.calls);
        CHECK(direct.open == 0 && wrapped.open == direct.open);
        CHECK(direct.visible == wrapped.visible);
        CHECK(!direct.noncanonical && !wrapped.noncanonical);
        if (modal) CHECK(!wrapped.visible); // A no-copyback-only fix still fails here.
    }

    ImGuiContext* ctx = start_frame();
    ImGui::SetNextWindowPos(ImVec2(0, 0));
    ImGui::SetNextWindowSize(ImVec2(400, 300));
    uint8_t window_name[] = "Output fixture";
    ::Begin(window_name, nullptr, ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoDocking);
    ImGuiWindow* window = ImGui::GetCurrentWindow();
    ctx->HoveredWindow = window;
    ctx->IO.MousePos = ImVec2(150, 150);
    PurrRect box = {{100, 100}, {200, 200}};
    uint32_t id = ImGui::GetID("Output button");
    uint8_t hovered, held; // Deliberately uninitialized: these are native OUT cells.
    ButtonBehavior_internal(&box, id, &hovered, &held, ImGuiButtonFlags_None);
    CHECK(hovered == 1 && held == 0);
    uint8_t aliased;
    ButtonBehavior_internal(&box, id, &aliased, &aliased, ImGuiButtonFlags_None);
    CHECK(aliased == held); // Native hovered/held write order, not copy-back order.
    ButtonBehavior_internal(&box, id, nullptr, nullptr, ImGuiButtonFlags_None);

    uint8_t label[] = "Long clipped tab label";
    PurrVec2 padding = {0, 0};
    PurrRect tab = {{20, 20}, {30, 40}};
    uint8_t closed, clipped; // Both output-only; prior bytes must never be read.
    TabItemLabelAndCloseButton_internal(window->DrawList, &tab, 0, padding, label, 42, 0, 1, &closed, &clipped);
    CHECK(closed == 0 && clipped == 1);
    TabItemLabelAndCloseButton_internal(window->DrawList, &tab, 0, padding, label, 42, 0, 1, &aliased, &aliased);
    CHECK(aliased == closed); // Native final close write follows the clipping write.
    TabItemLabelAndCloseButton_internal(window->DrawList, &tab, 0, padding, label, 42, 0, 1, nullptr, nullptr);
    tab.Max.x = tab.Min.x; // Early return still initializes optional output cells.
    closed = clipped = 0x80;
    TabItemLabelAndCloseButton_internal(window->DrawList, &tab, 0, padding, label, 42, 0, 1, &closed, &clipped);
    CHECK(closed == 0 && clipped == 0);
    ImGui::End();
    ImGui::EndFrame();
    ImGui::DestroyContext(ctx);
    return 0;
}
