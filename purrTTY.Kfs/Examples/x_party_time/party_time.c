/* Party time: the light show, with the light parts moving.

   Run it on the controlled vehicle from the game console:
     kfs on
     kfs load x_party_time/party_time.c --vehicle
     kfs start

   Lights, each refreshed ten times a second (half of them on each tick):
     colour     a colour wheel, one turn every 4 s; each light starts at its
                own place on the wheel (49/80 of a turn after the one before),
                so the lights show different colours at the same moment;
     cone       spot lights only: the outer cone half-angle breathes between
                CONE_MIN_RAD and CONE_MAX_RAD every 3 s, the inner at half;
     intensity  a pulse between half and twice the part's own intensity
                (X_LIGHT_STOCK_INTENSITY), once a second.
   The breath and the pulse are offset per light as the colour is. Lights
   with a switch are switched on at the start. Point lights get colour and
   intensity only.

   Actuators move only now and then: x_actuator_set says where the part
   should go, and the game animates it there at the part's own speed. An
   actuator is driven when its part also carries a light (X_ACTUATOR_PART_ID
   equals a light's X_LIGHT_PART_ID); when no actuator does, every actuator
   is driven. The log says which. Each driven actuator has its own schedule:
     - its first move comes after a start delay: the driven actuators, in a
       shuffled order, get one slice each of the first 2.5 s
       (START_SPREAD_TICKS) and a random tick inside it, so no two start in
       the same tick;
     - a move goes to a random target in 0..1 at least MIN_STEP (0.25) away
       from the last one;
     - the next move waits for this one to end, plus a random dwell of 0.2 to
       1.5 s (DWELL_MIN_S, DWELL_MAX_S), so the actuators drift out of step.
       A move is assumed to take its share of the part's full travel time,
       X_ACTUATOR_DURATION_S (0.5 of the travel of a 2 s part: 1 s), or
       ASSUMED_MOVE_S (1 s) when the part reports no duration, plus LAG_S
       (0.1 s) for the command to reach the part.
   An actuator is never commanded on two ticks running, and one whose part
   has left the vehicle is dropped. The first 32 lights and 32 actuators
   take part (MAX_LIGHTS, MAX_ACTUATORS); the log says when there are more.

   Knobs, read every tick (kfs poke <name> <value>, or the debug window):
     stop           1 ends the party: every light is reset, once, and the
                    actuators stay where they are; 0 starts it again.
     all_actuators  1 drives every actuator, on a light part or not.
     seed           the random numbers. Another value starts new schedules;
                    the same value gives the same party.
   kfs start (and kfs reset) puts every global back to its default, so poke
   a knob after the start, or change its default below.

   The look outlives the program: after kfs stop, a trap or another program,
   the lights keep their last colour, cone and intensity until you type
   purr lights reset in the game console (or use the purrTTY menu's kFS >
   Reset light overrides).

   Service calls per tick, at most 132 of the 256 a tick may make. The
   first tick reads 4 properties of up to 32 lights, plus a count, the
   switch and two log lines (132); the next ticks read the actuators, 8 a
   tick (33 calls and a line), and a tick of its own chooses the ones to
   drive. A party tick makes at most 4 calls for each of 16 lights, 2 for
   each of 32 actuators and a log line (129). At most 80 of them are
   commands: a frame that catches up a second of ticks at time warp (the
   game skips older ones) queues at most 21 x 80 = 1680, inside the 4096
   the extension holds between frames. */
#include <kfs.h>
#include <math.h>
#include <purrtty.h>

#pragma kfs tick_hz 20
#pragma kfs task party rate_hz=20

#define TICK_HZ         20.0f
#define LIGHT_SLICES    2u          /* light i is refreshed when tick % 2 == i % 2: 10 Hz */
#define WHEEL_TICKS     80u         /* one turn of the colour wheel: 4 s */
#define PHASE_STEP      49u         /* the next light is 49/80 (about 0.618) of a turn on */
#define BREATH_TICKS    60u         /* the cone breathes every 3 s */
#define PULSE_TICKS     20u         /* the intensity pulses once a second */
#define CONE_MIN_RAD    0.15f
#define CONE_MAX_RAD    0.70f

