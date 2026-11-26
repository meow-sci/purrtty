#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10
void t(void) { }
/* never closed
