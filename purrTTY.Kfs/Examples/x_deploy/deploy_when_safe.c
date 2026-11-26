/* Deploy the solar arrays once out of the atmosphere, retract them when back in.

   Solar arrays, like antennas, radiators and bay doors, are deployables:
   animations the part window shows with Deploy and Retract buttons. The
   property X_DEPLOY_LINKS tells them apart:
     bit 1  the animation unfolds a solar panel (the panel makes power only
            when it is deployed);
     bit 2  a parachute's own state machine drives it too (its bay doors):
            a program would fight it, so this one leaves such parts alone.

   The game says whether the vehicle is in the atmosphere in MSG_FLIGHT_DATA.
   On every change this program deploys (or retracts) every solar array and
   logs it. It does nothing in between, so the part window's buttons still
   work. An animation moves at its own speed: X_DEPLOY_STATE goes through
   X_STATE_DEPLOYING to X_STATE_DEPLOYED over X_DEPLOY_DURATION_S seconds. */
#include <kfs.h>
#include <vehicle.h>
#include <purrtty.h>

#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_FLIGHT_DATA flight
#pragma kfs task watch rate_hz=2

#define LINK_SOLAR      1u    /* X_DEPLOY_LINKS: drives a solar panel */
#define LINK_PARACHUTE  2u    /* X_DEPLOY_LINKS: a parachute drives it too */
#define MAX_DEPLOYABLES 64u   /* a bound the compiler can prove the loop by */

kfs_msg_flight_data_t flight;   /* altitude, in_atmosphere, ... (updated by the game) */

enum { UNKNOWN, INSIDE, OUTSIDE } where = UNKNOWN;
uint32_t arrays;                /* solar arrays commanded at the last change */

/* Deploys (1) or retracts (0) every solar array of the vehicle; returns how many. */
uint32_t drive_arrays(uint32_t deployed)
{
    uint32_t count = x_deploy_count();
    uint32_t n = 0u;
    for (uint32_t i = 0u; i < MAX_DEPLOYABLES; i++) {
        if (i >= count) break;
        if (x_deploy_geti(i, X_DEPLOY_PRESENT) == 0u) continue;   /* left the vehicle */
        uint32_t links = x_deploy_geti(i, X_DEPLOY_LINKS);
        if ((links & LINK_SOLAR) == 0u) continue;                 /* not a solar array */
        if ((links & LINK_PARACHUTE) != 0u) {
            kfs_logf("deploy_when_safe: deployable %u is also driven by a parachute: left alone", i);
            continue;
        }
        x_deploy_set(i, deployed);
        n++;
    }
    return n;
}

void watch(void)
{
    if (flight.hdr.seq == 0u) return;   /* no flight data yet */

    if (flight.in_atmosphere != 0u) {
        if (where != INSIDE) {
            arrays = drive_arrays(0u);
            kfs_logf("deploy_when_safe: in the atmosphere at %.1f km: retracting %u solar arrays",
                     flight.altitude_m / 1000.0f, arrays);
            where = INSIDE;
        }
    } else if (where != OUTSIDE) {
        arrays = drive_arrays(1u);
        kfs_logf("deploy_when_safe: out of the atmosphere at %.1f km: deploying %u solar arrays",
                 flight.altitude_m / 1000.0f, arrays);
        where = OUTSIDE;
    }
}