#define START_SPREAD_TICKS 50u      /* first moves spread over 2.5 s */
#define MIN_STEP        0.25f       /* a new target is at least this far from the last */
#define ASSUMED_MOVE_S  1.0f        /* a move, when the part reports no duration */
#define MAX_MOVE_S      60.0f       /* the longest a move is waited for */
#define LAG_S           0.1f        /* a command moves the part from the second frame on */
#define DWELL_MIN_S     0.2f
#define DWELL_MAX_S     1.5f
#define REPORT_TICKS    200u        /* a progress line every 10 s */

#define MAX_LIGHTS      32u
#define MAX_ACTUATORS   32u
#define SCAN_CHUNK      8u          /* actuators scanned per tick */

#define KIND_NONE       0u          /* not present at the scan, or gone since */
#define KIND_SPOT       1u
#define KIND_POINT      2u

uint32_t stop;              /* 1 ends the party and resets every light */
uint32_t all_actuators;     /* 1 drives every actuator */
uint32_t seed = 2026u;      /* the random numbers */

uint32_t tick;              /* ticks since the start */
uint32_t phase;             /* 0 scan the lights, 1 scan the actuators, 2 choose, 3 party */
uint32_t scanned;           /* actuators scanned so far */
uint32_t was_reset;         /* 1 once the lights were reset for this stop */
uint32_t lights;            /* lights taking part: the first MAX_LIGHTS */
uint32_t spots;             /* spot lights among them */
uint32_t actuators;         /* actuators known: the first MAX_ACTUATORS */
uint32_t on_light_part;     /* actuators whose part carries a light */
uint32_t driven;            /* actuators driven now */
uint32_t moves;             /* moves commanded so far */
uint32_t seed_used;         /* the seed of the schedules */
uint32_t mode_used;         /* all_actuators when the actuators were chosen */
uint32_t rng;               /* the shared random stream (start order) */

uint32_t light_kind[MAX_LIGHTS];
uint32_t light_part[MAX_LIGHTS];
float light_stock[MAX_LIGHTS];
uint32_t light_offset[MAX_LIGHTS];    /* its place on the wheel, in ticks */
float light_own[MAX_LIGHTS];          /* the same, as a fraction of a turn */

uint32_t act_part[MAX_ACTUATORS];
uint32_t act_matched[MAX_ACTUATORS];  /* 1: its part carries a light */
uint32_t act_gone[MAX_ACTUATORS];     /* 1: not on the vehicle */
uint32_t act_driven[MAX_ACTUATORS];
float act_travel_s[MAX_ACTUATORS];    /* full 0..1 travel time; 0 when not reported */
float act_target[MAX_ACTUATORS];      /* the last target */
uint32_t act_next[MAX_ACTUATORS];     /* the tick of the next move */
uint32_t act_rng[MAX_ACTUATORS];      /* each actuator's own random stream */
uint32_t act_moves[MAX_ACTUATORS];
uint32_t order[MAX_ACTUATORS];        /* the driven actuators, shuffled */

float clamp01(float x)
{
    if (x < 0.0f) return 0.0f;
    if (x > 1.0f) return 1.0f;
    return x;
}

/* 0 at 0, rising smoothly to 1 at 0.5 and back to 0 at 1: a smoothstep of a
   triangle, close to 0.5 - 0.5 cos(2 pi x) at a fraction of the cost. */
float wave(float x)
{
    float t = 2.0f * x;
    if (x >= 0.5f) t = 2.0f - 2.0f * x;
    return t * t * (3.0f - 2.0f * t);
}

