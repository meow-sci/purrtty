/* Two errors on different lines plus warnings. */
#include <kfs.h>
#include <purrtty.h>
#pragma kfs tick_hz 10
#pragma kfs task blink rate_hz=10

uint32_t n;

void blink(void)
{
    n++;
    purr_light_count();
    if (n = 3u) kfs_logf("three");
    not_a_service(n);
    n = n + ;
}
