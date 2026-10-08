// Owned C ABI shapes and narrow pinned-compiler FFI conversions.
// All core/adapter/probe TUs MUST use -fno-strict-aliasing in this profile.
#ifndef PURR_FULL_API_ABI_H
#define PURR_FULL_API_ABI_H
#include <stdint.h>
#include <stddef.h>

#if defined(_WIN32)
#define PURR_EXPORT __declspec(dllexport)
#else
#define PURR_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
#define PURR_API extern "C" PURR_EXPORT
#else
#define PURR_API PURR_EXPORT
#endif

typedef struct PurrVec2 { float x, y; } PurrVec2;
typedef struct PurrVec3 { float x, y, z; } PurrVec3;
typedef struct PurrVec4 { float x, y, z, w; } PurrVec4;
typedef struct PurrRect { PurrVec2 Min, Max; } PurrRect;
typedef struct PurrInt2 { int32_t x, y; } PurrInt2;
typedef struct PurrInt3 { int32_t x, y, z; } PurrInt3;
typedef struct PurrInt4 { int32_t x, y, z, w; } PurrInt4;
typedef struct PurrTextureRef { void* TexData; intptr_t TexID; } PurrTextureRef;
typedef struct PurrClipperRange {
    int32_t Min, Max;
    uint8_t PosToIndexConvert;
    int8_t PosToIndexOffsetMin, PosToIndexOffsetMax;
} PurrClipperRange;

#ifdef __cplusplus
#include <string.h>
#include "imgui.h"
#include "imgui_internal.h"