/* The fully saturated colour of a hue in [0, 1). */
vec3 hue_rgb(float hue)
{
    float h = 6.0f * hue;
    vec3 c;
    c.x = clamp01(fabsf(h - 3.0f) - 1.0f);
    c.y = clamp01(2.0f - fabsf(h - 2.0f));
    c.z = clamp01(2.0f - fabsf(h - 4.0f));
    return c;
}

/* ------------------------------------------------------------ random numbers */

/* A 32-bit integer hash (lowbias32): spreads a seed over every bit. */
uint32_t mix32(uint32_t x)
{
    x ^= x >> 16;
    x *= 0x7FEB352Du;
    x ^= x >> 15;
    x *= 0x846CA68Bu;
    x ^= x >> 16;
    return x;
}

/* One step of Marsaglia's xorshift32; never 0 from a state that is not 0. */
uint32_t xorshift32(uint32_t x)
{
    x ^= x << 13;
    x ^= x >> 17;
    x ^= x << 5;
    return x;
}

/* The top 24 bits as a float in [0, 1). */
float unit(uint32_t x)
{
    return (float)(x >> 8) * (1.0f / 16777216.0f);
}

/* The start of stream `salt` for the current seed; never 0. */
uint32_t stream(uint32_t salt)
{
    uint32_t x = mix32(seed ^ mix32(salt + 1u));
    if (x == 0u) x = 1u;
    return x;
}

float shared_random(void)
{
    rng = xorshift32(rng);
    return unit(rng);
}

float act_random(uint32_t i)
{
    uint32_t x = xorshift32(act_rng[i]);
    act_rng[i] = x;
    return unit(x);
}

/* ------------------------------------------------------------ lights */

void scan_lights(void)
{
    uint32_t count = x_light_count();
    lights = count;
    if (lights > MAX_LIGHTS) lights = MAX_LIGHTS;
    spots = 0u;
    for (uint32_t i = 0u; i < MAX_LIGHTS; i++) {
        if (i >= lights) break;
        light_kind[i] = KIND_NONE;
        light_offset[i] = (PHASE_STEP * i) % WHEEL_TICKS;
        light_own[i] = (float)light_offset[i] * (1.0f / (float)WHEEL_TICKS);
        if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) continue;
        light_part[i] = x_light_geti(i, X_LIGHT_PART_ID);
        light_stock[i] = x_light_getf(i, X_LIGHT_STOCK_INTENSITY);
        if (x_light_geti(i, X_LIGHT_TYPE) == X_LIGHT_TYPE_SPOT) {
            light_kind[i] = KIND_SPOT;
            spots++;
        } else {
            light_kind[i] = KIND_POINT;
        }
    }
    x_lights_set_all(1u);
    kfs_logf("party_time: %u lights, %u spot lights", count, spots);
    if (count > MAX_LIGHTS) kfs_logf("party_time: only the first %u lights take part", MAX_LIGHTS);
}

/* Half of the lights each tick: colour, cone (spot lights) and intensity. */
void update_lights(void)
{
    float breath_at = (float)(tick % BREATH_TICKS) * (1.0f / (float)BREATH_TICKS);
    float pulse_at = (float)(tick % PULSE_TICKS) * (1.0f / (float)PULSE_TICKS);
    for (uint32_t i = tick % LIGHT_SLICES; i < MAX_LIGHTS; i += LIGHT_SLICES) {
        if (i >= lights) break;
        if (light_kind[i] == KIND_NONE) continue;
        if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) {
            light_kind[i] = KIND_NONE;      /* its part has left: skipped from now on */
            continue;
        }
        float hue = (float)((tick + light_offset[i]) % WHEEL_TICKS) * (1.0f / (float)WHEEL_TICKS);
        x_light_set_color(i, hue_rgb(hue));
        if (light_kind[i] == KIND_SPOT) {
            float b = breath_at + light_own[i];
            if (b >= 1.0f) b = b - 1.0f;
            float outer = CONE_MIN_RAD + (CONE_MAX_RAD - CONE_MIN_RAD) * wave(b);
            x_light_set_cone(i, 0.5f * outer, outer);
        }
        float p = pulse_at + light_own[i];
        if (p >= 1.0f) p = p - 1.0f;
        x_light_set_intensity(i, (0.5f + 1.5f * wave(p)) * light_stock[i]);   /* half .. twice stock */
    }
}

