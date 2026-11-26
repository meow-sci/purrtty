/* Attitude hold: two 50 Hz tasks in one file on the software bus.
   attitude_control subscribes MSG_NAV_STATE and MSG_VEHICLE_PROPS, publishes
   MSG_WRENCH_CMD (so it owns ATTITUDE_CONTROL) and holds the attitude init read
   from MSG_NAV_STATE on the first boot tick; allocate subscribes MSG_WRENCH_CMD
   and MSG_VEHICLE_PROPS and claims ALLOCATION, with a crude allocator. Declared
   in this order in one file, attitude_control runs first, so allocate sees this
   tick's wrench. */
#include <kfs.h>
#include <vecmat.h>
#include <vehicle.h>

#pragma kfs init capture
#pragma kfs subscribe MSG_NAV_STATE     nav
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs publish   MSG_WRENCH_CMD    wrench       /* owns ATTITUDE_CONTROL */
#pragma kfs task attitude_control rate_hz=50

kfs_msg_nav_state_t     nav;
kfs_msg_vehicle_props_t props;
kfs_msg_wrench_cmd_t    wrench;

quat q_hold;             /* the attitude to hold (BODY to CCI): the first seen */
uint32_t hold_seq;       /* the seq of that navigation message (0: none yet) */
vec3 err_BODY_rad;       /* watch these with kfs watch */

#define KP_PER_S2  0.4f    /* rad/s^2 per rad of attitude error */
#define KD_PER_S   1.2f    /* rad/s^2 per rad/s of rate */

void capture(void)
{
    q_hold = nav.q_body_to_cci;
    hold_seq = nav.hdr.seq;
}

void attitude_control(void)
{
    /* No navigation on the boot tick: hold the first one we get. */
    if (hold_seq == 0u && nav.hdr.seq != 0u) {
        q_hold = nav.q_body_to_cci;
        hold_seq = nav.hdr.seq;
    }
    err_BODY_rad = quat_attitude_error(q_hold, nav.q_body_to_cci);
    vec3 alpha = vec3_sub(vec3_scale(err_BODY_rad, KP_PER_S2),
                          vec3_scale(nav.rates_body_rps, KD_PER_S));
    wrench.torque_body_Nm = mat3_mul_vec3(props.I_body_kgm2, alpha);
    wrench.force_body_N   = vec3_zero();
    wrench.throttle       = 0.0f;
    wrench.engines_on     = 0u;
}

/* The allocator: it reads attitude_control's wrench through its own
   subscription buffer, not the publication buffer. */
#pragma kfs claim ALLOCATION
#pragma kfs subscribe MSG_WRENCH_CMD wrench_in
#pragma kfs task allocate rate_hz=50

kfs_msg_wrench_cmd_t wrench_in;

void allocate(void)
{
    const float dt_s = sys.task_dt_s;    /* this task's period: 0.02 s at 50 Hz */
    for (int i = 0; i < THRUSTER_COUNT; i++) {
        vec3 tq = props.thruster[i].torque_body_Nm;
        float along = vec3_dot(tq, wrench_in.torque_body_Nm);
        float on_s = (along > 0.0f)
            ? fminf(along / vec3_dot(tq, tq), 1.0f) * dt_s
            : 0.0f;
        /* This allocator drops sub-minimum pulses. Left alone, the platform
           rounds them up. */
        commands.thruster[i].pulse_s =
            (on_s >= props.thruster[i].min_pulse_s) ? on_s : 0.0f;
    }
}
