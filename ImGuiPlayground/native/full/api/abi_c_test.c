// C11 consumption check for the common POD interface (no native includes).
#include "purr_abi.h"
#include <assert.h>
#include <stdalign.h>

static_assert(sizeof(PurrVec2) == 8 && alignof(PurrVec2) == 4, "float2 ABI");
static_assert(sizeof(PurrVec4) == 16 && alignof(PurrVec4) == 4, "float4 ABI");
static_assert(sizeof(PurrRect) == 16 && offsetof(PurrRect, Max) == 8, "rect ABI");
static_assert(sizeof(PurrTextureRef) == 16 && offsetof(PurrTextureRef, TexID) == 8, "texture ABI");
static_assert(sizeof(PurrClipperRange) == 12 && offsetof(PurrClipperRange, PosToIndexOffsetMax) == 10, "clipper ABI");
