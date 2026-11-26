/* Error after a tab, an astral character (2 UTF-16 units, 4 UTF-8 bytes) and a 2-byte character. */
#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10

void t(void)
{
	/* 🐱 é */ kfs_logf("ü"); oops_undeclared();
}
