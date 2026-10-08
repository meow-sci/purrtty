#include "../include/playground_imgui_probe.h"
#include "../../.tmp/native/source/031a18c417158427217bc5890e0ec0cb7e7b4b63/imgui.h"
#include "../../.tmp/native/source/031a18c417158427217bc5890e0ec0cb7e7b4b63/imgui_internal.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>

#ifndef PLAYGROUND_IMGUI_TARGET_RID
#define PLAYGROUND_IMGUI_TARGET_RID "unconfigured"
#endif

#define FIELD(type, member) {#member, offsetof(type, member), sizeof(((type*)0)->member), alignof(decltype(((type*)0)->member))}
#define TYPE(type, fields) {#type, sizeof(type), alignof(type), fields, sizeof(fields) / sizeof((fields)[0])}

struct FieldProbe
{
    const char* name;
    size_t offset;
    size_t size;
    size_t alignment;
};

struct TypeProbe
{
    const char* name;
    size_t size;
    size_t alignment;
    const FieldProbe* fields;
    size_t field_count;
};

#define FIELDS(name, ...) static const FieldProbe name[] = {__VA_ARGS__}

FIELDS(kVec2Fields, FIELD(ImVec2, x), FIELD(ImVec2, y));
FIELDS(kVec4Fields, FIELD(ImVec4, x), FIELD(ImVec4, y), FIELD(ImVec4, z), FIELD(ImVec4, w));
FIELDS(kPlaygroundVec2Fields, FIELD(PlaygroundImVec2, x), FIELD(PlaygroundImVec2, y));
FIELDS(kPlaygroundVec4Fields, FIELD(PlaygroundImVec4, x), FIELD(PlaygroundImVec4, y), FIELD(PlaygroundImVec4, z), FIELD(PlaygroundImVec4, w));
FIELDS(kPlaygroundRectFields, FIELD(PlaygroundFloatRect, Min), FIELD(PlaygroundFloatRect, Max));
FIELDS(kImWcharVectorFields, FIELD(ImVector<ImWchar>, Size), FIELD(ImVector<ImWchar>, Capacity), FIELD(ImVector<ImWchar>, Data));
FIELDS(kDrawVertVectorFields, FIELD(ImVector<ImDrawVert>, Size), FIELD(ImVector<ImDrawVert>, Capacity), FIELD(ImVector<ImDrawVert>, Data));
FIELDS(kDrawIdxVectorFields, FIELD(ImVector<ImDrawIdx>, Size), FIELD(ImVector<ImDrawIdx>, Capacity), FIELD(ImVector<ImDrawIdx>, Data));
FIELDS(kDrawCmdVectorFields, FIELD(ImVector<ImDrawCmd>, Size), FIELD(ImVector<ImDrawCmd>, Capacity), FIELD(ImVector<ImDrawCmd>, Data));
FIELDS(kDrawListVectorFields, FIELD(ImVector<ImDrawList*>, Size), FIELD(ImVector<ImDrawList*>, Capacity), FIELD(ImVector<ImDrawList*>, Data));
FIELDS(kTextureVectorFields, FIELD(ImVector<ImTextureData*>, Size), FIELD(ImVector<ImTextureData*>, Capacity), FIELD(ImVector<ImTextureData*>, Data));
FIELDS(kDrawListFields, FIELD(ImDrawList, CmdBuffer), FIELD(ImDrawList, IdxBuffer), FIELD(ImDrawList, VtxBuffer));
FIELDS(kTextureRefFields, FIELD(ImTextureRef, _TexData), FIELD(ImTextureRef, _TexID));
FIELDS(kDrawVertFields, FIELD(ImDrawVert, pos), FIELD(ImDrawVert, uv), FIELD(ImDrawVert, col));
FIELDS(kDrawCmdFields, FIELD(ImDrawCmd, ClipRect), FIELD(ImDrawCmd, TexRef), FIELD(ImDrawCmd, VtxOffset), FIELD(ImDrawCmd, IdxOffset), FIELD(ImDrawCmd, ElemCount), FIELD(ImDrawCmd, UserCallback), FIELD(ImDrawCmd, UserCallbackData));
FIELDS(kDrawDataFields, FIELD(ImDrawData, Valid), FIELD(ImDrawData, CmdListsCount), FIELD(ImDrawData, TotalIdxCount), FIELD(ImDrawData, TotalVtxCount), FIELD(ImDrawData, CmdLists), FIELD(ImDrawData, DisplayPos), FIELD(ImDrawData, DisplaySize), FIELD(ImDrawData, FramebufferScale), FIELD(ImDrawData, OwnerViewport), FIELD(ImDrawData, Textures));
FIELDS(kTextureDataFields, FIELD(ImTextureData, UniqueID), FIELD(ImTextureData, Status), FIELD(ImTextureData, BackendUserData), FIELD(ImTextureData, TexID), FIELD(ImTextureData, Format), FIELD(ImTextureData, Width), FIELD(ImTextureData, Height), FIELD(ImTextureData, BytesPerPixel), FIELD(ImTextureData, Pixels), FIELD(ImTextureData, UsedRect), FIELD(ImTextureData, UpdateRect), FIELD(ImTextureData, Updates), FIELD(ImTextureData, UnusedFrames), FIELD(ImTextureData, RefCount), FIELD(ImTextureData, UseColors), FIELD(ImTextureData, WantDestroyNextFrame));
FIELDS(kFontConfigFields, FIELD(ImFontConfig, Name), FIELD(ImFontConfig, FontData), FIELD(ImFontConfig, FontDataSize), FIELD(ImFontConfig, FontDataOwnedByAtlas), FIELD(ImFontConfig, EllipsisChar), FIELD(ImFontConfig, GlyphRanges), FIELD(ImFontConfig, GlyphOffset), FIELD(ImFontConfig, FontLoaderFlags), FIELD(ImFontConfig, FontLoader), FIELD(ImFontConfig, FontLoaderData));
FIELDS(kFontBakedFields, FIELD(ImFontBaked, IndexAdvanceX), FIELD(ImFontBaked, FallbackAdvanceX), FIELD(ImFontBaked, Size), FIELD(ImFontBaked, RasterizerDensity), FIELD(ImFontBaked, IndexLookup), FIELD(ImFontBaked, Glyphs), FIELD(ImFontBaked, FallbackGlyphIndex), FIELD(ImFontBaked, Ascent), FIELD(ImFontBaked, LastUsedFrame), FIELD(ImFontBaked, BakedId), FIELD(ImFontBaked, ContainerFont), FIELD(ImFontBaked, FontLoaderDatas));
FIELDS(kFontGlyphFields, FIELD(ImFontGlyph, AdvanceX), FIELD(ImFontGlyph, X0), FIELD(ImFontGlyph, Y0), FIELD(ImFontGlyph, X1), FIELD(ImFontGlyph, Y1), FIELD(ImFontGlyph, U0), FIELD(ImFontGlyph, V0), FIELD(ImFontGlyph, U1), FIELD(ImFontGlyph, V1), FIELD(ImFontGlyph, PackId));
FIELDS(kFontFields, FIELD(ImFont, LastBaked), FIELD(ImFont, ContainerAtlas), FIELD(ImFont, Flags), FIELD(ImFont, CurrentRasterizerDensity), FIELD(ImFont, FontId), FIELD(ImFont, LegacySize), FIELD(ImFont, Sources), FIELD(ImFont, EllipsisChar), FIELD(ImFont, FallbackChar), FIELD(ImFont, Used8kPagesMap), FIELD(ImFont, EllipsisAutoBake), FIELD(ImFont, RemapPairs));
FIELDS(kFontAtlasFields, FIELD(ImFontAtlas, Flags), FIELD(ImFontAtlas, TexDesiredFormat), FIELD(ImFontAtlas, TexGlyphPadding), FIELD(ImFontAtlas, UserData), FIELD(ImFontAtlas, TexRef), FIELD(ImFontAtlas, TexData), FIELD(ImFontAtlas, TexList), FIELD(ImFontAtlas, TexUvScale), FIELD(ImFontAtlas, Fonts), FIELD(ImFontAtlas, Sources), FIELD(ImFontAtlas, Builder), FIELD(ImFontAtlas, FontLoader), FIELD(ImFontAtlas, RefCount), FIELD(ImFontAtlas, OwnerContext));
FIELDS(kIoFields, FIELD(ImGuiIO, ConfigFlags), FIELD(ImGuiIO, BackendFlags), FIELD(ImGuiIO, DisplaySize), FIELD(ImGuiIO, DisplayFramebufferScale), FIELD(ImGuiIO, DeltaTime), FIELD(ImGuiIO, Fonts), FIELD(ImGuiIO, FontDefault), FIELD(ImGuiIO, InputQueueCharacters), FIELD(ImGuiIO, IniFilename), FIELD(ImGuiIO, LogFilename), FIELD(ImGuiIO, ConfigMacOSXBehaviors));
FIELDS(kStyleFields, FIELD(ImGuiStyle, Alpha), FIELD(ImGuiStyle, FontSizeBase), FIELD(ImGuiStyle, WindowPadding), FIELD(ImGuiStyle, Colors));
FIELDS(kPlatformIoFields, FIELD(ImGuiPlatformIO, Platform_SetClipboardTextFn), FIELD(ImGuiPlatformIO, Renderer_TextureMaxWidth), FIELD(ImGuiPlatformIO, Renderer_TextureMaxHeight), FIELD(ImGuiPlatformIO, Platform_GetClipboardTextFn), FIELD(ImGuiPlatformIO, Platform_GetWindowPos), FIELD(ImGuiPlatformIO, Platform_GetWindowSize), FIELD(ImGuiPlatformIO, Platform_GetWindowFramebufferScale), FIELD(ImGuiPlatformIO, Monitors), FIELD(ImGuiPlatformIO, Textures), FIELD(ImGuiPlatformIO, Viewports));
FIELDS(kBoxSelectFields, FIELD(ImGuiBoxSelectState, ID), FIELD(ImGuiBoxSelectState, IsActive), FIELD(ImGuiBoxSelectState, IsStarting), FIELD(ImGuiBoxSelectState, StartPosRel), FIELD(ImGuiBoxSelectState, EndPosRel), FIELD(ImGuiBoxSelectState, Window), FIELD(ImGuiBoxSelectState, UnclipRect), FIELD(ImGuiBoxSelectState, BoxSelectRectPrev), FIELD(ImGuiBoxSelectState, BoxSelectRectCurr));
FIELDS(kDockNodeFields, FIELD(ImGuiDockNode, ID), FIELD(ImGuiDockNode, SharedFlags), FIELD(ImGuiDockNode, LocalFlags), FIELD(ImGuiDockNode, ParentNode), FIELD(ImGuiDockNode, ChildNodes), FIELD(ImGuiDockNode, Windows), FIELD(ImGuiDockNode, Pos), FIELD(ImGuiDockNode, Size), FIELD(ImGuiDockNode, WindowClass), FIELD(ImGuiDockNode, HostWindow), FIELD(ImGuiDockNode, SelectedTabId));
FIELDS(kStackLevelFields, FIELD(ImGuiStackLevelInfo, ID), FIELD(ImGuiStackLevelInfo, QueryFrameCount), FIELD(ImGuiStackLevelInfo, QuerySuccess), FIELD(ImGuiStackLevelInfo, Desc));
FIELDS(kContextFields, FIELD(ImGuiContext, Initialized), FIELD(ImGuiContext, IO), FIELD(ImGuiContext, PlatformIO), FIELD(ImGuiContext, Style), FIELD(ImGuiContext, ConfigFlagsCurrFrame), FIELD(ImGuiContext, FontAtlases), FIELD(ImGuiContext, Font), FIELD(ImGuiContext, FontBaked), FIELD(ImGuiContext, FrameCount), FIELD(ImGuiContext, CurrentWindow), FIELD(ImGuiContext, Windows), FIELD(ImGuiContext, Viewports), FIELD(ImGuiContext, DockContext));
FIELDS(kRectFields, FIELD(ImRect, Min), FIELD(ImRect, Max));

