/* Profiler example: double arithmetic is soft on the Mk2 (K32F), so __muldf3
   and __adddf3 show up as children of the function that ran them, and the
   line's cost names them. kfs prof, or the editor's profiler pane. On the Mk3
   (K32D) the same lines are hardware: try Preview. */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

double pos = 0.0;
double vel = 1.0;
float pos_f;

static double integrate(double p, double v, double dt) {
    return p + v * dt;
}

void step(void) {
    pos = integrate(pos, vel, 0.02);
    pos_f = (float)pos;
}
