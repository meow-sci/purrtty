/* Oscilloscope example: a damped oscillator integrated at 50 Hz, restarted
   every 10 s. Watch x and v with kfs watch x and kfs watch v, or the editor's
   scope pane (Watch). */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50
#define DT 0.02f
#define OMEGA 3.0f
#define ZETA 0.05f

float x = 1.0f;
float v = 0.0f;
uint32_t ticks;

void step(void) {
    float a = -2.0f * ZETA * OMEGA * v - OMEGA * OMEGA * x;
    v += a * DT;
    x += v * DT;
    ticks++;
    if (ticks % 500u == 0u) {
        x = 1.0f;
        v = 0.0f;
    }
}