static const TypeProbe kTypes[] = {
    TYPE(ImVec2, kVec2Fields), TYPE(ImVec4, kVec4Fields), TYPE(PlaygroundImVec2, kPlaygroundVec2Fields),
    TYPE(PlaygroundImVec4, kPlaygroundVec4Fields), TYPE(PlaygroundFloatRect, kPlaygroundRectFields),
    TYPE(ImVector<ImWchar>, kImWcharVectorFields),
    TYPE(ImVector<ImDrawVert>, kDrawVertVectorFields), TYPE(ImVector<ImDrawIdx>, kDrawIdxVectorFields),
    TYPE(ImVector<ImDrawCmd>, kDrawCmdVectorFields), TYPE(ImVector<ImDrawList*>, kDrawListVectorFields),
    TYPE(ImVector<ImTextureData*>, kTextureVectorFields), TYPE(ImDrawList, kDrawListFields),
    TYPE(ImTextureRef, kTextureRefFields), TYPE(ImFontGlyph, kFontGlyphFields),
    TYPE(ImDrawVert, kDrawVertFields), TYPE(ImDrawCmd, kDrawCmdFields), TYPE(ImDrawData, kDrawDataFields),
    TYPE(ImTextureData, kTextureDataFields), TYPE(ImFontConfig, kFontConfigFields), TYPE(ImFontBaked, kFontBakedFields),
    TYPE(ImFont, kFontFields), TYPE(ImFontAtlas, kFontAtlasFields), TYPE(ImGuiIO, kIoFields),
    TYPE(ImGuiStyle, kStyleFields), TYPE(ImGuiPlatformIO, kPlatformIoFields), TYPE(ImRect, kRectFields),
    TYPE(ImGuiBoxSelectState, kBoxSelectFields), TYPE(ImGuiDockNode, kDockNodeFields),
    TYPE(ImGuiStackLevelInfo, kStackLevelFields), TYPE(ImGuiContext, kContextFields)
};

