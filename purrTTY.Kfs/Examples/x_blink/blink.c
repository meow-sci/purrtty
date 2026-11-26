/* Blink every light of the vehicle once a second.

   The first purrTTY example: it needs the purrTTY mod, whose <purrtty.h>
   declares the x_ services. Every light of the vehicle has an index, 0 to
   x_light_count() - 1, in a list frozen when the program starts. Type
   purr parts in the game console to see which index is which part.

   On and off are per part: the emitters of one part share its switch, so
   switching one switches the others. X_LIGHT_SWITCH_GROUP is the lowest
   index on the same switch, and this program switches only that one. Lights
   without a switch (interior fill lights) are left alone, and so is a light
   whose part has left the vehicle (staging, undocking).

   A switch command takes effect at the next frame: X_LIGHT_ON reads back the
   new state from then on. X_LIGHT_LIT (which also needs power) follows a
   switch-off at the same time, but a switch-on one frame later, once the
   game's physics step has run. */
#include <kfs.h>
#include <purrtty.h>

#pragma kfs tick_hz 10
#pragma kfs task blink rate_hz=10

#define HALF_PERIOD_TICKS 5u   /* 0.5 s on, 0.5 s off at 10 Hz: one blink a second */
#define MAX_LIGHTS        64u  /* a bound the compiler can prove the loop by */

uint32_t ticks;      /* ticks since the program started */
uint32_t on;         /* 1 while the lights are meant to be on */
uint32_t switches;   /* switch groups switched at the last change */

void blink(void)
{
    if (ticks % HALF_PERIOD_TICKS == 0u) {
        uint32_t count = x_light_count();
        on = on ^ 1u;
        switches = 0u;
        for (uint32_t i = 0u; i < MAX_LIGHTS; i++) {
            if (i >= count) break;
            if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) continue;      /* left the vehicle */
            if (x_light_geti(i, X_LIGHT_HAS_SWITCH) == 0u) continue;   /* nothing to switch */
            if (x_light_geti(i, X_LIGHT_SWITCH_GROUP) != i) continue;  /* switched with a lower index */
            x_light_set_on(i, on);
            switches++;
        }
        if (ticks == 0u) {
            kfs_logf("blink: %u lights, %u switch groups", count, switches);
        }
    }
    ticks++;
}
