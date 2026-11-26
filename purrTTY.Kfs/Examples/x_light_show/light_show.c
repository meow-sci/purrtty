/* A light show on the vehicle's spot lights.

   Three effects run together on every spot light:
     colour     a colour wheel, one turn every 6.4 s, each light an eighth of
                a turn further round it than the one before;
     cone       the outer cone half-angle breathes between CONE_MIN_RAD and
                CONE_MAX_RAD every 4 s, the inner cone at half of it;
     intensity  a ramp from half to twice the part's own intensity
                (X_LIGHT_STOCK_INTENSITY), starting again every 5 s.
   Point lights have no cone and are left alone, so are lights whose part has
   left the vehicle. Switching lights on is up to you (or to x_blink): a
   light that is off shows nothing.

   Appearance changes are per light: the first one gives the light a private
   copy of its part's look, so other lights of the same part type keep
   theirs. They are not saved; x_light_reset puts the stock look back.

   To end the show, set the global stop to 1 (kfs poke stop 1, or the debug
   window): every light is reset, once. kfs poke stop 0 starts it again.
   The look outlives the program: after kfs stop, a trap or another program,
   the lights keep their last colour, cone and intensity until you type
   purr lights reset in the game console (or use the purrTTY menu's kFS >
   Reset light overrides). */
#include <kfs.h>
#include <math.h>
#include <purrtty.h>

#pragma kfs tick_hz 10
#pragma kfs task show rate_hz=10

#define TICK_S          0.1f        /* one tick at 10 Hz */
#define WHEEL_TICKS     64u         /* one turn of the colour wheel: 6.4 s */
#define WHEEL_STEP      8u          /* the next light is an eighth of a turn on */
#define BREATH_PERIOD_S 4.0f
#define RAMP_TICKS      50u         /* the intensity ramp: 5 s */
#define CONE_MIN_RAD    0.15f
#define CONE_MAX_RAD    0.60f
#define TWO_PI          6.2831853f
/* Six service calls per spot light and tick: 32 lights stay well under the
   256 calls a tick may make. */
#define MAX_LIGHTS      32u

uint32_t stop;        /* 1 ends the show and resets every light */
uint32_t was_reset;   /* 1 once the lights were reset for this stop */
uint32_t ticks;       /* ticks of show so far */
uint32_t spots;       /* spot lights driven in the last tick */

float clamp01(float x)
{
    if (x < 0.0f) return 0.0f;
    if (x > 1.0f) return 1.0f;
    return x;
}

/* The fully saturated colour of a hue in [0, 1): red, yellow, green, cyan,
   blue, magenta and back to red. */
vec3 hue_rgb(float hue)
{
    float h = 6.0f * hue;
    vec3 c;
    c.x = clamp01(fabsf(h - 3.0f) - 1.0f);
    c.y = clamp01(2.0f - fabsf(h - 2.0f));
    c.z = clamp01(2.0f - fabsf(h - 4.0f));
    return c;
}

/* Puts the stock look back on every light this vehicle still has. */
void reset_all(void)
{
    uint32_t count = x_light_count();
    for (uint32_t i = 0u; i < MAX_LIGHTS; i++) {
        if (i >= count) break;
        if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) continue;
        x_light_reset(i);
    }
    kfs_logf("light show: stopped, %u lights reset", count);
}

void show(void)
{
    if (stop != 0u) {
        if (was_reset == 0u) {
            reset_all();
            was_reset = 1u;
        }
        return;
    }
    was_reset = 0u;

    float t = (float)ticks * TICK_S;
    float breath = 0.5f - 0.5f * cosf(TWO_PI * t / BREATH_PERIOD_S);    /* 0 .. 1 .. 0 */
    float outer = CONE_MIN_RAD + (CONE_MAX_RAD - CONE_MIN_RAD) * breath;
    float ramp = (float)(ticks % RAMP_TICKS) / (float)RAMP_TICKS;        /* 0 .. 1, then again */
    float scale = 0.5f + 1.5f * ramp;                                    /* half .. twice stock */

    uint32_t count = x_light_count();
    spots = 0u;
    for (uint32_t i = 0u; i < MAX_LIGHTS; i++) {
        if (i >= count) break;
        if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) continue;
        if (x_light_geti(i, X_LIGHT_TYPE) != X_LIGHT_TYPE_SPOT) continue;
        float hue = (float)((ticks + WHEEL_STEP * i) % WHEEL_TICKS) / (float)WHEEL_TICKS;
        vec3 rgb = hue_rgb(hue);
        x_light_set_color(i, rgb);
        x_light_set_cone(i, 0.5f * outer, outer);
        x_light_set_intensity(i, scale * x_light_getf(i, X_LIGHT_STOCK_INTENSITY));
        spots++;
    }
    if (ticks == 0u) {
        kfs_logf("light show: %u lights, %u spot lights", count, spots);
    }
    ticks++;
}
