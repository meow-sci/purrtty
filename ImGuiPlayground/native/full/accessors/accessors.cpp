// Include provenance: component-local .clangd selects the verified patched build
// compilation database. Standalone C++ target builds verify these actual headers.
#include "imgui.h"
#include "imgui_internal.h"
namespace ImStb {
#include "imstb_textedit.h"
}
#include "accessors.h"
#include "identity.h"
#include <limits.h>

static_assert(sizeof(PurrFieldInfo)==24 && alignof(PurrFieldInfo)==4, "field DTO");
static_assert(sizeof(PurrTriple)==12 && alignof(PurrTriple)==4, "triple DTO");
static_assert(sizeof(PurrSharedSnapshot)==20 && offsetof(PurrSharedSnapshot,initial_flags)==16, "shared DTO");
static_assert(sizeof(PurrTextEditSnapshot)==16 && sizeof(PurrCellSnapshot)==8, "snapshot DTO");
static_assert(sizeof(ImGuiStyleVarInfo)==4 && sizeof(ImFontAtlasRectEntry)==4, "native strides");

const char* purr_accessors_identity(void) { return PURR_ACCESSOR_IDENTITY; }
int32_t purr_accessor_field_info(int32_t field, PurrFieldInfo* out)
{
    if (!out) return PURR_NULL;
    switch (field) {
#define PURR_INFO
#include "fields.inc"
#undef PURR_INFO
    default: return PURR_ID;
    }
}
int32_t purr_accessor_get(int32_t record, const void* object, int32_t field, int64_t* out)
{
    if (!object || !out) return PURR_NULL;
    switch (field) {
#define PURR_GET
#include "fields.inc"
#undef PURR_GET
    default: return PURR_ID;
    }
}
int32_t purr_accessor_set(int32_t record, void* object, int32_t field, int64_t value)
{
    if (!object) return PURR_NULL;
    switch (field) {
#define PURR_SET
#include "fields.inc"
#undef PURR_SET
    default: return PURR_ID;
    }
}
int32_t purr_accessor_style_count(void) { return ImGuiStyleVar_COUNT; }
int32_t purr_accessor_style(int32_t index, PurrTriple* out)
{
    if (!out) return PURR_NULL;
    if (index < 0 || index >= ImGuiStyleVar_COUNT) return PURR_RANGE;
    const ImGuiStyleVarInfo* info = ImGui::GetStyleVarInfo(index);
    *out = {(int32_t)info->Count, (int32_t)info->DataType, (int32_t)info->Offset};
    return PURR_OK;
}
int32_t purr_accessor_rect_count(const void* vector, int32_t* out)
{
    if (!vector || !out) return PURR_NULL;
    *out = ((const ImVector<ImFontAtlasRectEntry>*)vector)->Size;
    return PURR_OK;
}
int32_t purr_accessor_rect(const void* vector, int32_t index, PurrTriple* out)
{
    if (!vector || !out) return PURR_NULL;
    const auto& v = *(const ImVector<ImFontAtlasRectEntry>*)vector;
    if (index < 0 || index >= v.Size) return PURR_RANGE;
    const ImFontAtlasRectEntry& e = v[index];
    *out = {e.TargetIndex, e.Generation, (int32_t)e.IsUsed};
    return PURR_OK;
}
int32_t purr_accessor_rect_set(void* vector, int32_t index, int32_t field, int64_t value)
{
    if (!vector) return PURR_NULL;
    auto& v = *(ImVector<ImFontAtlasRectEntry>*)vector;
    if (index < 0 || index >= v.Size) return PURR_RANGE;
    // Only fields of this record are admitted; purr_accessor_set repeats the check.
    if (field < 44 || field > 46) return PURR_ID;
    return purr_accessor_set(11, &v[index], field, value);
}
int32_t purr_accessor_empty_string(uint8_t* out)
{
    if (!out) return PURR_NULL;
    *out = (uint8_t)ImGuiTextBuffer::EmptyString[0];
    return PURR_OK;
}
int32_t purr_accessor_text_copy(const void* buffer, uint8_t* out, int32_t capacity, int32_t* required)
{
    if (!buffer || !required) return PURR_NULL;
    if (capacity < 0) return PURR_RANGE;
    const ImGuiTextBuffer& b = *(const ImGuiTextBuffer*)buffer;
    *required = b.size();
    if (capacity < b.size()) return PURR_BUFFER;
    if (b.size() && !out) return PURR_NULL;
    if (b.size()) memcpy(out, b.c_str(), (size_t)b.size());
    return PURR_OK;
}
int32_t purr_accessor_text_append(void* buffer, const uint8_t* utf8, int32_t length)
{
    if (!buffer || (!utf8 && length)) return PURR_NULL;
    auto& b = *(ImGuiTextBuffer*)buffer;
    if (length < 0 || length > INT_MAX - b.Buf.Size - 1) return PURR_RANGE;
    if (length) b.append((const char*)utf8, (const char*)utf8 + length);
    return PURR_OK;
}
int32_t purr_accessor_text_clear(void* buffer)
{
    if (!buffer) return PURR_NULL;
    ((ImGuiTextBuffer*)buffer)->clear();
    return PURR_OK;
}
int32_t purr_accessor_named_key(void* bits, int32_t key, int32_t write, int32_t* value)
{
    if (!bits || !value) return PURR_NULL;
    if (key < ImGuiKey_NamedKey_BEGIN || key >= ImGuiKey_NamedKey_END || write < 0 || write > 1) return PURR_RANGE;
    auto& b = *(ImBitArrayForNamedKeys*)bits;
    if (write) {
        if (*value < 0 || *value > 1) return PURR_RANGE;
        if (*value) b.SetBit(key); else b.ClearBit(key);
    }
    *value = b.TestBit(key) ? 1 : 0;
    return PURR_OK;
}

