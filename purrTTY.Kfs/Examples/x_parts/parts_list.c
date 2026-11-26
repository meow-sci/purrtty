/* Which index is which part: the index tables in the program log.

   x_parts_describe writes a table of the vehicle's lights, deployables and
   actuators to the program log: index, part, and what each one is doing.
   These are the lists every x_ service addresses. They are frozen when the
   program starts and change only when the program asks: after docking,
   undocking or staging, X_PARTS_DIRTY reads 1 (the vehicle's parts no longer
   match the lists) and x_parts_rescan() rebuilds them at the next frame,
   which changes X_PARTS_GENERATION. Indices may change then; a part that left
   keeps its slot until the rescan, and its X_..._PRESENT reads 0.

   This program describes the vehicle once, then checks once a second: when
   the vehicle has changed it rescans, waits for the new generation and
   describes it again. purr parts in the game console shows the same lists. */
#include <kfs.h>
#include <purrtty.h>

#pragma kfs tick_hz 10
#pragma kfs task check rate_hz=1

uint32_t generation;   /* the generation described last; 0 before the first */
uint32_t rescanning;   /* 1 between x_parts_rescan() and the new generation */
uint32_t rescans;      /* rescans so far */

void describe(void)
{
    generation = x_parts_info(X_PARTS_GENERATION);
    kfs_logf("parts_list: generation %u: %u lights, %u deployables, %u actuators", generation,
             x_parts_info(X_PARTS_LIGHTS), x_parts_info(X_PARTS_DEPLOYABLES),
             x_parts_info(X_PARTS_ACTUATORS));
    x_parts_describe(X_KIND_ALL);   /* at most one table a second */
}

void check(void)
{
    if (generation == 0u) {
        describe();
        return;
    }
    if (rescanning != 0u) {
        if (x_parts_info(X_PARTS_GENERATION) != generation) {   /* the lists were rebuilt */
            rescanning = 0u;
            describe();
        }
        return;
    }
    if (x_parts_info(X_PARTS_DIRTY) != 0u) {
        kfs_logf("parts_list: the vehicle has changed; rescanning");
        x_parts_rescan();
        rescanning = 1u;
        rescans++;
    }
}
