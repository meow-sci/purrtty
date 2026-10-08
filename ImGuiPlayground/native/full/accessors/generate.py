"""Closed accessor inventory. Generation is only into a new, prevalidated scratch tree."""
from __future__ import annotations
import importlib.util
import json
import sys
from pathlib import Path
from typing import Any

HERE = Path(__file__).absolute().parent
# This invoked entrypoint is the trusted bootstrap. Check the guard leaf/cache
# and every ancestor lexically BEFORE importing any selected project module.
guard_file = HERE / 'path_guard.py'
for selected_module in (guard_file, Path(importlib.util.cache_from_source(str(guard_file)))):
    if '..' in selected_module.parts:
        raise ValueError('accessor preflight: parent traversal not allowed: '+str(selected_module))
    for candidate in (selected_module, *selected_module.parents):
        if candidate.is_symlink():
            raise ValueError('accessor preflight: symlink not allowed: '+str(candidate))
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('purr_accessor_paths', guard_file)
assert spec is not None and spec.loader is not None
paths: Any = importlib.util.module_from_spec(spec)
spec.loader.exec_module(paths)
paths.preflight_component(HERE)
NATIVE = HERE.parents[1]
patch = paths.load_module('patch_source', HERE.parent / 'layout/patch_source.py')
layout = paths.load_module('purr_layout_generate', HERE.parent / 'layout/generate.py')
digest = patch.digest

SOURCE_LOCK_SHA256 = '0f8dba40bb599030d1a13d8907a07a411a4c55a5b6fad208768d3e43b0602a77'


def verify_source_lock(data: bytes) -> dict:
    if digest(data) != SOURCE_LOCK_SHA256:
        raise ValueError('frozen source/config/toolchain/target profile changed')
    return json.loads(data)


DOMAINS = {
 'ImFontGlyph': 'Colored/Visible canonical0/1; SourceIdx must select an existing font source; Codepoint must satisfy the caller font/glyph domain, not merely fit26 bits.',
 'ImFontBaked': 'MetricsTotalSurface counts actual surface; load/destroy flags participate in atlas lifecycle. Caller maintains font/atlas invariants.',
 'ImGuiStyleVarInfo': 'Const native style table; Count/type/Offset describe the actual ImGuiStyle member. Production writes forbidden.',
 'ImGuiBoxSelectState': 'KeyMods live values are modifier masks (Ctrl/Shift/Alt/Super), not arbitrary signed integers.',
 'ImGuiDockNode': 'Authorities are ImGuiDataAuthority named values0/1/2; bools participate in docking lifecycle. Caller maintains tree invariants.',
 'ImGuiStackLevelInfo': 'DataType must be a valid public/private ImGuiDataType for the associated debug ID description (Desc).',
 'ImGuiContext': 'ActiveIdMouseButton is -1 or a valid mouse button0..4; arbitrary representable ints are fixture-only.',
 'ImGuiWindow': 'AllowFlags are ImGuiCond masks (Always/Once/FirstUseEver/Appearing); docking bools require consistent live docking state.',
 'ImGuiTableColumn': 'SortDirection uses named0/1/2; availability count/mask must describe actual permitted sort directions.',
 'ImGuiTable': 'RowFlags/LastRowFlags use ImGuiTableRowFlags (None/Headers) and actual row lifecycle.',
 'ImGuiTableColumnSettings': 'SortDirection named0/1/2; IsEnabled is signed2-bit storage, not bool (preserve -1 sentinel); settings must agree with the table.',
 'ImFontAtlasRectEntry': 'TargetIndex must reference valid Rects/free-list or native sentinel; Generation follows native ID recycling; IsUsed agrees with allocator state. Do not mutate live allocator state independently.',
}
OPAQUE = [
 ('NamedKeys','ImBitArrayForNamedKeys','named_key','Borrowed named-key bit storage; actual ImGuiKey_NamedKey_BEGIN <= key < END.'),
 ('TableSettings','ImChunkStream<ImGuiTableSettings>','count/at','Borrowed chunk ordinal iteration via begin/next_chunk; invalidate on stream mutation.'),
 ('WindowSettings','ImChunkStream<ImGuiWindowSettings>','count/at','Borrowed chunk ordinal iteration via begin/next_chunk; invalidate on stream mutation.'),
 ('DrawListSharedData','ImDrawListSharedData','shared snapshot','Borrowed context/draw-list shared state; copied scalar snapshot only.'),
 ('FileHandle','ImFileHandle','file_size','Pointer-valued borrowed file; no close. Native failure may leave position moved.'),
 ('MultiSelectPool','ImPool<ImGuiMultiSelectState>','count/at/lookup/alive','Map iteration skips tombstones; invalidate before add/remove/reserve/clear.'),
 ('TabBarPool','ImPool<ImGuiTabBar>','count/at/lookup/alive','Map iteration skips tombstones; invalidate before add/remove/reserve/clear.'),
 ('TablePool','ImPool<ImGuiTable>','count/at/lookup/alive','Map iteration skips tombstones; invalidate before add/remove/reserve/clear.'),
 ('CellSpan','ImSpan<ImGuiTableCellData>','count/at/cell snapshot','Borrowed backing array; invalidate before backing destruction/reallocation or span rebinding.'),
 ('ColumnSpan','ImSpan<ImGuiTableColumn>','count/at','Borrowed native column indexing; derived record accesses retain scope.'),
 ('ShortSpan','ImSpan<short>','count/at/short snapshot','Borrowed backing array; copied short value, never a managed Size1 stride.'),
 ('BakedVector','ImStableVector<ImFontBaked,32>','count/at','Real block indexing; growth retains addresses natively, clear/destruction invalidates. No component ownership/destruction.'),
 ('TextEditState','ImStbTexteditState','textedit snapshot','Real configured ImStb header definition; borrowed input-text lifetime, no replacement editor algorithm.'),
]