static bool Append(char* buffer, size_t capacity, size_t* length, const char* format, ...)
{
    if (*length >= capacity) return false;
    va_list args;
    va_start(args, format);
    int written = vsnprintf(buffer + *length, capacity - *length, format, args);
    va_end(args);
    if (written < 0 || (size_t)written >= capacity - *length) return false;
    *length += (size_t)written;
    return true;
}

static const char* BuildManifest()
{
    static char manifest[32768];
    static bool ready = false;
    if (ready) return manifest;
    size_t length = 0;
    if (!Append(manifest, sizeof(manifest), &length,
        "{\"schema\":\"playground_imgui.native-abi\",\"schemaVersion\":1,"
        "\"upstream\":{\"version\":\"%s\",\"sourceCommit\":\"031a18c417158427217bc5890e0ec0cb7e7b4b63\"},"
        "\"targetRid\":\"%s\",\"configuration\":{\"IMGUI_USE_WCHAR32\":true,"
        "\"IMGUI_DISABLE_OBSOLETE_FUNCTIONS\":true,\"ImDrawIdxBytes\":%zu},"
        "\"scalars\":{\"boolBytes\":%zu,\"ImWcharBytes\":%zu,\"ImDrawIdxBytes\":%zu,"
        "\"ImTextureIDBytes\":%zu,\"sizeTBytes\":%zu},\"types\":[",
        IMGUI_VERSION, PLAYGROUND_IMGUI_TARGET_RID, sizeof(ImDrawIdx), sizeof(bool), sizeof(ImWchar),
        sizeof(ImDrawIdx), sizeof(ImTextureID), sizeof(size_t))) return NULL;
    for (size_t i = 0; i < sizeof(kTypes) / sizeof(kTypes[0]); ++i)
    {
        const TypeProbe& type = kTypes[i];
        if (!Append(manifest, sizeof(manifest), &length,
            "%s{\"name\":\"%s\",\"sizeBytes\":%zu,\"alignmentBytes\":%zu,\"fields\":[",
            i == 0 ? "" : ",", type.name, type.size, type.alignment)) return NULL;
        for (size_t field = 0; field < type.field_count; ++field)
        {
            const FieldProbe& item = type.fields[field];
            if (!Append(manifest, sizeof(manifest), &length,
                "%s{\"name\":\"%s\",\"offsetBytes\":%zu,\"sizeBytes\":%zu,\"alignmentBytes\":%zu}",
                field == 0 ? "" : ",", item.name, item.offset, item.size, item.alignment)) return NULL;
        }
        if (!Append(manifest, sizeof(manifest), &length, "]}")) return NULL;
    }
    if (!Append(manifest, sizeof(manifest), &length,
        "],\"bitFieldsNotIndividuallyAddressable\":["
        "{\"type\":\"ImFontGlyph\",\"fields\":[\"Colored\",\"Visible\",\"SourceIdx\",\"Codepoint\"]},"
        "{\"type\":\"ImFontBaked\",\"fields\":[\"MetricsTotalSurface\",\"WantDestroy\",\"LoadNoFallback\",\"LoadNoRenderOnLayout\"]},"
        "{\"type\":\"ImGuiBoxSelectState\",\"fields\":[\"KeyMods\"]},"
        "{\"type\":\"ImGuiDockNode\",\"fields\":[\"AuthorityForPos\",\"AuthorityForSize\",\"AuthorityForViewport\",\"IsVisible\",\"IsFocused\",\"IsBgDrawnThisFrame\",\"HasCloseButton\",\"HasWindowMenuButton\",\"HasCentralNodeChild\",\"WantCloseAll\",\"WantLockSizeOnce\",\"WantMouseMove\",\"WantHiddenTabBarUpdate\",\"WantHiddenTabBarToggle\"]},"
        "{\"type\":\"ImGuiStackLevelInfo\",\"fields\":[\"DataType\"]}]}")) return NULL;
    ready = true;
    return manifest;
}

