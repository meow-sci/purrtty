// Compiled to LLVM IR on each target, not linked as an extra native export.
#include "purr_abi.h"

extern "C" bool* purr_bool_output_proof(uint8_t* p) {
    return purr::bool_output(p);
}
extern "C" bool* purr_bool_inout_proof(uint8_t* p) {
    return purr::bool_inout(p);
}
