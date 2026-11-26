/* A macro that breaks a prototype inside the system header <kfs.h>. */
#define kfs_halt 42
#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10

void t(void) { }