template<class T> static int PurrChunkCount(const void* object)
{
    auto& stream = *(ImChunkStream<T>*)object;
    int count = 0;
    if (!stream.empty()) for(T* p=stream.begin(); p; p=stream.next_chunk(p)) ++count;
    return count;
}
template<class T> static void* PurrChunkAt(void* object, int index)
{
    auto& stream = *(ImChunkStream<T>*)object;
    T* p=stream.empty() ? NULL : stream.begin();
    while (p && index--) p=stream.next_chunk(p);
    return p;
}
template<class T> static int PurrSpanSize(const void* object)
{
    auto& span=*(const ImSpan<T>*)object;
    // Avoid subtraction of two null pointers for the legitimate default span.
    return span.Data ? span.size() : 0;
}
int32_t purr_accessor_count(int32_t shape, const void* object, int32_t* out)
{
    if (!object || !out) return PURR_NULL;
    switch (shape) {
    case 1: *out=PurrChunkCount<ImGuiTableSettings>(object); break;
    case 2: *out=PurrChunkCount<ImGuiWindowSettings>(object); break;
    case 5: *out=((const ImPool<ImGuiMultiSelectState>*)object)->GetMapSize(); break;
    case 6: *out=((const ImPool<ImGuiTabBar>*)object)->GetMapSize(); break;
    case 7: *out=((const ImPool<ImGuiTable>*)object)->GetMapSize(); break;
    case 8: *out=PurrSpanSize<ImGuiTableCellData>(object); break;
    case 9: *out=PurrSpanSize<ImGuiTableColumn>(object); break;
    case 10: *out=PurrSpanSize<short>(object); break;
    case 11: *out=((const ImStableVector<ImFontBaked,32>*)object)->Size; break;
    default: return PURR_ID;
    }
    return PURR_OK;
}
int32_t purr_accessor_at(int32_t shape, void* object, int32_t index, void** out)
{
    if (!object || !out) return PURR_NULL;
    int32_t count=0;
    int32_t status=purr_accessor_count(shape,object,&count);
    if (status) return status;
    if (index<0 || index>=count) return PURR_RANGE;
    switch (shape) {
    case 1: *out=PurrChunkAt<ImGuiTableSettings>(object,index); break;
    case 2: *out=PurrChunkAt<ImGuiWindowSettings>(object,index); break;
    case 5: *out=((ImPool<ImGuiMultiSelectState>*)object)->TryGetMapData(index); break;
    case 6: *out=((ImPool<ImGuiTabBar>*)object)->TryGetMapData(index); break;
    case 7: *out=((ImPool<ImGuiTable>*)object)->TryGetMapData(index); break;
    case 8: *out=&(*(ImSpan<ImGuiTableCellData>*)object)[index]; break;
    case 9: *out=&(*(ImSpan<ImGuiTableColumn>*)object)[index]; break;
    case 10: *out=&(*(ImSpan<short>*)object)[index]; break;
    case 11: *out=&(*(ImStableVector<ImFontBaked,32>*)object)[index]; break;
    default: return PURR_ID;
    }
    return PURR_OK;
}
int32_t purr_accessor_lookup(int32_t shape, void* object, uint32_t key, void** out)
{
    if (!object || !out) return PURR_NULL;
    switch(shape) {
    case 5: *out=((ImPool<ImGuiMultiSelectState>*)object)->GetByKey(key); break;
    case 6: *out=((ImPool<ImGuiTabBar>*)object)->GetByKey(key); break;
    case 7: *out=((ImPool<ImGuiTable>*)object)->GetByKey(key); break;
    default: return PURR_ID;
    }
    return PURR_OK;
}
int32_t purr_accessor_pool_alive(int32_t shape, const void* object, int32_t* out)
{
    if (!object || !out) return PURR_NULL;
    switch(shape) {
    case 5: *out=((const ImPool<ImGuiMultiSelectState>*)object)->GetAliveCount(); break;
    case 6: *out=((const ImPool<ImGuiTabBar>*)object)->GetAliveCount(); break;
    case 7: *out=((const ImPool<ImGuiTable>*)object)->GetAliveCount(); break;
    default: return PURR_ID;
    }
    return PURR_OK;
}
int32_t purr_accessor_short(const void* span, int32_t index, int32_t* out)
{
    if (!span || !out) return PURR_NULL;
    if (index<0 || index>=PurrSpanSize<short>(span)) return PURR_RANGE;
    *out=(*(const ImSpan<short>*)span)[index];
    return PURR_OK;
}
int32_t purr_accessor_cell(const void* span, int32_t index, PurrCellSnapshot* out)
{
    if (!span || !out) return PURR_NULL;
    if (index<0 || index>=PurrSpanSize<ImGuiTableCellData>(span)) return PURR_RANGE;
    const auto& cell=(*(const ImSpan<ImGuiTableCellData>*)span)[index];
    *out={cell.BgColor, cell.Column};
    return PURR_OK;
}
int32_t purr_accessor_shared(const void* object, PurrSharedSnapshot* out)
{
    if (!object || !out) return PURR_NULL;
    const auto& s=*(const ImDrawListSharedData*)object;
    *out={s.FontSize,s.FontScale,s.CurveTessellationTol,s.CircleSegmentMaxError,(uint32_t)s.InitialFlags};
    return PURR_OK;
}
int32_t purr_accessor_textedit(const void* object, PurrTextEditSnapshot* out)
{
    if (!object || !out) return PURR_NULL;
    const auto& s=*(const ImStbTexteditState*)object;
    *out={s.cursor,s.select_start,s.select_end,(int32_t)s.insert_mode};
    return PURR_OK;
}
int32_t purr_accessor_file_size(void* file, uint64_t* out)
{
    if (!file || !out) return PURR_NULL;
    const ImU64 size=ImFileGetSize((ImFileHandle)file);
    if (size==(ImU64)-1) return PURR_IO;
    *out=size;
    return PURR_OK;
}
