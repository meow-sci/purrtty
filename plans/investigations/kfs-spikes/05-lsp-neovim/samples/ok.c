/* A valid kC program using a stock service and the two demo purrTTY services. */
#include <kfs.h>
#include <purrtty.h>
#pragma kfs tick_hz 10
#pragma kfs task blink rate_hz=10

uint32_t n;

void blink(void)
{
    n++;
    if (n == 2u) kfs_logf("lights: %u", purr_light_count());
    if (n % 2u == 0u) purr_light_set_on(0u, (n / 2u) % 2u);
}
