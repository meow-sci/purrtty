// TEST ONLY: includes the real core TU once for independent const-table observation.
// The fixture link must OMIT separate imgui.cpp. No copied upstream definitions.
#ifndef PURR_COMBINED_CORE_INCLUDED
#include "imgui.cpp"
#endif
namespace ImStb {
#include "imstb_textedit.h"
}
#include "fixture.h"
#if defined(_WIN32)
#include <io.h>
#include <fcntl.h>
#else
#include <unistd.h>
#endif
static int PurrCreated=0, PurrDestroyed=0;

static ImFileHandle PurrNonseekableFile()
{
    int descriptors[2];
#if defined(_WIN32)
    if(_pipe(descriptors,4096,_O_BINARY)) return NULL;
    _close(descriptors[1]);
    FILE* file=_fdopen(descriptors[0],"rb");
    if(!file) _close(descriptors[0]);
#else
    if(pipe(descriptors)) return NULL;
    close(descriptors[1]);
    FILE* file=fdopen(descriptors[0],"rb");
    if(!file) close(descriptors[0]);
#endif
    return file;
}

static int PurrAssign(void* object, int field, int64_t value)
{
    switch(field) {
#define PURR_ASSIGN
#include "fixture_fields.inc"
#undef PURR_ASSIGN
    default: return PURR_ID;
    }
}
static int64_t PurrDirectRead(const void* object, int record, int field)
{
    int64_t result=0;
    int64_t* out=&result;
    // Use a lambda so the generated return status doesn't become the value.
    auto read = [&]() -> int {
        switch(field) {
#define PURR_DIRECT_GET
#include "fixture_fields.inc"
#undef PURR_DIRECT_GET
        default: return PURR_ID;
        }
    };
    read();
    return result;
}
struct PurrFieldFixture
{
    ImGuiContext* previous;
    ImGuiContext* fixture_context;
    void* objects;
    int field, record, stride;
    int64_t original[47][2];
    ImFont fixture_font; // Real non-owning ContainerFont target, alive past the baked records.
    PurrFieldFixture(int f): previous(ImGui::GetCurrentContext()), fixture_context(ImGui::CreateContext()), objects(NULL),field(f),record(0),stride(0)
    {
        PurrFieldInfo info;
        purr_accessor_field_info(f,&info);
        record=info.record;
        switch(record) {
#define PURR_CREATE
#include "fixture_fields.inc"
#undef PURR_CREATE
        }
        for(int index=0;index<2;index++) {
            // Meaningful ordinary neighbors, without poisoning pointer ownership or
            // memset on live objects. No engine algorithm consumes these fixtures.
            void* p=pointer(index);
            switch(record) {
            case 0: ((ImFontGlyph*)p)->AdvanceX=123.5f+index; ((ImFontGlyph*)p)->X0=9.25f; break;
            case 1: ((ImFontBaked*)p)->BakedId=0x12345678u+index; ((ImFontBaked*)p)->ContainerFont=&fixture_font; break;
            case 3: ((ImGuiBoxSelectState*)p)->StartPosRel=ImVec2(13.5f,17.25f); ((ImGuiBoxSelectState*)p)->RequestClear=true; break;
            case 4: ((ImGuiDockNode*)p)->RefViewportId=0xabcdefu+index; break;
            case 5: strcpy(((ImGuiStackLevelInfo*)p)->Desc,"ordinary-neighbor"); ((ImGuiStackLevelInfo*)p)->QuerySuccess=true; break;
            case 6: ((ImGuiContext*)p)->ActiveIdClickOffset=ImVec2(7.5f,9.25f); ((ImGuiContext*)p)->ActiveIdFromShortcut=true; break;
            case 7: ((ImGuiWindow*)p)->SetWindowPosVal=ImVec2(7.25f,13.5f); ((ImGuiWindow*)p)->DockOrder=123; break;
            case 8: ((ImGuiTableColumn*)p)->CannotSkipItemsQueue=0xa5; ((ImGuiTableColumn*)p)->SortDirectionsAvailList=0x24; break;
            case 9: ((ImGuiTable*)p)->RowIndentOffsetX=27.5f; ((ImGuiTable*)p)->RowBgColorCounter=123456; break;
            case 10: ((ImGuiTableColumnSettings*)p)->SortOrder=123; ((ImGuiTableColumnSettings*)p)->UserID=0xabcdefu+index; break;
            default: break; // Both stride4 records consist entirely of packed fields.
            }
        }
        for(int id=0;id<47;id++) {
            purr_accessor_field_info(id,&info);
            if(info.record!=record) continue;
            for(int index=0;index<2;index++) {
                original[id][index]=PurrDirectRead(pointer(index),record,id);
                // Nonzero neighbor fields AND adjacent record are observable. Values
                // are isolated representations; restored before any native cleanup.
                int64_t max=(INT64_C(1)<<(info.effective_bits-info.is_signed))-1;
                PurrAssign(pointer(index),id, index ? max : (id%2 ? 1 : 0));
            }
        }
        PurrCreated+=2;
    }
    void* pointer(int index)
    {
        if(index<0 || index>1) return NULL;
        switch(record) {
#define PURR_POINTER
#include "fixture_fields.inc"
#undef PURR_POINTER
        default:return NULL;
        }
    }
    ~PurrFieldFixture()
    {
        PurrFieldInfo info;
        for(int id=0;id<47;id++) {
            purr_accessor_field_info(id,&info);
            if(info.record==record) for(int i=0;i<2;i++) PurrAssign(pointer(i),id,original[id][i]);
        }
        switch(record) {
#define PURR_DESTROY
#include "fixture_fields.inc"
#undef PURR_DESTROY
        }
        ImGui::MemFree(objects);
        ImGui::SetCurrentContext(fixture_context);
        ImGui::DestroyContext(fixture_context);
        ImGui::SetCurrentContext(previous);
        PurrDestroyed+=2;
    }
};
void* purr_fixture_field_create(int32_t field)
{
    if(field<0 || field>=47) return NULL;
    return IM_NEW(PurrFieldFixture)(field);
}
void purr_fixture_field_destroy(void* fixture) { IM_DELETE((PurrFieldFixture*)fixture); }
void* purr_fixture_field_object(void* fixture,int32_t index) { return fixture ? ((PurrFieldFixture*)fixture)->pointer(index) : NULL; }
int32_t purr_fixture_field_stride(void* fixture) { return fixture ? ((PurrFieldFixture*)fixture)->stride : 0; }
int32_t purr_fixture_field_snapshot(void* fixture,uint8_t* out,int32_t capacity)
{
    if(!fixture || !out) return PURR_NULL;
    auto& f=*(PurrFieldFixture*)fixture;
    if(capacity<f.stride*2) return PURR_BUFFER;
    memcpy(out,f.objects,(size_t)f.stride*2);
    return PURR_OK;
}
int32_t purr_fixture_field_mask(void* fixture,uint8_t* out,int32_t capacity)
{
    if(!fixture || !out) return PURR_NULL;
    auto& f=*(PurrFieldFixture*)fixture;
    if(capacity<f.stride) return PURR_BUFFER;
    const int64_t before=PurrDirectRead(f.objects,f.record,f.field);
    PurrFieldInfo info;
    purr_accessor_field_info(f.field,&info);
    PurrAssign(f.objects,f.field,0);
    memcpy(out,f.objects,(size_t)f.stride);
    const int64_t all=info.is_signed ? -1 : (INT64_C(1)<<info.effective_bits)-1;
    PurrAssign(f.objects,f.field,all);
    for(int i=0;i<f.stride;i++) out[i]^=((const uint8_t*)f.objects)[i];
    PurrAssign(f.objects,f.field,before);
    return PURR_OK;
}
int32_t purr_fixture_field_assign(void* fixture,int64_t value)
{
    if(!fixture) return PURR_NULL;
    auto& f=*(PurrFieldFixture*)fixture;
    return PurrAssign(f.objects,f.field,value);
}
int64_t purr_fixture_field_read(void* fixture)
{
    auto& f=*(PurrFieldFixture*)fixture;
    return PurrDirectRead(f.objects,f.record,f.field);
}

