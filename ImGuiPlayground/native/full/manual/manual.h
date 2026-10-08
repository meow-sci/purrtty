// Owned fixed managed ABI. va_list comes ONLY from the selected compiler headers.
#ifndef PURR_MANUAL_H
#define PURR_MANUAL_H
#include "../api/purr_abi.h"
#include <stdarg.h>

PURR_API void BulletText(const char* fmt);
PURR_API void DebugLog(const char* fmt);
PURR_API void LabelText(const char* label, const char* fmt);
PURR_API void LogText(const char* fmt);
PURR_API void SetItemTooltip(const char* fmt);
PURR_API void SetTooltip(const char* fmt);
PURR_API void Text(const char* fmt);
PURR_API void TextColored(const PurrVec4* col, const char* fmt);
PURR_API void TextDisabled(const char* fmt);
PURR_API void TextWrapped(const char* fmt);
PURR_API uint8_t TreeNode_1(const char* str_id, const char* fmt);
PURR_API uint8_t TreeNode_2(const void* ptr_id, const char* fmt);
PURR_API uint8_t TreeNodeEx_1(const char* str_id, int32_t flags, const char* fmt);
PURR_API uint8_t TreeNodeEx_2(const void* ptr_id, int32_t flags, const char* fmt);
PURR_API void TextAligned_internal(float align_x, float size_x, const char* fmt);
PURR_API void TextV(const char* fmt, va_list args);

#ifdef __cplusplus
// A borrowed pointer, not a copied viewport. Both callback and native aggregate
// return conventions are compiled separately; the trampoline converts fields.
typedef PurrVec2 (*PurrManagedViewportVector)(ImGuiViewport*);
typedef ImVec2 (*PurrNativeViewportVector)(ImGuiViewport*);
extern "C" {
PURR_EXPORT extern PurrManagedViewportVector Platform_GetWindowFramebufferScale_ManagedFunctionPointer;
PURR_EXPORT extern PurrManagedViewportVector Platform_GetWindowPos_ManagedFunctionPointer;
PURR_EXPORT extern PurrManagedViewportVector Platform_GetWindowSize_ManagedFunctionPointer;
}
PURR_API PurrNativeViewportVector Get_GetWindowFramebufferScale_InteropPointer();
PURR_API PurrNativeViewportVector Get_GetWindowPos_InteropPointer();
PURR_API PurrNativeViewportVector Get_GetWindowSize_InteropPointer();
#endif
#endif