def generate(contract: Path, output: Path) -> dict:
    paths.preflight_component(HERE)
    contract = paths.input_file(contract)
    output = paths.generation_output(output, NATIVE.parent, contract)
    verify_source_lock(paths.input_file(NATIVE/'source.lock.json').read_bytes())
    _, mapping, bits = layout.load_inputs(contract)
    fields = bits['members']
    types = list(dict.fromkeys(b['nativeType'] for b in fields))
    if set(types) != set(DOMAINS):
        raise ValueError('domain inventory changed')
    if {x['native'] for x in mapping['types'] if x['disposition']=='opaque-placeholder'} != {x[1] for x in OPAQUE}:
        raise ValueError('opaque inventory changed')
    members: list[dict[str, Any]]=[]
    for i,b in enumerate(fields):
        width=32 if b['disposition']=='promoted-ordinary' else b['originalWidthBits']
        signed=b['signed']
        lo=-(1 << (width-1)) if signed else 0
        hi=(1 << (width-int(signed)))-1
        members.append(dict(b,id=i,record=types.index(b['nativeType']),effectiveWidthBits=width,minimum=lo,maximum=hi,readOnly=b['nativeType']=='ImGuiStyleVarInfo',boolean=b['declaredType']=='bool' or (width==1 and not signed),domain=DOMAINS[b['nativeType']],maskEvidence='actual native member assignments and byte snapshots; per-executed-target managed-report.json',semanticEvidence='FullAccessorChecks actual native/helper/oracle and two-record preservation; foreign execution remains pending'))
    manifest={'schema':'purr.native-accessors','version':1,'contractSha256':layout.CONTRACT_SHA256,'portableApiProjectionSha256':'3e3cd8793d90fcfd351ef14c9179446d56ec27c859fda78027a54c96804330eb','layoutPins':layout.INPUT_PINS,'sourceLockSha256':digest((NATIVE/'source.lock.json').read_bytes()),'patchManifestSha256':digest((HERE.parent/'layout/patch-manifest.json').read_bytes()),'records':types,'fields':members,'opaqueShapes':[{'id':i,'name':n,'native':t,'route':r,'lifetime':lifetime} for i,(n,t,r,lifetime) in enumerate(OPAQUE)]}
    manifest['componentContractHashes']={p.name:digest(p.read_bytes()) for p in [HERE/'accessors.h',HERE/'accessors.cpp',HERE/'generate.py',HERE/'path_guard.py']}
    identity=digest(json.dumps(manifest,sort_keys=True,separators=(',',':')).encode())
    manifest['identity']=identity
    info=[]
    get=[]
    set_=[]
    assign=[]
    ptr=[]
    create=[]
    destroy=[]
    for b in members:
        i,t,m,r,w,s=b['id'],b['nativeType'],b['nativeMember'],b['record'],b['effectiveWidthBits'],int(b['signed'])
        info.append(f'case {i}: *out = {{{r},{b["originalWidthBits"]},{w},{s},{int(b["readOnly"])},{int(b["boolean"])}}}; return PURR_OK;')
        get.append(f'case {i}: if(record!={r}) return PURR_ID; *out = ((const {t}*)object)->{m}; return PURR_OK;')
        condition=f'value < INT64_C({b["minimum"]}) || value > INT64_C({b["maximum"]})'
        write=f'(({t}*)object)->{m} = static_cast<decltype((({t}*)object)->{m})>(value); return PURR_OK;'
        set_.append(f'case {i}: if(record!={r}) return PURR_ID; '+('return PURR_READONLY;' if b['readOnly'] else f'if({condition}) return PURR_RANGE; {write}'))
        assign.append(f'case {i}: {write}')
    for r,t in enumerate(types):
        args={'ImGuiDockNode':'100 + i','ImGuiWindow':'fixture_context, i ? "accessor-neighbor" : "accessor-target"','ImGuiContext':'NULL'}.get(t,'')
        initialize = f'ImGui::SetCurrentContext((({t}*)objects)+i); ImGui::Initialize();' if t=='ImGuiContext' else ''
        create.append(f'case {r}: objects=ImGui::MemAlloc(2*sizeof({t})); for(int i=0;i<2;i++) {{ IM_PLACEMENT_NEW((({t}*)objects)+i) {t}({args}); {initialize} }} ImGui::SetCurrentContext(fixture_context); stride=sizeof({t}); break;')
        cleanup='ImGui::Shutdown();' if t=='ImGuiContext' else ''
        destroy.append(f'case {r}: for(int i=0;i<2;i++) {{ '+(f'ImGui::SetCurrentContext((({t}*)objects)+i); {cleanup}' if cleanup else '')+f'((({t}*)objects)+i)->~{t}(); }} break;')
        ptr.append(f'case {r}: return (({t}*)objects)+index;')
    def conditional(name,lines):
        return '#ifdef '+name+'\n'+'\n'.join(lines)+'\n#endif\n'
    (output/'fields.inc').write_text(conditional('PURR_INFO',info)+conditional('PURR_GET',get)+conditional('PURR_SET',set_))
    (output/'fixture_fields.inc').write_text(conditional('PURR_ASSIGN',assign)+conditional('PURR_CREATE',create)+conditional('PURR_DESTROY',destroy)+conditional('PURR_POINTER',ptr)+conditional('PURR_DIRECT_GET',get))
    (output/'identity.h').write_text('#define PURR_ACCESSOR_IDENTITY "'+identity+'"\n')
    (output/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    return manifest