struct PurrContainerFixture
{
    ImBitArrayForNamedKeys keys;
    ImChunkStream<ImGuiTableSettings> tables;
    ImChunkStream<ImGuiWindowSettings> windows;
    ImDrawListSharedData shared;
    ImFileHandle file;
    ImFileHandle nonseekable;
    ImPool<ImGuiMultiSelectState> multi;
    ImPool<ImGuiTabBar> tabs;
    ImPool<ImGuiTable> table_pool;
    ImGuiTableCellData cells[2];
    ImGuiTableColumn columns[2];
    short shorts[2];
    ImSpan<ImGuiTableCellData> cell_span;
    ImSpan<ImGuiTableColumn> column_span;
    ImSpan<short> short_span;
    ImStableVector<ImFontBaked,32> baked;
    ImStbTexteditState textedit;
    ImVector<ImFontAtlasRectEntry> rects;
    ImGuiTextBuffer text;
    template<class T> static void seed_pool(ImPool<T>& pool)
    {
        pool.GetOrAddByKey(10);
        pool.GetOrAddByKey(20);
        pool.GetOrAddByKey(30);
        pool.Remove(20,pool.GetByKey(20));
    }
    PurrContainerFixture(const char* path):file(ImFileOpen(path,"rb")),nonseekable(PurrNonseekableFile()),cell_span(cells,2),column_span(columns,2),short_span(shorts,2),textedit{}
    {
        if(file) fseek(file,2,SEEK_SET);
        for(int i=0;i<2;i++) {
            auto* t=tables.alloc_chunk(sizeof(ImGuiTableSettings)+(i+1)*sizeof(ImGuiTableColumnSettings));
            IM_PLACEMENT_NEW(t) ImGuiTableSettings();
            t->ID=100+i;
            t->ColumnsCount=t->ColumnsCountMax=(ImGuiTableColumnIdx)(i+1);
            for(int j=0;j<=i;j++) IM_PLACEMENT_NEW(t->GetColumnSettings()+j) ImGuiTableColumnSettings();
            auto* w=windows.alloc_chunk(sizeof(ImGuiWindowSettings)+16+i*8);
            IM_PLACEMENT_NEW(w) ImGuiWindowSettings();
            w->ID=200+i;
            strcpy(w->GetName(),i ? "second" : "first");
        }
        seed_pool(multi); seed_pool(tabs); seed_pool(table_pool);
        cells[0].BgColor=0x12345678; cells[0].Column=3;
        cells[1].BgColor=0xfedcba98; cells[1].Column=-1;
        columns[0].SortDirection=1; columns[1].SortDirection=2;
        shorts[0]=-123; shorts[1]=321;
        // Native stable vector deliberately doesn't construct/destruct elements.
        // Construct every actually live slot and explicitly destruct it below.
        baked.resize(33);
        for(int i=0;i<baked.Size;i++) { IM_PLACEMENT_NEW(&baked[i]) ImFontBaked(); baked[i].MetricsTotalSurface=100+i; }
        shared.FontSize=17.5f; shared.FontScale=1.25f;
        shared.CurveTessellationTol=0.75f; shared.CircleSegmentMaxError=0.25f; shared.InitialFlags=ImDrawListFlags_AntiAliasedLines;
        textedit.cursor=7; textedit.select_start=2; textedit.select_end=5; textedit.insert_mode=1;
        rects.resize(4);
        for(int i=0;i<4;i++) IM_PLACEMENT_NEW(&rects[i]) ImFontAtlasRectEntry();
        // Two logical adjacent records, with live trivial tail records retained in
        // capacity so disposable wrong-CLR7-stride mutants fail by data, not OOB.
        rects.resize(2);
        rects[0].TargetIndex=19; rects[0].Generation=3; rects[0].IsUsed=1;
        rects[1].TargetIndex=-1; rects[1].Generation=17; rects[1].IsUsed=0;
        PurrCreated++;
    }
    ~PurrContainerFixture()
    {
        for(int i=0;i<baked.Size;i++) baked[i].~ImFontBaked();
        if(file) ImFileClose(file);
        if(nonseekable) ImFileClose(nonseekable);
        PurrDestroyed++;
    }
};
void* purr_fixture_containers_create(const char* file_path) { return file_path ? IM_NEW(PurrContainerFixture)(file_path) : NULL; }
void purr_fixture_containers_destroy(void* fixture) { IM_DELETE((PurrContainerFixture*)fixture); }
void* purr_fixture_container(void* fixture,int32_t shape)
{
    if(!fixture) return NULL;
    auto& f=*(PurrContainerFixture*)fixture;
    switch(shape) {
    case 0:return &f.keys;
    case 1:return &f.tables;
    case 2:return &f.windows;
    case 3:return &f.shared;
    case 4:return f.file;
    case 5:return &f.multi;
    case 6:return &f.tabs;
    case 7:return &f.table_pool;
    case 8:return &f.cell_span;
    case 9:return &f.column_span;
    case 10:return &f.short_span;
    case 11:return &f.baked;
    case 12:return &f.textedit;
    default:return NULL;
    }
}
void* purr_fixture_rects(void* fixture) { return fixture ? &((PurrContainerFixture*)fixture)->rects : NULL; }
void* purr_fixture_text(void* fixture) { return fixture ? &((PurrContainerFixture*)fixture)->text : NULL; }
int32_t purr_fixture_file_position(void* fixture) { return (int32_t)ftell(((PurrContainerFixture*)fixture)->file); }
void* purr_fixture_nonseekable_file(void* fixture) { return fixture ? ((PurrContainerFixture*)fixture)->nonseekable : NULL; }
int32_t purr_fixture_counts(int32_t* created,int32_t* destroyed)
{
    if(!created || !destroyed) return PURR_NULL;
    *created=PurrCreated; *destroyed=PurrDestroyed; return PURR_OK;
}
int32_t purr_fixture_style(int32_t index,PurrTriple* out)
{
    if(!out) return PURR_NULL;
    if(index<0 || index>=IM_ARRAYSIZE(GStyleVarsInfo)) return PURR_RANGE;
    const auto& s=GStyleVarsInfo[index];
    *out={(int32_t)s.Count,(int32_t)s.DataType,(int32_t)s.Offset};
    return PURR_OK;
}

