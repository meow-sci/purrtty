#pragma once
/* <purrtty.h> - purrTTY vehicle-parts services for kFS.
   Generated from purrTTY's service catalogue; do not edit.
   Every function is a kFS service (SYSCALL 0x5000-0x50FF) provided by the
   purrTTY mod: an image that calls one does not load without it. Indices are 0-based,
   per vehicle, and frozen when a run starts - load and start, reset, stop and start -
   until x_parts_rescan. Where there is no part (an index out of range, a part that has
   left) every property reads 0, which for some properties is also a real value: check
   the PRESENT property first. Writes are applied at the next frame boundary: read a
   value back no earlier than the next tick. */
#include <kfs.h>

/* x_parts_info: selectors of x_parts_info */
#define X_PARTS_GENERATION 0
#define X_PARTS_DIRTY 1
#define X_PARTS_LIGHTS 2
#define X_PARTS_DEPLOYABLES 3
#define X_PARTS_ACTUATORS 4

/* x_part_kind: part kinds of x_parts_describe */
#define X_KIND_ALL 0
#define X_KIND_LIGHT 1
#define X_KIND_DEPLOYABLE 2
#define X_KIND_ACTUATOR 3

/* x_light_prop: light properties: integers 0..15 (x_light_geti), floats 16 and up (x_light_getf) */
#define X_LIGHT_PRESENT 0
#define X_LIGHT_HAS_SWITCH 1
#define X_LIGHT_ON 2
#define X_LIGHT_LIT 3
#define X_LIGHT_TYPE 4
#define X_LIGHT_PART_ID 5
#define X_LIGHT_SWITCH_GROUP 6
#define X_LIGHT_OVERRIDDEN 7
#define X_LIGHT_INTENSITY 16
#define X_LIGHT_RANGE_M 17
#define X_LIGHT_COLOR_R 18
#define X_LIGHT_COLOR_G 19
#define X_LIGHT_COLOR_B 20
#define X_LIGHT_INNER_RAD 21
#define X_LIGHT_OUTER_RAD 22
#define X_LIGHT_STOCK_INTENSITY 23

/* x_deploy_prop: deployable properties: integers 0..15 (x_deploy_geti), floats 16 and up (x_deploy_getf); position and target are 0..1 of the travel */
#define X_DEPLOY_PRESENT 0
#define X_DEPLOY_STATE 1
#define X_DEPLOY_COMMANDED 2
#define X_DEPLOY_MOVING 3
#define X_DEPLOY_PART_ID 4
#define X_DEPLOY_LINKS 5
#define X_DEPLOY_POSITION 16
#define X_DEPLOY_TARGET 17
#define X_DEPLOY_DURATION_S 18

/* x_actuator_prop: actuator properties: integers 0..15 (x_actuator_geti), floats 16 and up (x_actuator_getf); position and target are 0..1 of the travel */
#define X_ACTUATOR_PRESENT 0
#define X_ACTUATOR_MOVING 1
#define X_ACTUATOR_PART_ID 2
#define X_ACTUATOR_LINKS 3
#define X_ACTUATOR_POSITION 16
#define X_ACTUATOR_TARGET 17
#define X_ACTUATOR_DURATION_S 18

/* constants: values of X_DEPLOY_STATE: X_STATE_NONE (0) where there is no part; X_STATE_BROKEN is never produced in this game build */
#define X_STATE_NONE 0
#define X_STATE_DEPLOYED 1
#define X_STATE_RETRACTED 2
#define X_STATE_DEPLOYING 3
#define X_STATE_RETRACTING 4
#define X_STATE_BROKEN 5

/* constants: values of X_LIGHT_TYPE */
#define X_LIGHT_TYPE_SPOT 0
#define X_LIGHT_TYPE_POINT 1

/* x_parts_info - Reads a vehicle-wide parts figure: the list generation, the dirty flag or a part
   count.
   what: an X_PARTS_* selector.
   Returns the selected figure.
   X_PARTS_GENERATION changes each time the part lists are rebuilt (each run, x_parts_rescan).
   X_PARTS_DIRTY is 1 when the vehicle's module counts differ from the frozen lists, a hint to
   rescan. X_PARTS_LIGHTS, X_PARTS_DEPLOYABLES and X_PARTS_ACTUATORS are the list lengths.
   Service 0x5000 SVC_X_PARTS_INFO, cost class Light. */
uint32_t x_parts_info(uint32_t what);