static ImVec2 ToImVec2(PlaygroundImVec2 value) { return ImVec2(value.x, value.y); }
static ImVec4 ToImVec4(PlaygroundImVec4 value) { return ImVec4(value.x, value.y, value.z, value.w); }
static PlaygroundImVec2 FromImVec2(ImVec2 value) { return {value.x, value.y}; }
static PlaygroundImVec4 FromImVec4(ImVec4 value) { return {value.x, value.y, value.z, value.w}; }
static ImRect ToImRect(PlaygroundFloatRect value)
{
    return ImRect(ToImVec2(value.Min), ToImVec2(value.Max));
}

static ImVec2 TrampolineWindowPos(ImGuiViewport* viewport)
{
    PlaygroundManagedViewportVectorFn callback = Platform_GetWindowPos_ManagedFunctionPointer;
    if (!callback) return ImVec2(0.0f, 0.0f);
    return ToImVec2(callback(viewport));
}
static ImVec2 TrampolineWindowSize(ImGuiViewport* viewport)
{
    PlaygroundManagedViewportVectorFn callback = Platform_GetWindowSize_ManagedFunctionPointer;
    if (!callback) return ImVec2(0.0f, 0.0f);
    return ToImVec2(callback(viewport));
}
static ImVec2 TrampolineFramebufferScale(ImGuiViewport* viewport)
{
    PlaygroundManagedViewportVectorFn callback = Platform_GetWindowFramebufferScale_ManagedFunctionPointer;
    if (!callback) return ImVec2(0.0f, 0.0f);
    return ToImVec2(callback(viewport));
}