/* Puts the stock look back on every light this vehicle still has. */
void reset_all(void)
{
    uint32_t count = x_light_count();
    uint32_t reset = 0u;
    for (uint32_t i = 0u; i < MAX_LIGHTS; i++) {
        if (i >= count) break;
        if (x_light_geti(i, X_LIGHT_PRESENT) == 0u) continue;
        x_light_reset(i);
        reset++;
    }
    kfs_logf("party_time: stopped, %u lights reset; the actuators stay where they are", reset);
}

/* ------------------------------------------------------------ actuators */

/* Reads the next SCAN_CHUNK actuators; returns 1 once every one is read. A
   part is matched against every light, so the scan is spread over ticks. */
uint32_t scan_actuators(void)
{
    uint32_t count = x_actuator_count();
    actuators = count;
    if (actuators > MAX_ACTUATORS) actuators = MAX_ACTUATORS;
    if (scanned == 0u) on_light_part = 0u;
    for (uint32_t n = 0u; n < SCAN_CHUNK; n++) {
        uint32_t i = scanned;
        if (i >= actuators) break;
        scanned++;
        act_matched[i] = 0u;
        act_gone[i] = 0u;
        if (x_actuator_geti(i, X_ACTUATOR_PRESENT) == 0u) {
            act_gone[i] = 1u;
            continue;
        }
        act_part[i] = x_actuator_geti(i, X_ACTUATOR_PART_ID);
        act_travel_s[i] = x_actuator_getf(i, X_ACTUATOR_DURATION_S);
        act_target[i] = x_actuator_getf(i, X_ACTUATOR_TARGET);
        for (uint32_t j = 0u; j < MAX_LIGHTS; j++) {
            if (j >= lights) break;
            if (light_kind[j] != KIND_NONE && light_part[j] == act_part[i]) {
                act_matched[i] = 1u;
                break;
            }
        }
        if (act_matched[i] != 0u) on_light_part++;
    }
    if (scanned < actuators) return 0u;
    if (count > MAX_ACTUATORS) kfs_logf("party_time: only the first %u actuators take part", MAX_ACTUATORS);
    return 1u;
}

/* Gives every driven actuator its first move: the driven actuators in a
   shuffled order, the k-th at a random tick of the k-th slice of the start
   spread, so no two start in the same tick. */
void schedule_starts(void)
{
    driven = 0u;
    for (uint32_t i = 0u; i < MAX_ACTUATORS; i++) {
        if (i >= actuators) break;
        if (act_driven[i] == 0u) continue;
        order[driven] = i;
        driven++;
    }
    if (driven == 0u) return;
    for (uint32_t k = MAX_ACTUATORS - 1u; k > 0u; k--) {      /* Fisher-Yates */
        if (k >= driven) continue;
        uint32_t j = (uint32_t)(shared_random() * (float)(k + 1u));
        if (j > k) j = k;
        uint32_t swap = order[k];
        order[k] = order[j];
        order[j] = swap;
    }
    uint32_t width = START_SPREAD_TICKS / driven;              /* at least 1: driven <= 32 */
    for (uint32_t k = 0u; k < MAX_ACTUATORS; k++) {
        if (k >= driven) break;
        uint32_t jitter = (uint32_t)(shared_random() * (float)width);
        if (jitter >= width) jitter = width - 1u;
        act_next[order[k]] = tick + 1u + k * width + jitter;
    }
}

/* Chooses the actuators to drive, seeds every random stream from seed and
   schedules the first moves. */