/* x_parts_rescan - Rebuilds the vehicle's part lists; indices may change and X_PARTS_GENERATION
   changes.
   Lists are frozen when a run starts and change only here, never spontaneously. Commands issued
   before the rescan still reach the part they named. The rebuild happens at the next frame
   boundary, once however many rescans a tick issues: poll X_PARTS_GENERATION.
   Service 0x5001 SVC_X_PARTS_RESCAN, cost class Medium. */
void x_parts_rescan(void);

/* x_parts_describe - Writes the index table of one part kind, or of all kinds, to the program log.
   kind: an X_KIND_* value.
   Shows which index is which part. The table is written when the calling tick ends; a second call
   within the same second of computer time is rejected.
   Rejected (program log, sys.rejected_requests) when: more than one table per second.
   Service 0x5002 SVC_X_PARTS_DESCRIBE, cost class Medium. */
void x_parts_describe(uint32_t kind);

/* x_light_count - Number of lights on the vehicle; indices are 0 to count-1.
   Returns the number of lights.
   Every light emitter of the vehicle, interior fill lights included, ranked by part instance id in
   the list frozen when the run started.
   Service 0x5010 SVC_X_LIGHT_COUNT, cost class Light. */
uint32_t x_light_count(void);

/* x_light_geti - Reads an integer property of a light; 0 for an index out of range.
   light: light index.
   prop: an X_LIGHT_* integer property, X_LIGHT_PRESENT to X_LIGHT_OVERRIDDEN.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5011 SVC_X_LIGHT_GETI, cost class Light. */
uint32_t x_light_geti(uint32_t light, uint32_t prop);

/* x_light_getf - Reads a float property of a light; 0 for an index out of range.
   light: light index.
   prop: an X_LIGHT_* float property, X_LIGHT_INTENSITY to X_LIGHT_STOCK_INTENSITY.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5012 SVC_X_LIGHT_GETF, cost class Light. */
float x_light_getf(uint32_t light, uint32_t prop);

/* x_light_set_on - Switches a light's part on or off; every light on the same switch follows.
   light: light index.
   on: 0 switches off, anything else on.
   On and off are per part: emitters sharing a part switch together (see X_LIGHT_SWITCH_GROUP). A
   light without a switch (X_LIGHT_HAS_SWITCH 0) is rejected.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed; light
   has no switch.
   Service 0x5013 SVC_X_LIGHT_SET_ON, cost class Light. */
void x_light_set_on(uint32_t light, uint32_t on);

/* x_light_set_color - Overrides a light's emission colour (linear RGB, each component clamped to
   0..1).
   light: light index.
   rgb: linear RGB, each component clamped to 0..1.
   The first appearance write gives this light a private copy of its template, so other lights of
   the same part type are unaffected. The override lasts until x_light_reset, a reload or the
   part's destruction, and is not saved.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed.
   Service 0x5014 SVC_X_LIGHT_SET_COLOR, cost class Light. */
void x_light_set_color(uint32_t light, vec3 rgb);

/* x_light_set_cone - Overrides a spot light's cone half-angles, in radians; ignored for point
   lights.
   light: light index.
   inner_rad: inner cone half-angle in radians, clamped to 0..outer_rad.
   outer_rad: outer cone half-angle in radians, clamped to 1e-5..1.5697963.
   The first appearance write gives this light a private copy of its template, so other lights of
   the same part type are unaffected. The override lasts until x_light_reset, a reload or the
   part's destruction, and is not saved.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed.
   Service 0x5015 SVC_X_LIGHT_SET_CONE, cost class Light. */
void x_light_set_cone(uint32_t light, float inner_rad, float outer_rad);

/* x_light_set_intensity - Overrides a light's intensity (0 to 1e6).
   light: light index.
   intensity: unitless, >= 0, clamped to at most 1e6 (stock parts use 0.05..50).
   The first appearance write gives this light a private copy of its template, so other lights of
   the same part type are unaffected. The override lasts until x_light_reset, a reload or the
   part's destruction, and is not saved. X_LIGHT_STOCK_INTENSITY is the authored value to scale
   from.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed.
   Service 0x5016 SVC_X_LIGHT_SET_INTENSITY, cost class Light. */
void x_light_set_intensity(uint32_t light, float intensity);

/* x_light_set_range - Overrides a light's range in metres (0 to 1e6).
   light: light index.
   range_m: range in metres, >= 0, clamped to at most 1e6.
   The first appearance write gives this light a private copy of its template, so other lights of
   the same part type are unaffected. The override lasts until x_light_reset, a reload or the
   part's destruction, and is not saved.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed.
   Service 0x5017 SVC_X_LIGHT_SET_RANGE, cost class Light. */
