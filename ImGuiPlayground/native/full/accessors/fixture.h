#pragma once
#include "accessors.h"
#ifdef __cplusplus
extern "C" {
#endif
/* TEST-ONLY exports: never production accessor ABI; see exports.json/testOnly.
 * No callback or engine algorithm consumes deliberately invalid field values. */
PURR_ACCESSOR_EXPORT void* purr_fixture_field_create(int32_t field);
PURR_ACCESSOR_EXPORT void purr_fixture_field_destroy(void* fixture);
PURR_ACCESSOR_EXPORT void* purr_fixture_field_object(void* fixture, int32_t index);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_field_stride(void* fixture);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_field_snapshot(void* fixture, uint8_t* out, int32_t capacity);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_field_mask(void* fixture, uint8_t* out, int32_t capacity);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_field_assign(void* fixture, int64_t value);
PURR_ACCESSOR_EXPORT int64_t purr_fixture_field_read(void* fixture);
PURR_ACCESSOR_EXPORT void* purr_fixture_containers_create(const char* file_path);
PURR_ACCESSOR_EXPORT void purr_fixture_containers_destroy(void* fixture);
PURR_ACCESSOR_EXPORT void* purr_fixture_container(void* fixture, int32_t shape);
PURR_ACCESSOR_EXPORT void* purr_fixture_rects(void* fixture);
PURR_ACCESSOR_EXPORT void* purr_fixture_text(void* fixture);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_file_position(void* fixture);
PURR_ACCESSOR_EXPORT void* purr_fixture_nonseekable_file(void* fixture);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_counts(int32_t* created, int32_t* destroyed);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_style(int32_t index, PurrTriple* out);
/* Fresh single-owner process only; no preexisting native objects/allocations. */
typedef struct PurrAllocationAudit { uint64_t allocations, frees, failed, null_frees, outstanding, errors, restored; } PurrAllocationAudit;
PURR_ACCESSOR_EXPORT int32_t purr_fixture_audit_begin(void);
PURR_ACCESSOR_EXPORT int32_t purr_fixture_audit_end(PurrAllocationAudit* out);
#ifdef __cplusplus
}
#endif