void choose_actuators(void)
{
    uint32_t every = 0u;
    if (all_actuators != 0u || on_light_part == 0u) every = 1u;
    for (uint32_t i = 0u; i < MAX_ACTUATORS; i++) {
        if (i >= actuators) break;
        act_rng[i] = stream(i);
        act_driven[i] = 0u;
        if (act_gone[i] != 0u) continue;
        if (every != 0u || act_matched[i] != 0u) act_driven[i] = 1u;
    }
    rng = stream(MAX_ACTUATORS);
    seed_used = seed;
    mode_used = all_actuators;
    schedule_starts();
    if (actuators == 0u) {
        kfs_logf("party_time: no actuators: nothing to actuate, lights only");
    } else if (all_actuators != 0u) {
        kfs_logf("party_time: all_actuators: driving all %u actuators (seed %u)", driven, seed);
    } else if (on_light_part == 0u) {
        kfs_logf("party_time: no actuator is on a light part: driving all %u actuators (seed %u)", driven, seed);
    } else {
        kfs_logf("party_time: driving the %u actuators on light parts, of %u (seed %u)", driven, actuators, seed);
    }
}

/* One move of actuator i: a new target, and the tick of the next move. */
void move(uint32_t i)
{
    if (x_actuator_geti(i, X_ACTUATOR_PRESENT) == 0u) {
        act_gone[i] = 1u;
        act_driven[i] = 0u;
        driven--;
        kfs_logf("party_time: actuator %u has left the vehicle", i);
        return;
    }
    float last = act_target[i];
    float below = last - MIN_STEP;          /* room in [0, last - MIN_STEP] */
    if (below < 0.0f) below = 0.0f;
    float above = 1.0f - last - MIN_STEP;   /* room in [last + MIN_STEP, 1] */
    if (above < 0.0f) above = 0.0f;
    float u = act_random(i) * (below + above);
    float target = u;
    if (u >= below) target = last + MIN_STEP + (u - below);
    target = clamp01(target);
    x_actuator_set(i, target);

    float move_s = ASSUMED_MOVE_S;
    if (act_travel_s[i] > 0.0f) move_s = fabsf(target - last) * act_travel_s[i];
    if (move_s > MAX_MOVE_S) move_s = MAX_MOVE_S;
    float wait_s = LAG_S + move_s + DWELL_MIN_S + (DWELL_MAX_S - DWELL_MIN_S) * act_random(i);
    act_next[i] = tick + 1u + (uint32_t)(wait_s * TICK_HZ);
    act_target[i] = target;
    act_moves[i]++;
    moves++;
}

void move_due_actuators(void)
{
    for (uint32_t i = 0u; i < MAX_ACTUATORS; i++) {
        if (i >= actuators) break;
        if (act_driven[i] == 0u) continue;
        if (tick < act_next[i]) continue;
        move(i);
    }
}

/* ------------------------------------------------------------ the task */

void party(void)
{
    if (stop != 0u) {
        if (was_reset == 0u) {
            reset_all();
            was_reset = 1u;
        }
        tick++;
        return;
    }
    if (was_reset != 0u) {
        was_reset = 0u;
        kfs_logf("party_time: resumed");
        if (phase == 3u) {
            schedule_starts();                  /* the actuators start again, staggered */
            tick++;
            return;
        }
    }

    if (phase == 0u) {
        scan_lights();
        phase = 1u;
    } else if (phase == 1u) {
        if (scan_actuators() != 0u) phase = 2u;
    } else if (phase == 2u || seed != seed_used || all_actuators != mode_used) {
        choose_actuators();                     /* a tick of its own: it costs the most cycles */
        phase = 3u;
    } else {
        update_lights();
        move_due_actuators();
        if (tick % REPORT_TICKS == 0u) {
            kfs_logf("party_time: %u s, %u moves by %u actuators", tick / 20u, moves, driven);
        }
    }
    tick++;
}
