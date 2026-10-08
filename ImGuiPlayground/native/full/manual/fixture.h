// TEST-ONLY exports, never part of the production 22-symbol component.
#ifndef PURR_MANUAL_FIXTURE_H
#define PURR_MANUAL_FIXTURE_H
#include "manual.h"
enum PurrManualCase {
    PurrBulletText, PurrDebugLog, PurrLabelText, PurrLogText,
    PurrSetItemTooltip, PurrSetTooltip, PurrText, PurrTextColored,
    PurrTextDisabled, PurrTextWrapped, PurrTreeNode1, PurrTreeNode2,
    PurrTreeNodeEx1, PurrTreeNodeEx2, PurrTextAligned
};
typedef uint8_t (*PurrFormatExercise)(int32_t, const char*, void*, int32_t);
typedef void (*PurrTextVExercise)(const char*, va_list);
PURR_API ImGuiContext* purr_manual_fixture_create();
PURR_API void purr_manual_fixture_destroy(ImGuiContext*);
// NULL means all assertions passed; otherwise a borrowed diagnostic until next call.
PURR_API const char* purr_manual_fixture_formats(PurrFormatExercise);
PURR_API const char* purr_manual_fixture_textv(PurrTextVExercise);
PURR_API ImGuiViewport* purr_manual_fixture_viewport();
PURR_API PurrVec2 purr_manual_fixture_invoke(PurrNativeViewportVector, ImGuiViewport*);
#endif
