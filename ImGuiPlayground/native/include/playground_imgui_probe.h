#pragma once

#include <stddef.h>
#include <stdint.h>

#if defined(PLAYGROUND_IMGUI_WINDOWS)
#define PLAYGROUND_IMGUI_API __declspec(dllexport)
#else
#define PLAYGROUND_IMGUI_API __attribute__((visibility("default")))
#endif

extern "C" {

// Probe protocol: UTF-8 JSON returned by playground_imgui_probe_manifest_utf8().
// Top-level schema is {schema, schemaVersion, upstream, targetRid, configuration,
// scalars, types, bitFieldsNotIndividuallyAddressable}. Each type has name,
// sizeBytes, alignmentBytes, and fields; each measured field has name,
// offsetBytes, sizeBytes, and alignmentBytes. Values are emitted from this
// translation unit's sizeof/alignof/offsetof expressions, never managed declarations.
PLAYGROUND_IMGUI_API const char* playground_imgui_probe_manifest_utf8(void);
PLAYGROUND_IMGUI_API const char* playground_imgui_probe_upstream_commit(void);
PLAYGROUND_IMGUI_API const char* playground_imgui_probe_target_rid(void);

// ABI conversion exercises: all forward into actual upstream ImGui functions or
// ImRect methods. POD definitions are mirrored in the JSON ABI manifest.
typedef struct PlaygroundImVec2 { float x, y; } PlaygroundImVec2;
typedef struct PlaygroundImVec4 { float x, y, z, w; } PlaygroundImVec4;
typedef struct PlaygroundFloatRect { PlaygroundImVec2 Min, Max; } PlaygroundFloatRect;
PLAYGROUND_IMGUI_API void playground_imgui_abi_set_next_window_pos(PlaygroundImVec2 pos, int32_t condition);
PLAYGROUND_IMGUI_API PlaygroundImVec2 playground_imgui_abi_get_window_pos(void);
PLAYGROUND_IMGUI_API void playground_imgui_abi_push_style_color(int32_t color_index, PlaygroundImVec4 color);
PLAYGROUND_IMGUI_API PlaygroundImVec4 playground_imgui_abi_color_convert_u32(uint32_t color);
PLAYGROUND_IMGUI_API uint8_t playground_imgui_abi_rect_contains(PlaygroundFloatRect rect, PlaygroundImVec2 point);
PLAYGROUND_IMGUI_API PlaygroundImVec4 playground_imgui_abi_rect_to_vec4(PlaygroundFloatRect rect);

// The BRUTAL binding resolves three writable callback slots and three getters.
// Callback signatures return float2 by value; trampoline wrappers convert that
// result into upstream ImVec2 using the target C++ ABI.
typedef PlaygroundImVec2 (*PlaygroundManagedViewportVectorFn)(void* viewport);
PLAYGROUND_IMGUI_API extern PlaygroundManagedViewportVectorFn Platform_GetWindowPos_ManagedFunctionPointer;
PLAYGROUND_IMGUI_API extern PlaygroundManagedViewportVectorFn Platform_GetWindowSize_ManagedFunctionPointer;
PLAYGROUND_IMGUI_API extern PlaygroundManagedViewportVectorFn Platform_GetWindowFramebufferScale_ManagedFunctionPointer;
PLAYGROUND_IMGUI_API void* Get_GetWindowPos_InteropPointer(void);
PLAYGROUND_IMGUI_API void* Get_GetWindowSize_InteropPointer(void);
PLAYGROUND_IMGUI_API void* Get_GetWindowFramebufferScale_InteropPointer(void);
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_pos(void* viewport, PlaygroundImVec2* result);
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_size(void* viewport, PlaygroundImVec2* result);
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_framebuffer_scale(void* viewport, PlaygroundImVec2* result);

}
