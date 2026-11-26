/* all_stock.c: owns no block, so the computer runs in pilot mode and the game's
   own flight computer flies the vehicle. Load it with kfs load
   ex_stock/all_stock.c --vehicle and kfs start; the UI's attitude and burn
   settings work as without kFS. */
#pragma kfs tick_hz 10
#pragma kfs task idle rate_hz=10
void idle(void) { }