namespace purr {
template<class To, class From> inline To bits(const From& value) {
    static_assert(sizeof(To) == sizeof(From), "bit-pattern width mismatch");
    static_assert(__is_trivially_copyable(To) && __is_trivially_copyable(From), "bit-pattern type mismatch");
    To out;
    memcpy(&out, &value, sizeof(out));
    return out;
}
inline ImVec2 to_native(PurrVec2 p) { return ImVec2(p.x, p.y); }
inline ImVec4 to_native(PurrVec4 p) { return ImVec4(p.x, p.y, p.z, p.w); }
inline ImRect to_native(PurrRect p) { return ImRect(to_native(p.Min), to_native(p.Max)); }
inline ImTextureRef to_native(PurrTextureRef p) {
    ImTextureRef out;
    out._TexData = static_cast<ImTextureData*>(p.TexData);
    out._TexID = bits<ImTextureID>(p.TexID);
    return out;
}
inline PurrVec2 from_native(const ImVec2& p) { return {p.x, p.y}; }
inline PurrVec4 from_native(const ImVec4& p) { return {p.x, p.y, p.z, p.w}; }
inline PurrRect from_native(const ImRect& p) { return {from_native(p.Min), from_native(p.Max)}; }
inline PurrTextureRef from_native(const ImTextureRef& p) { return {p._TexData, bits<intptr_t>(p._TexID)}; }
inline PurrClipperRange from_native(const ImGuiListClipperRange& p) {
    return {p.Min, p.Max, uint8_t(p.PosToIndexConvert ? 1 : 0), p.PosToIndexOffsetMin, p.PosToIndexOffsetMax};
}

// Pinned-compiler FFI representation contract, NOT a general C++ lifetime rule.
// Borrowed Bool8 cells retain their address, including callback/UserData aliases.
// Normalize noncanonical entry bytes in place only for input/inout parameters.
// Callers and callbacks MUST use canonical 0/1 writes while native bool execution
// shares the cell. Global allocator callbacks also make endpoints reentrant.
static_assert(sizeof(bool) == sizeof(uint8_t) && alignof(bool) == alignof(uint8_t), "Bool8 storage shape");
static_assert(__builtin_bit_cast(uint8_t, false) == 0 && __builtin_bit_cast(uint8_t, true) == 1, "selected compiler bool representations");
inline bool* bool_inout(uint8_t* p) {
    if (p) *p = *p != 0 ? 1 : 0;
    return reinterpret_cast<bool*>(p);
}
// Do not initialize, normalize, or read output storage: native writes (including
// their alias ordering and optional/required null preconditions) are authoritative.
inline bool* bool_output(uint8_t* p) {
    return reinterpret_cast<bool*>(p);
}

#define PURR_SHAPE(P, N) \
    static_assert(sizeof(P) == sizeof(N) && alignof(P) == alignof(N), "FFI size/alignment mismatch"); \
    static_assert(__is_standard_layout(P) && __is_standard_layout(N), "FFI standard-layout required"); \
    static_assert(__is_trivially_copyable(P) && __is_trivially_copyable(N), "FFI trivial-copy required")
#define PURR_OFFSET(P, PF, N, NF) static_assert(offsetof(P, PF) == offsetof(N, NF), "FFI field offset mismatch")
PURR_SHAPE(uint8_t, bool);
PURR_SHAPE(PurrVec2, ImVec2);
PURR_OFFSET(PurrVec2, x, ImVec2, x);
PURR_OFFSET(PurrVec2, y, ImVec2, y);
PURR_SHAPE(PurrVec4, ImVec4);
PURR_OFFSET(PurrVec4, x, ImVec4, x);
PURR_OFFSET(PurrVec4, y, ImVec4, y);
PURR_OFFSET(PurrVec4, z, ImVec4, z);
PURR_OFFSET(PurrVec4, w, ImVec4, w);
PURR_SHAPE(PurrRect, ImRect);
PURR_OFFSET(PurrRect, Min, ImRect, Min);
PURR_OFFSET(PurrRect, Max, ImRect, Max);
PURR_SHAPE(PurrTextureRef, ImTextureRef);
PURR_OFFSET(PurrTextureRef, TexData, ImTextureRef, _TexData);
PURR_OFFSET(PurrTextureRef, TexID, ImTextureRef, _TexID);
PURR_SHAPE(PurrClipperRange, ImGuiListClipperRange);
PURR_OFFSET(PurrClipperRange, Min, ImGuiListClipperRange, Min);
PURR_OFFSET(PurrClipperRange, Max, ImGuiListClipperRange, Max);
PURR_OFFSET(PurrClipperRange, PosToIndexConvert, ImGuiListClipperRange, PosToIndexConvert);
PURR_OFFSET(PurrClipperRange, PosToIndexOffsetMin, ImGuiListClipperRange, PosToIndexOffsetMin);
PURR_OFFSET(PurrClipperRange, PosToIndexOffsetMax, ImGuiListClipperRange, PosToIndexOffsetMax);
// Scalar-array views used by the finite Drag/Input/Slider/Color signatures.
#define PURR_SCALAR_ARRAY(P, S, C) \
    static_assert(sizeof(P) == sizeof(S)*(C) && alignof(P) == alignof(S), "scalar-array representation mismatch"); \
    static_assert(__is_standard_layout(P) && __is_trivially_copyable(P), "scalar-array POD required")
PURR_SCALAR_ARRAY(PurrVec2, float, 2);
PURR_SCALAR_ARRAY(PurrVec3, float, 3);
PURR_SCALAR_ARRAY(PurrVec4, float, 4);
PURR_SCALAR_ARRAY(PurrInt2, int32_t, 2);
PURR_SCALAR_ARRAY(PurrInt3, int32_t, 3);
PURR_SCALAR_ARRAY(PurrInt4, int32_t, 4);
static_assert(offsetof(PurrVec3, x)==0 && offsetof(PurrVec3, y)==4 && offsetof(PurrVec3, z)==8, "float3 offsets");
static_assert(offsetof(PurrInt2, x)==0 && offsetof(PurrInt2, y)==4, "int2 offsets");
static_assert(offsetof(PurrInt3, x)==0 && offsetof(PurrInt3, y)==4 && offsetof(PurrInt3, z)==8, "int3 offsets");
static_assert(offsetof(PurrInt4, x)==0 && offsetof(PurrInt4, y)==4 && offsetof(PurrInt4, z)==8 && offsetof(PurrInt4, w)==12, "int4 offsets");
static_assert(sizeof(void*) == 8 && sizeof(int) == 4 && sizeof(bool) == 1, "only pinned 64-bit ABI supported");
static_assert(sizeof(ImWchar) == 4 && sizeof(ImDrawIdx) == 2 && sizeof(ImTextureID) == 8, "configuration mismatch");
#undef PURR_SCALAR_ARRAY
#undef PURR_SHAPE
#undef PURR_OFFSET
} // namespace purr
#endif
#endif