typedef ImVec2 (*NativeViewportVectorFn)(ImGuiViewport* viewport);
static uint8_t InvokeVector(NativeViewportVectorFn callback, PlaygroundManagedViewportVectorFn managed_callback, void* viewport, PlaygroundImVec2* result)
{
    if (!callback || !managed_callback || !result) return 0;
    *result = FromImVec2(callback((ImGuiViewport*)viewport));
    return 1;
}

extern "C" {

PLAYGROUND_IMGUI_API PlaygroundManagedViewportVectorFn Platform_GetWindowPos_ManagedFunctionPointer = nullptr;
PLAYGROUND_IMGUI_API PlaygroundManagedViewportVectorFn Platform_GetWindowSize_ManagedFunctionPointer = nullptr;
PLAYGROUND_IMGUI_API PlaygroundManagedViewportVectorFn Platform_GetWindowFramebufferScale_ManagedFunctionPointer = nullptr;

PLAYGROUND_IMGUI_API const char* playground_imgui_probe_manifest_utf8(void) { return BuildManifest(); }
PLAYGROUND_IMGUI_API const char* playground_imgui_probe_upstream_commit(void) { return "031a18c417158427217bc5890e0ec0cb7e7b4b63"; }
PLAYGROUND_IMGUI_API const char* playground_imgui_probe_target_rid(void) { return PLAYGROUND_IMGUI_TARGET_RID; }

PLAYGROUND_IMGUI_API ImGuiContext* CreateContext(ImFontAtlas* shared_font_atlas) { return ImGui::CreateContext(shared_font_atlas); }
PLAYGROUND_IMGUI_API void DestroyContext(ImGuiContext* context) { ImGui::DestroyContext(context); }
PLAYGROUND_IMGUI_API ImGuiContext* GetCurrentContext(void) { return ImGui::GetCurrentContext(); }
PLAYGROUND_IMGUI_API const char* GetVersion(void) { return ImGui::GetVersion(); }
PLAYGROUND_IMGUI_API uint8_t DebugCheckVersionAndDataLayout(const char* version, size_t io_size, size_t style_size, size_t vec2_size, size_t vec4_size, size_t draw_vert_size, size_t draw_idx_size)
{
    return ImGui::DebugCheckVersionAndDataLayout(version, io_size, style_size, vec2_size, vec4_size, draw_vert_size, draw_idx_size) ? 1 : 0;
}
PLAYGROUND_IMGUI_API ImGuiIO* GetIO(void) { return &ImGui::GetIO(); }
PLAYGROUND_IMGUI_API ImGuiPlatformIO* GetPlatformIO(void) { return &ImGui::GetPlatformIO(); }
PLAYGROUND_IMGUI_API ImGuiStyle* GetStyle(void) { return &ImGui::GetStyle(); }
PLAYGROUND_IMGUI_API void NewFrame(void) { ImGui::NewFrame(); }
PLAYGROUND_IMGUI_API void Render(void) { ImGui::Render(); }
PLAYGROUND_IMGUI_API ImDrawData* GetDrawData(void) { return ImGui::GetDrawData(); }
PLAYGROUND_IMGUI_API void StyleColorsDark(ImGuiStyle* destination) { ImGui::StyleColorsDark(destination); }
PLAYGROUND_IMGUI_API uint8_t Begin(const char* name, uint8_t* open, int flags)
{
    bool open_value = open && *open != 0;
    bool visible = ImGui::Begin(name, open ? &open_value : NULL, flags);
    if (open) *open = open_value ? 1 : 0;
    return visible ? 1 : 0;
}
PLAYGROUND_IMGUI_API void End(void) { ImGui::End(); }
PLAYGROUND_IMGUI_API void SetNextWindowPos(const PlaygroundImVec2* position, int condition, const PlaygroundImVec2* pivot)
{
    if (!position) return;
    const ImVec2 native_position = ToImVec2(*position);
    const ImVec2 native_pivot = pivot ? ToImVec2(*pivot) : ImVec2(0.0f, 0.0f);
    ImGui::SetNextWindowPos(native_position, condition, native_pivot);
}
PLAYGROUND_IMGUI_API void SetNextWindowSize(const PlaygroundImVec2* size, int condition)
{
    if (size) ImGui::SetNextWindowSize(ToImVec2(*size), condition);
}
PLAYGROUND_IMGUI_API void TextUnformatted(const char* text, const char* text_end) { ImGui::TextUnformatted(text, text_end); }
PLAYGROUND_IMGUI_API uint8_t Button(const char* label, const PlaygroundImVec2* size)
{
    const ImVec2 native_size = size ? ToImVec2(*size) : ImVec2(0.0f, 0.0f);
    return ImGui::Button(label, native_size) ? 1 : 0;
}
PLAYGROUND_IMGUI_API void PushFont(ImFont* font, float size) { ImGui::PushFont(font, size); }
PLAYGROUND_IMGUI_API void PopFont(void) { ImGui::PopFont(); }
PLAYGROUND_IMGUI_API ImFont* GetFont(void) { return ImGui::GetFont(); }
PLAYGROUND_IMGUI_API ImDrawList* GetBackgroundDrawList(ImGuiViewport* viewport) { return ImGui::GetBackgroundDrawList(viewport); }
PLAYGROUND_IMGUI_API ImVec2 GetWindowPos(void) { return ImGui::GetWindowPos(); }
PLAYGROUND_IMGUI_API ImVec2 GetWindowSize(void) { return ImGui::GetWindowSize(); }
PLAYGROUND_IMGUI_API const ImVec4* GetStyleColorVec4(int color_index) { return &ImGui::GetStyleColorVec4((ImGuiCol)color_index); }
PLAYGROUND_IMGUI_API ImVec4 ColorConvertU32ToFloat4(uint32_t color) { return ImGui::ColorConvertU32ToFloat4(color); }
PLAYGROUND_IMGUI_API void Image(ImTextureRef texture, const PlaygroundImVec2* size, const PlaygroundImVec2* uv0, const PlaygroundImVec2* uv1)
{
    if (!size) return;
    const ImVec2 native_size = ToImVec2(*size);
    const ImVec2 native_uv0 = uv0 ? ToImVec2(*uv0) : ImVec2(0.0f, 0.0f);
    const ImVec2 native_uv1 = uv1 ? ToImVec2(*uv1) : ImVec2(1.0f, 1.0f);
    ImGui::Image(texture, native_size, native_uv0, native_uv1);
}
PLAYGROUND_IMGUI_API void ShowDemoWindow(uint8_t* open)
{
    bool open_value = open && *open != 0;
    ImGui::ShowDemoWindow(open ? &open_value : NULL);
    if (open) *open = open_value ? 1 : 0;
}

PLAYGROUND_IMGUI_API void ImGuiIO_AddKeyEvent(ImGuiIO* target, int key, uint8_t down) { if (target) target->AddKeyEvent((ImGuiKey)key, down != 0); }
PLAYGROUND_IMGUI_API void ImGuiIO_AddMousePosEvent(ImGuiIO* target, float x, float y) { if (target) target->AddMousePosEvent(x, y); }
PLAYGROUND_IMGUI_API void ImGuiIO_AddMouseButtonEvent(ImGuiIO* target, int button, uint8_t down) { if (target) target->AddMouseButtonEvent(button, down != 0); }
PLAYGROUND_IMGUI_API void ImGuiIO_AddMouseWheelEvent(ImGuiIO* target, float wheel_x, float wheel_y) { if (target) target->AddMouseWheelEvent(wheel_x, wheel_y); }
PLAYGROUND_IMGUI_API void ImGuiIO_AddFocusEvent(ImGuiIO* target, uint8_t focused) { if (target) target->AddFocusEvent(focused != 0); }
PLAYGROUND_IMGUI_API void ImGuiIO_AddInputCharacter(ImGuiIO* target, uint32_t character) { if (target) target->AddInputCharacter(character); }
PLAYGROUND_IMGUI_API void ImGuiIO_SetKeyEventNativeData(ImGuiIO* target, int key, int keycode, int scancode, int legacy_index)
{
    if (target) target->SetKeyEventNativeData((ImGuiKey)key, keycode, scancode, legacy_index);
}

PLAYGROUND_IMGUI_API ImFont* ImFontAtlas_AddFontFromFileTTF(ImFontAtlas* target, const char* filename, float size, ImFontConfig* config, const ImWchar* glyph_ranges)
{
    return target ? target->AddFontFromFileTTF(filename, size, config, glyph_ranges) : NULL;
}
PLAYGROUND_IMGUI_API const char* ImFont_GetDebugName(ImFont* target) { return target ? target->GetDebugName() : "<null>"; }
PLAYGROUND_IMGUI_API void* ImTextureData_GetPixels(ImTextureData* target) { return target ? target->GetPixels() : NULL; }
PLAYGROUND_IMGUI_API void* ImTextureData_GetPixelsAt(ImTextureData* target, int x, int y) { return target ? target->GetPixelsAt(x, y) : NULL; }
PLAYGROUND_IMGUI_API ImTextureRef ImTextureData_GetTexRef(ImTextureData* target) { return target ? target->GetTexRef() : ImTextureRef(); }
PLAYGROUND_IMGUI_API void ImTextureData_SetTexID(ImTextureData* target, uintptr_t texture_id) { if (target) target->SetTexID((ImTextureID)texture_id); }
PLAYGROUND_IMGUI_API void ImTextureData_SetStatus(ImTextureData* target, int status) { if (target) target->SetStatus((ImTextureStatus)status); }
PLAYGROUND_IMGUI_API uintptr_t ImTextureRef_GetTexID(ImTextureRef* target) { return target ? (uintptr_t)target->GetTexID() : 0; }
PLAYGROUND_IMGUI_API uintptr_t ImDrawCmd_GetTexID(ImDrawCmd* target) { return target ? (uintptr_t)target->GetTexID() : 0; }
PLAYGROUND_IMGUI_API void* MemAlloc(size_t size) { return ImGui::MemAlloc(size); }
PLAYGROUND_IMGUI_API void MemFree(void* memory) { ImGui::MemFree(memory); }

PLAYGROUND_IMGUI_API void ImDrawList_AddRectFilled(ImDrawList* target, const PlaygroundImVec2* minimum, const PlaygroundImVec2* maximum, uint32_t color, float rounding, int flags)
{
    if (target && minimum && maximum)
        target->AddRectFilled(ToImVec2(*minimum), ToImVec2(*maximum), color, rounding, (ImDrawFlags)flags);
}
PLAYGROUND_IMGUI_API void ImDrawList_AddCallback(ImDrawList* target, void* callback, void* user_data, size_t user_data_size)
{
    if (target) target->AddCallback((ImDrawCallback)callback, user_data, user_data_size);
}
PLAYGROUND_IMGUI_API ImVec2 ImDrawList_GetClipRectMin(ImDrawList* target) { return target ? target->GetClipRectMin() : ImVec2(0.0f, 0.0f); }
PLAYGROUND_IMGUI_API ImVec2 ImDrawList_GetClipRectMax(ImDrawList* target) { return target ? target->GetClipRectMax() : ImVec2(0.0f, 0.0f); }
PLAYGROUND_IMGUI_API void ImDrawList_PushClipRect(ImDrawList* target, const PlaygroundImVec2* minimum, const PlaygroundImVec2* maximum, uint8_t intersect)
{
    if (target && minimum && maximum)
        target->PushClipRect(ToImVec2(*minimum), ToImVec2(*maximum), intersect != 0);
}
PLAYGROUND_IMGUI_API void ImDrawList_PopClipRect(ImDrawList* target)
{
    if (target) target->PopClipRect();
}

PLAYGROUND_IMGUI_API uint8_t ImRect_Contains_internal_0(const PlaygroundFloatRect* target, const PlaygroundImVec2* point)
{
    if (!target || !point) return 0;
    return ToImRect(*target).Contains(ToImVec2(*point)) ? 1 : 0;
}
PLAYGROUND_IMGUI_API PlaygroundImVec4 ImRect_ToVec4_internal(const PlaygroundFloatRect* target)
{
    return target ? FromImVec4(ToImRect(*target).ToVec4()) : PlaygroundImVec4{0.0f, 0.0f, 0.0f, 0.0f};
}

PLAYGROUND_IMGUI_API void playground_imgui_abi_set_next_window_pos(PlaygroundImVec2 position, int32_t condition)
{
    ImGui::SetNextWindowPos(ToImVec2(position), (ImGuiCond)condition);
}
PLAYGROUND_IMGUI_API PlaygroundImVec2 playground_imgui_abi_get_window_pos(void) { return FromImVec2(ImGui::GetWindowPos()); }
PLAYGROUND_IMGUI_API void playground_imgui_abi_push_style_color(int32_t index, PlaygroundImVec4 color)
{
    ImGui::PushStyleColor((ImGuiCol)index, ToImVec4(color));
}
PLAYGROUND_IMGUI_API PlaygroundImVec4 playground_imgui_abi_color_convert_u32(uint32_t color)
{
    return FromImVec4(ImGui::ColorConvertU32ToFloat4(color));
}
PLAYGROUND_IMGUI_API uint8_t playground_imgui_abi_rect_contains(PlaygroundFloatRect rect, PlaygroundImVec2 point)
{
    return ToImRect(rect).Contains(ToImVec2(point)) ? 1 : 0;
}
PLAYGROUND_IMGUI_API PlaygroundImVec4 playground_imgui_abi_rect_to_vec4(PlaygroundFloatRect rect)
{
    return FromImVec4(ToImRect(rect).ToVec4());
}

PLAYGROUND_IMGUI_API void* Get_GetWindowPos_InteropPointer(void) { return (void*)&TrampolineWindowPos; }
PLAYGROUND_IMGUI_API void* Get_GetWindowSize_InteropPointer(void) { return (void*)&TrampolineWindowSize; }
PLAYGROUND_IMGUI_API void* Get_GetWindowFramebufferScale_InteropPointer(void) { return (void*)&TrampolineFramebufferScale; }
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_pos(void* viewport, PlaygroundImVec2* result)
{
    return InvokeVector(&TrampolineWindowPos, Platform_GetWindowPos_ManagedFunctionPointer, viewport, result);
}
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_size(void* viewport, PlaygroundImVec2* result)
{
    return InvokeVector(&TrampolineWindowSize, Platform_GetWindowSize_ManagedFunctionPointer, viewport, result);
}
PLAYGROUND_IMGUI_API uint8_t playground_imgui_probe_invoke_window_framebuffer_scale(void* viewport, PlaygroundImVec2* result)
{
    return InvokeVector(&TrampolineFramebufferScale, Platform_GetWindowFramebufferScale_ManagedFunctionPointer, viewport, result);
}

} // extern "C"
