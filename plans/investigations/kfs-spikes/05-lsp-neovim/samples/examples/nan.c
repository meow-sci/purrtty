/* NaN watch example: level falls by 0.01 a tick, and from the 101st tick the
   square root's argument is negative, so root and then filtered become NaN. The
   NaN watch names the line and the tick, and lists the globals that went NaN.
   */
#include <kfs.h>
#include <math.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

uint32_t count;
float level = 1.0f;
float root;
float filtered;

void step(void) {
    count++;
    level = 1.0f - 0.01f * (float)count;
    root = sqrtf(level);
    filtered = 0.9f * filtered + 0.1f * root;
}