// TEST-ONLY pass-through allocation ledger. Bookkeeping uses CRT malloc/free,
// never ImGui allocation, so the callbacks cannot recurse through themselves.
struct PurrAllocationNode { void* address; PurrAllocationNode* next; };
static PurrAllocationNode* PurrAllocations=NULL;
static ImGuiMemAllocFunc PurrOriginalAlloc=NULL;
static ImGuiMemFreeFunc PurrOriginalFree=NULL;
static void* PurrOriginalUserData=NULL;
static bool PurrAuditing=false;
static PurrAllocationAudit PurrAudit={};
static void PurrAuditIncrement(uint64_t& value)
{
    if(value==UINT64_MAX) { PurrAudit.errors=UINT64_MAX; return; }
    ++value;
}
static void* PurrAuditedAlloc(size_t size, void*)
{
    void* address=PurrOriginalAlloc(size,PurrOriginalUserData);
    if(!address) { PurrAuditIncrement(PurrAudit.failed); return NULL; }
    PurrAuditIncrement(PurrAudit.allocations);
    for(PurrAllocationNode* n=PurrAllocations;n;n=n->next)
        if(n->address==address) { PurrAuditIncrement(PurrAudit.errors); return address; }
    auto* node=(PurrAllocationNode*)malloc(sizeof(PurrAllocationNode));
    if(!node) { PurrAuditIncrement(PurrAudit.errors); return address; }
    node->address=address; node->next=PurrAllocations; PurrAllocations=node;
    PurrAuditIncrement(PurrAudit.outstanding);
    return address;
}
static void PurrAuditedFree(void* address, void*)
{
    if(!address) { PurrAuditIncrement(PurrAudit.null_frees); PurrOriginalFree(address,PurrOriginalUserData); return; }
    PurrAllocationNode** link=&PurrAllocations;
    while(*link && (*link)->address!=address) link=&(*link)->next;
    if(!*link) PurrAuditIncrement(PurrAudit.errors);
    else {
        PurrAllocationNode* node=*link; *link=node->next; free(node);
        if(!PurrAudit.outstanding) PurrAuditIncrement(PurrAudit.errors); else --PurrAudit.outstanding;
        PurrAuditIncrement(PurrAudit.frees);
    }
    PurrOriginalFree(address,PurrOriginalUserData);
}
int32_t purr_fixture_audit_begin(void)
{
    if(PurrAuditing || ImGui::GetCurrentContext()) return PURR_ID;
    // NULL context is necessary, NOT sufficient: fresh-process provenance remains
    // the harness precondition for excluding preexisting objects/allocations.
    ImGui::GetAllocatorFunctions(&PurrOriginalAlloc,&PurrOriginalFree,&PurrOriginalUserData);
    if(!PurrOriginalAlloc || !PurrOriginalFree) return PURR_NULL;
    PurrAudit={}; PurrAllocations=NULL; PurrAuditing=true;
    ImGui::SetAllocatorFunctions(PurrAuditedAlloc,PurrAuditedFree,NULL);
    return PURR_OK;
}
int32_t purr_fixture_audit_end(PurrAllocationAudit* out)
{
    if(!PurrAuditing) return PURR_ID;
    ImGuiMemAllocFunc current_alloc;
    ImGuiMemFreeFunc current_free;
    void* current_user;
    ImGui::GetAllocatorFunctions(&current_alloc,&current_free,&current_user);
    if(current_alloc!=PurrAuditedAlloc || current_free!=PurrAuditedFree || current_user!=NULL)
        PurrAuditIncrement(PurrAudit.errors);
    // Restore even on audit failure; never free a leaked client object to hide it.
    ImGui::SetAllocatorFunctions(PurrOriginalAlloc,PurrOriginalFree,PurrOriginalUserData);
    ImGui::GetAllocatorFunctions(&current_alloc,&current_free,&current_user);
    PurrAudit.restored=(current_alloc==PurrOriginalAlloc && current_free==PurrOriginalFree && current_user==PurrOriginalUserData) ? 1 : 0;
    if(!PurrAudit.restored) PurrAuditIncrement(PurrAudit.errors);
    while(PurrAllocations) {
        PurrAllocationNode* node=PurrAllocations; PurrAllocations=node->next; free(node);
    }
    PurrAuditing=false;
    if(!out) return PURR_NULL;
    *out=PurrAudit;
    return PurrAudit.errors || PurrAudit.outstanding || PurrAudit.failed ? PURR_ID : PURR_OK;
}
