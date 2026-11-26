/* Point prograde: A bus example in full control (no stock flight computer).
   Two 10 Hz tasks in one file on the software bus:

     attitude_control - subscribes MSG_NAV_STATE and MSG_VEHICLE_PROPS and publishes
                        MSG_WRENCH_CMD (so it owns ATTITUDE_CONTROL)
     allocate - subscribes MSG_WRENCH_CMD and MSG_VEHICLE_PROPS, claims ALLOCATION.
   
   Declared in this order in one file, attitude_control runs first, so allocate sees 
   this tick's wrench.

   The target is prograde: the vehicle's nose, body +X (KSA's body frame is X
   forward, Y right, Z down), along its velocity relative to the parent body in
   CCI. Roll about the nose is left free and only damped. */
#include <kfs.h>
#include <math.h>
#include <vecmat.h>
#include <vehicle.h>

#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_NAV_STATE     nav
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs publish   MSG_WRENCH_CMD    wrench       /* owns ATTITUDE_CONTROL */
#pragma kfs task attitude_control rate_hz=10

kfs_msg_nav_state_t     nav;
kfs_msg_vehicle_props_t props;
kfs_msg_wrench_cmd_t    wrench;

vec3  prograde_BODY;     /* prograde in the body frame; watch these with kfs watch */
vec3  err_BODY_rad;      /* the rotation that takes the nose to prograde */
float point_err_rad;     /* the angle between the nose and prograde */

#define KP_PER_S2  0.4f    /* rad/s^2 per rad of pointing error */
#define KD_PER_S   1.2f    /* rad/s^2 per rad/s of rate */

void attitude_control(void)
{
    wrench.force_body_N = vec3_zero();
    wrench.throttle     = 0.0f;
    wrench.engines_on   = 0u;
    if (nav.hdr.seq == 0u) {    /* no navigation yet: command nothing */
        wrench.torque_body_Nm = vec3_zero();
        return;
    }

    /* Velocity into the body frame: q_body_to_cci takes BODY to CCI, so its
       inverse takes CCI to BODY. */
    vec3 v_CCI = vec3_from_d(nav.v_cci_mps);
    prograde_BODY = vec3_normalize(quat_rotate(quat_inverse(nav.q_body_to_cci), v_CCI));

    /* Rotating the nose about nose × target moves it toward the target, by the
       angle between them. */
    vec3  nose  = vec3_make(1.0f, 0.0f, 0.0f);
    vec3  axis  = vec3_cross(nose, prograde_BODY);
    float s     = vec3_length(axis);
    float c     = vec3_dot(nose, prograde_BODY);
    point_err_rad = atan2f(s, c);
    if (s > 1e-6f) {
        err_BODY_rad = vec3_scale(axis, point_err_rad / s);
    } else if (c < 0.0f) {
        /* Exactly retrograde: any axis across the nose will do. */
        err_BODY_rad = vec3_make(0.0f, point_err_rad, 0.0f);
    } else {
        err_BODY_rad = vec3_zero();
    }

    vec3 alpha = vec3_sub(vec3_scale(err_BODY_rad, KP_PER_S2),
                          vec3_scale(nav.rates_body_rps, KD_PER_S));
    wrench.torque_body_Nm = mat3_mul_vec3(props.I_body_kgm2, alpha);
}

/* The allocator: it reads attitude_control's wrench through its own
   subscription buffer, not the publication buffer. */
#pragma kfs claim ALLOCATION
#pragma kfs subscribe MSG_WRENCH_CMD wrench_in
#pragma kfs task allocate rate_hz=10

kfs_msg_wrench_cmd_t wrench_in;

void allocate(void)
{
    const float dt_s = sys.task_dt_s;    /* this task's period: 0.1 s at 10 Hz */
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
