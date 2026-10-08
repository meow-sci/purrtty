// Compiler-only proof; never linked or exported into production.
#include "manual.h"
static_assert(__is_same(decltype(&TextV), decltype(&ImGui::TextV)), "actual target va_list call shape");
static_assert(__is_same(PurrNativeViewportVector, decltype(ImGuiPlatformIO::Platform_GetWindowPos)), "position native callback type");
static_assert(__is_same(PurrNativeViewportVector, decltype(ImGuiPlatformIO::Platform_GetWindowSize)), "size native callback type");
static_assert(__is_same(PurrNativeViewportVector, decltype(ImGuiPlatformIO::Platform_GetWindowFramebufferScale)), "scale native callback type");
static_assert(__is_same(decltype(Get_GetWindowPos_InteropPointer()), PurrNativeViewportVector), "typed getter");
static_assert(__is_same(decltype(Platform_GetWindowPos_ManagedFunctionPointer), PurrManagedViewportVector), "typed slot");
// Checked separately from managed runtime measurements: only these pinned target
// header typedefs have been selected. A new target must not silently use a guessed
// pointer adjustment. Linux's actual array element type stays compiler-owned.
#if defined(__APPLE__) && defined(__aarch64__)
static_assert(__is_same(va_list, char*), "Darwin arm64 va_list definition");
#elif defined(_WIN32) && defined(__x86_64__)
static_assert(__is_same(va_list, char*), "Win64 GNU va_list definition");
#elif defined(__linux__) && defined(__x86_64__)
static_assert(__is_array(va_list) && __array_extent(va_list, 0) == 1, "SysV va_list parameter adjustment");
#else
#error Unsupported manual ABI target
#endif
