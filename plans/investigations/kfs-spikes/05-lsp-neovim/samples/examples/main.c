/* Step 1 of 2: a hot kC function. dot3 runs 20 times a tick; profile it with
   kfs prof and note dot3's cycles per call. Step 2 is e2e04_kasm/: the same
   program with dot3 in kASM. */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

float va[3] = { 1.0f, 2.0f, 3.0f };
float vb[3] = { 4.0f, 5.0f, 6.0f };
float out;

float dot3(const float *a, const float *b) {
    float s = 0.0f;
    int32_t i;
    for (i = 0; i < 3; i++)
        s += a[i] * b[i];
    return s;
}

void step(void) {
    float acc = 0.0f;
    int32_t k;
    for (k = 0; k < 20; k++)
        acc += dot3(va, vb);
    out = acc;
}
