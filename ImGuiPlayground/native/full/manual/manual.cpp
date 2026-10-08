#include "manual.h"

// The casts pin native overloads, including the ellipsis. The exported functions
// themselves are FIXED signatures: the unchanged managed imports pass no varargs.
// Calling the real printf endpoint preserves %% (unlike "%s" forwarding).
PURR_API void BulletText(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::BulletText)(fmt);
}
PURR_API void DebugLog(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::DebugLog)(fmt);
}
PURR_API void LabelText(const char* label, const char* fmt) {
    using NativeCall = void (*)(const char*, const char*, ...);
    static_cast<NativeCall>(&ImGui::LabelText)(label, fmt);
}
PURR_API void LogText(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::LogText)(fmt);
}
PURR_API void SetItemTooltip(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::SetItemTooltip)(fmt);
}
PURR_API void SetTooltip(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::SetTooltip)(fmt);
}
PURR_API void Text(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::Text)(fmt);
}
PURR_API void TextColored(const PurrVec4* col, const char* fmt) {
    using NativeCall = void (*)(const ImVec4&, const char*, ...);
    static_cast<NativeCall>(&ImGui::TextColored)(purr::to_native(*col), fmt);
}
PURR_API void TextDisabled(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::TextDisabled)(fmt);
}
PURR_API void TextWrapped(const char* fmt) {
    using NativeCall = void (*)(const char*, ...);
    static_cast<NativeCall>(&ImGui::TextWrapped)(fmt);
}
PURR_API uint8_t TreeNode_1(const char* str_id, const char* fmt) {
    using NativeCall = bool (*)(const char*, const char*, ...);
    return static_cast<NativeCall>(&ImGui::TreeNode)(str_id, fmt) ? 1 : 0;
}
PURR_API uint8_t TreeNode_2(const void* ptr_id, const char* fmt) {
    using NativeCall = bool (*)(const void*, const char*, ...);
    return static_cast<NativeCall>(&ImGui::TreeNode)(ptr_id, fmt) ? 1 : 0;
}
PURR_API uint8_t TreeNodeEx_1(const char* str_id, int32_t flags, const char* fmt) {
    using NativeCall = bool (*)(const char*, ImGuiTreeNodeFlags, const char*, ...);
    return static_cast<NativeCall>(&ImGui::TreeNodeEx)(str_id, flags, fmt) ? 1 : 0;
}
PURR_API uint8_t TreeNodeEx_2(const void* ptr_id, int32_t flags, const char* fmt) {
    using NativeCall = bool (*)(const void*, ImGuiTreeNodeFlags, const char*, ...);
    return static_cast<NativeCall>(&ImGui::TreeNodeEx)(ptr_id, flags, fmt) ? 1 : 0;
}
PURR_API void TextAligned_internal(float align_x, float size_x, const char* fmt) {
    using NativeCall = void (*)(float, float, const char*, ...);
    static_cast<NativeCall>(&ImGui::TextAligned)(align_x, size_x, fmt);
}
PURR_API void TextV(const char* fmt, va_list args) {
    using NativeCall = void (*)(const char*, va_list);
    static_cast<NativeCall>(&ImGui::TextV)(fmt, args);
}

extern "C" {
PURR_EXPORT PurrManagedViewportVector Platform_GetWindowFramebufferScale_ManagedFunctionPointer = nullptr;
PURR_EXPORT PurrManagedViewportVector Platform_GetWindowPos_ManagedFunctionPointer = nullptr;
PURR_EXPORT PurrManagedViewportVector Platform_GetWindowSize_ManagedFunctionPointer = nullptr;
}
static_assert(sizeof(PurrManagedViewportVector) == sizeof(void*), "slot width");
static_assert(sizeof(PurrNativeViewportVector) == sizeof(void*), "getter width");

// A live callback is required. An empty slot is INACTIVE, not a zero-vector
// callback. Install, invoke, replace, and clear only on the owning UI thread;
// retain the callback and library until calls have completed and slot is clear.
static ImVec2 framebuffer_scale(ImGuiViewport* viewport) {
    return purr::to_native(Platform_GetWindowFramebufferScale_ManagedFunctionPointer(viewport));
}
static ImVec2 window_pos(ImGuiViewport* viewport) {
    return purr::to_native(Platform_GetWindowPos_ManagedFunctionPointer(viewport));
}
static ImVec2 window_size(ImGuiViewport* viewport) {
    return purr::to_native(Platform_GetWindowSize_ManagedFunctionPointer(viewport));
}
PURR_API PurrNativeViewportVector Get_GetWindowFramebufferScale_InteropPointer() { return &framebuffer_scale; }
PURR_API PurrNativeViewportVector Get_GetWindowPos_InteropPointer() { return &window_pos; }
PURR_API PurrNativeViewportVector Get_GetWindowSize_InteropPointer() { return &window_size; }
