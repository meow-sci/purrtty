/* Sweep actuator 0 up and down: a triangle wave, with read-back.

   Actuators are animations the part window shows as a 0..1 slider (bay
   doors, ramps, robotic parts). x_actuator_set says where the part should
   be; the part then moves there at its own speed, and X_ACTUATOR_POSITION
   reads where it is now. Type purr parts in the game console to see which
   index is which part, and change ACTUATOR below.

   The target climbs from 0 to 1 and back down, one step every tick. Each way
   takes HALF_PERIOD_S, or twice the part's own travel time when that is
   longer, so the part can keep up. At each turn the program logs the target
   and the position it reads back: the difference is how far the part lags
   (two ticks at least: a command takes effect at the next frame, and the
   part moves in the physics step of the frame after).

   A target that changes every tick keeps the part, and so the vehicle, from
   coming to rest, which limits time warp. kfs stop ends the sweep. */
#include <kfs.h>
#include <purrtty.h>

#pragma kfs tick_hz 10
#pragma kfs task sweep rate_hz=10

#define ACTUATOR       0u
#define TICK_HZ        10.0f
#define HALF_PERIOD_S  4.0f
#define MAX_HALF_TICKS 6000u   /* ten minutes each way at most */

uint32_t half_ticks;   /* ticks from the bottom to the top; 0 before the first tick */
uint32_t step;         /* where in the wave: 0 .. 2 * half_ticks - 1 */
uint32_t turns;        /* turns so far */
uint32_t stopped;      /* 1 once there is nothing to sweep */
float target;          /* this tick's target, 0..1 */
float position;        /* the position read back this tick, 0..1 */

void sweep(void)
{
    if (stopped != 0u) return;

    if (half_ticks == 0u) {
        if (x_actuator_count() <= ACTUATOR) {
            kfs_logf("actuator_sweep: this vehicle has no actuator %u", ACTUATOR);
            stopped = 1u;
            return;
        }
        float half_s = 2.0f * x_actuator_getf(ACTUATOR, X_ACTUATOR_DURATION_S);
        if (half_s < HALF_PERIOD_S) half_s = HALF_PERIOD_S;
        half_ticks = (uint32_t)(half_s * TICK_HZ + 0.5f);
        if (half_ticks > MAX_HALF_TICKS) half_ticks = MAX_HALF_TICKS;
        kfs_logf("actuator_sweep: actuator %u, %.1f s each way", ACTUATOR, (float)half_ticks / TICK_HZ);
    }
    if (x_actuator_geti(ACTUATOR, X_ACTUATOR_PRESENT) == 0u) {
        kfs_logf("actuator_sweep: actuator %u has left the vehicle", ACTUATOR);
        stopped = 1u;
        return;
    }

    position = x_actuator_getf(ACTUATOR, X_ACTUATOR_POSITION);
    if (step < half_ticks) {
        target = (float)step / (float)half_ticks;
    } else {
        target = (float)(2u * half_ticks - step) / (float)half_ticks;
    }

    if (step == half_ticks) {
        turns++;
        kfs_logf("actuator_sweep: turn %u at the top: target %.2f, position %.3f", turns, target, position);
    } else if (step == 0u && turns > 0u) {
        turns++;
        kfs_logf("actuator_sweep: turn %u at the bottom: target %.2f, position %.3f", turns, target, position);
    }

    x_actuator_set(ACTUATOR, target);
    step++;
    if (step >= 2u * half_ticks) step = 0u;
}
