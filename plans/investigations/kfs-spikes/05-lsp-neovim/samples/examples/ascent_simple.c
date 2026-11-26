/* A simple ascent to orbit for the Rocket, from Earth's LC-39A launch site to 
   a 200 km circular orbit.

   The stock flight computer steers the rocket. This program tells it what to
   do, as a pilot would:
     1. LAUNCH:   Point straight up, full throttle, engines on.
     2. ASCENT:   Gravity turn to the east, MECO when apogee reaches 200 km,
                  drop the empty first stage.
     3. COAST:    Coast up to the apoapsis. Once out of the air, plan a
                  circularization burn and start it on the upper stage's engine.
     4. CIRCULARIZE: The flight computer flies the burn; wait for it to end.
     5. DONE:     In orbit. */
#include <kfs.h>
#include <vehicle.h>

#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_FLIGHT_DATA flight
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs subscribe MSG_BURN_STATUS burn_status
#pragma kfs task fly rate_hz=10

#define TARGET_ALTITUDE_M  (200.0f * KM)  /* the atmosphere ends at 167 km */
#define TURN_END_ALT_M     (100.0f * KM)  /* where the turn ends */
#define FINAL_PITCH_DEG    10.0f          /* pitch above the horizon after it */
#define EAST_DEG           90.0f          /* launching east borrows the Earth's spin */

kfs_msg_flight_data_t flight;       /* altitude, apoapsis, ... (updated by the game) */
kfs_msg_vehicle_props_t props;      /* mass, engines, ... */
kfs_msg_burn_status_t burn_status;  /* how the planned burn is going */

enum { LAUNCH, ASCENT, COAST, CIRCULARIZE, DONE } phase = LAUNCH;
kfs_burn_t circ_burn;
/* How the circularisation ended (KFS_BURN_DONE when it worked). */
uint32_t final_state;

void fly(void)
{
    switch (phase) {
    case LAUNCH:
        kfs_hold_heading(EAST_DEG * DEG2RAD, 90.0f * DEG2RAD, KFS_ROLL_UP);
        kfs_set_throttle(1.0f);
        kfs_engines_on();
        phase = ASCENT;
        break;

    case ASCENT:
        /* Gravity turn from 90 degrees to FINAL_PITCH_DEG at TURN_END_ALT_M. */
        if (flight.apoapsis_m < TARGET_ALTITUDE_M) {
            float pitch_deg = 90.0f - (90.0f - FINAL_PITCH_DEG)
                              * flight.altitude_m / TURN_END_ALT_M;
            if (pitch_deg < FINAL_PITCH_DEG) pitch_deg = FINAL_PITCH_DEG;
            kfs_hold_heading(EAST_DEG * DEG2RAD, pitch_deg * DEG2RAD, KFS_ROLL_UP);
            break;
        }
        /* MECO: Drop the first stage (the decoupler), stage again to activate
           the upper stage's engine, and point along the direction of travel. */
        kfs_engines_off();
        kfs_stage();
        kfs_stage();
        kfs_hold_direction(KFS_VLF_PROGRADE, KFS_ROLL_FREE);
        kfs_logf("MECO: altitude %.1f km, apoapsis %.1f km",
                 flight.altitude_m / KM, flight.apoapsis_m / KM);
        phase = COAST;
        break;

    case COAST:
        /* Wait until we're exo-atmosphere to plan circularization (drag would
           spoil the plan), and only when the upper engine is active and has
           propellant. A newly-activated engine reports its propellant a moment
           later, and a burn started before then ends at once. */
        if (flight.in_atmosphere) break;
        for (int32_t i = 0; i < ENGINE_COUNT; ++i) {
            if (props.engine[i].active != 0u && props.engine[i].propellant_ok != 0u) {
                circ_burn = kfs_plan_circularize(KFS_AT_APOAPSIS);
                kfs_burn_start(circ_burn);
                kfs_logf("circularization planned: apoapsis %.1f km in %.0f s",
                         flight.apoapsis_m / KM, flight.time_to_apoapsis_s);
                phase = CIRCULARIZE;
                break;
            }
        }
        break;

    case CIRCULARIZE:
        if (burn_status.burn == circ_burn
            && (burn_status.state == KFS_BURN_DONE || burn_status.state == KFS_BURN_ABORTED)) {
            final_state = burn_status.state;
            kfs_logf("circularization ended (state %u): apoapsis %.1f km, periapsis %.1f km", final_state,
                     flight.apoapsis_m / KM, flight.periapsis_m / KM);
            phase = DONE;
        }
        break;

    case DONE:
        break;
    }
}
