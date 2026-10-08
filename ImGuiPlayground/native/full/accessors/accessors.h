#ifndef PURR_ACCESSORS_H
#define PURR_ACCESSORS_H
#include <stdint.h>
#include <stddef.h>
#if defined(_WIN32)
#define PURR_ACCESSOR_EXPORT __declspec(dllexport)
#else
#define PURR_ACCESSOR_EXPORT __attribute__((visibility("default")))
#endif
#if defined(__cplusplus)
extern "C" {
#endif
/* Versioned supplemental ABI. All pointers are caller-borrowed, correctly typed live
 * native objects on their owning UI thread. No allocation uses a CLR placeholder.
 * Errors do not write output except text_copy's required size. No C++ exceptions.
 * Representational validation is NOT validation of live-engine invariants. */
enum PurrAccessorStatus { PURR_OK=0, PURR_NULL=1, PURR_ID=2, PURR_RANGE=3, PURR_READONLY=4, PURR_BUFFER=5, PURR_IO=6 };
typedef struct PurrFieldInfo { int32_t record, original_bits, effective_bits, is_signed, read_only, is_boolean; } PurrFieldInfo;
typedef struct PurrTriple { int32_t a,b,c; } PurrTriple;
typedef struct PurrSharedSnapshot { float font_size, font_scale, curve_tolerance, circle_error; uint32_t initial_flags; } PurrSharedSnapshot;
typedef struct PurrTextEditSnapshot { int32_t cursor, select_start, select_end, insert_mode; } PurrTextEditSnapshot;
typedef struct PurrCellSnapshot { uint32_t color; int32_t column; } PurrCellSnapshot;
PURR_ACCESSOR_EXPORT const char* purr_accessors_identity(void);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_field_info(int32_t field, PurrFieldInfo* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_get(int32_t record, const void* object, int32_t field, int64_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_set(int32_t record, void* object, int32_t field, int64_t value);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_style_count(void);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_style(int32_t index, PurrTriple* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_rect_count(const void* vector, int32_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_rect(const void* vector, int32_t index, PurrTriple* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_rect_set(void* vector, int32_t index, int32_t field, int64_t value);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_empty_string(uint8_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_text_copy(const void* buffer, uint8_t* out, int32_t capacity, int32_t* required);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_text_append(void* buffer, const uint8_t* utf8, int32_t length);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_text_clear(void* buffer);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_named_key(void* bits, int32_t key, int32_t write, int32_t* value);
/* Shape IDs in manifest.json/opaqueShapes. Counts: chunks=count of records; pools=map slots
 * including tombstones; spans/stable=logical elements. at(pool) may return NULL for
 * a tombstone. lookup is pool-only, absent keys return NULL. Never index Buf holes. */
PURR_ACCESSOR_EXPORT int32_t purr_accessor_count(int32_t shape, const void* object, int32_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_at(int32_t shape, void* object, int32_t index, void** out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_lookup(int32_t shape, void* object, uint32_t key, void** out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_pool_alive(int32_t shape, const void* object, int32_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_short(const void* span, int32_t index, int32_t* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_cell(const void* span, int32_t index, PurrCellSnapshot* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_shared(const void* object, PurrSharedSnapshot* out);
PURR_ACCESSOR_EXPORT int32_t purr_accessor_textedit(const void* object, PurrTextEditSnapshot* out);
/* ImFileHandle is the pointer VALUE, not address of a Size1 managed placeholder.
 * Uses ImFileGetSize: attempts restoration of position on success; on failure the
 * native function may have moved position. Does not close/own the file. */
PURR_ACCESSOR_EXPORT int32_t purr_accessor_file_size(void* file, uint64_t* out);
#if defined(__cplusplus)
}
#endif
#endif
