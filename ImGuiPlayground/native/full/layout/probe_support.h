#pragma once
// Storage observations only. No ABI forwarding or semantic bitfield accessors.
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <type_traits>

template<class T> struct LayoutRemovePointer { using type = T; };
template<class T> struct LayoutRemovePointer<T*> { using type = T; };
template<class T> struct LayoutRemoveReference { using type = T; };
template<class T> struct LayoutRemoveReference<T&> { using type = T; };

template<class T> struct LayoutShape
{
    static constexpr size_t count = 0;
    static constexpr size_t stride = sizeof(T);
};
template<class T, size_t N> struct LayoutShape<T[N]>
{
    static constexpr size_t count = N;
    static constexpr size_t stride = sizeof(T);
};
template<class T, bool E = std::is_enum<T>::value> struct LayoutSigned
{
    static constexpr int value = std::is_integral<T>::value ? (std::is_signed<T>::value ? 1 : 0) : -1;
};
template<class T> struct LayoutSigned<T, true>
{
    static constexpr int value = std::is_signed<typename std::underlying_type<T>::type>::value ? 1 : 0;
};

// Stable numeric table: IDs index mapping.json type/field arrays. -1 offset is absent,
// never inferred for bitfields/static members. All storage numbers are C++ expressions.
struct LayoutEntry
{
    int64_t type_id, field_id, size, alignment, offset, array_count, element_stride, signedness;
};

template<class T> constexpr LayoutEntry LayoutType(int64_t id)
{
    return {id, -1, sizeof(T), alignof(T), -1, LayoutShape<T>::count, LayoutShape<T>::stride, LayoutSigned<T>::value};
}
template<class T> constexpr LayoutEntry LayoutField(int64_t id, int64_t field, int64_t offset)
{
    return {id, field, sizeof(T), alignof(T), offset, LayoutShape<T>::count, LayoutShape<T>::stride, LayoutSigned<T>::value};
}
