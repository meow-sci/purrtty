/* Reproduces offline-harness E8: a service with a 3-cell result makes the kC lowering throw. */
#include <kfs.h>
#include <purrtty_crash.h>
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10

vec3 v;

void t(void) { v = purr_crash_vec(); }
