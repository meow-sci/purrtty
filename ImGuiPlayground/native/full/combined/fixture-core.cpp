// TEST ONLY. One actual configured core supplies both CPP-local fixture bodies.
// Do not define the marker globally or compile either included body separately.
#ifdef PURR_COMBINED_CORE_INCLUDED
#error "combined core marker must not be supplied by build flags"
#endif
#include "imgui.cpp"
#define PURR_COMBINED_CORE_INCLUDED 1
#include "../accessors/fixture.cpp"
#include "enum-fixture.cpp"
#undef PURR_COMBINED_CORE_INCLUDED