void x_light_set_range(uint32_t light, float range_m);

/* x_light_reset - Restores a light's stock colour, cone, intensity and range (not its on/off
   state).
   light: light index.
   Undoes every appearance override of this light; X_LIGHT_OVERRIDDEN reads 0 afterwards.
   Rejected (program log, sys.rejected_requests) when: light index out of range or departed.
   Service 0x5018 SVC_X_LIGHT_RESET, cost class Light. */
void x_light_reset(uint32_t light);

/* x_lights_set_all - Switches every light that has a switch on or off, like the game's lights key.
   on: 0 switches off, anything else on.
   Lights without a switch are left alone.
   Service 0x5019 SVC_X_LIGHTS_SET_ALL, cost class Light. */
void x_lights_set_all(uint32_t on);

/* x_deploy_count - Number of deployables (parts the game shows with Deploy and Retract).
   Returns the number of deployables.
   Animations with Deploy / Retract buttons and a duration above zero, ranked by part instance id
   in the list frozen when the run started.
   Service 0x5020 SVC_X_DEPLOY_COUNT, cost class Light. */
uint32_t x_deploy_count(void);

/* x_deploy_geti - Reads an integer property of a deployable; 0 for an index out of range.
   index: deployable index.
   prop: an X_DEPLOY_* integer property, X_DEPLOY_PRESENT to X_DEPLOY_LINKS.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5021 SVC_X_DEPLOY_GETI, cost class Light. */
uint32_t x_deploy_geti(uint32_t index, uint32_t prop);

/* x_deploy_getf - Reads a float property of a deployable; 0 for an index out of range.
   index: deployable index.
   prop: an X_DEPLOY_* float property, X_DEPLOY_POSITION to X_DEPLOY_DURATION_S.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5022 SVC_X_DEPLOY_GETF, cost class Light. */
float x_deploy_getf(uint32_t index, uint32_t prop);

/* x_deploy_set - Deploys (1) or retracts (0) a deployable.
   index: deployable index.
   deployed: 0 retracts, anything else deploys.
   The animation moves at its authored rate: poll X_DEPLOY_STATE or X_DEPLOY_POSITION.
   Rejected (program log, sys.rejected_requests) when: deployable index out of range or departed.
   Service 0x5023 SVC_X_DEPLOY_SET, cost class Light. */
void x_deploy_set(uint32_t index, uint32_t deployed);

/* x_deploy_toggle - Flips a deployable's commanded state; valid mid-travel.
   index: deployable index.
   The animation reverses from where it is.
   Rejected (program log, sys.rejected_requests) when: deployable index out of range or departed.
   Service 0x5024 SVC_X_DEPLOY_TOGGLE, cost class Light. */
void x_deploy_toggle(uint32_t index);

/* x_actuator_count - Number of actuators (parts the game shows with a 0..1 slider).
   Returns the number of actuators.
   Animations without Deploy / Retract buttons, ranked by part instance id in the list frozen when
   the run started.
   Service 0x5030 SVC_X_ACTUATOR_COUNT, cost class Light. */
uint32_t x_actuator_count(void);

/* x_actuator_geti - Reads an integer property of an actuator; 0 for an index out of range.
   index: actuator index.
   prop: an X_ACTUATOR_* integer property, X_ACTUATOR_PRESENT to X_ACTUATOR_LINKS.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5031 SVC_X_ACTUATOR_GETI, cost class Light. */
uint32_t x_actuator_geti(uint32_t index, uint32_t prop);

/* x_actuator_getf - Reads a float property of an actuator; 0 for an index out of range.
   index: actuator index.
   prop: an X_ACTUATOR_* float property, X_ACTUATOR_POSITION to X_ACTUATOR_DURATION_S.
   Returns the property value.
   Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, so
   the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is
   also a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads
   X_STATE_NONE. A selector of the other getter is rejected by the VM at issue time.
   Service 0x5032 SVC_X_ACTUATOR_GETF, cost class Light. */
float x_actuator_getf(uint32_t index, uint32_t prop);

/* x_actuator_set - Drives an actuator toward a position, 0..1 of its travel.
   index: actuator index.
   value: target position, 0..1 of the travel (clamped).
   The part moves at its authored rate; poll X_ACTUATOR_POSITION. A program that changes the target
   every tick keeps the part moving and the vehicle off rails.
   Rejected (program log, sys.rejected_requests) when: actuator index out of range or departed.
   Service 0x5033 SVC_X_ACTUATOR_SET, cost class Light. */
void x_actuator_set(uint32_t index, float value);
