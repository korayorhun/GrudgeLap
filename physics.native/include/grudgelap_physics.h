/*
 * C interface of GrudgeLap.Physics.Native.dll, the simulation core (vehicle dynamics, track, race rules) built
 * with NativeAOT. The layouts below mirror physics.native/NativeTypes.cs field for field; change both together
 * and bump GLP_ABI_VERSION, which glp_abi_version() returns so a caller can refuse a mismatched library.
 *
 * Coordinates are the simulation's: metres, +X right, +Y up, +Z forward. Godot shows them as (x, y, -z), Unreal
 * as (x, -z, y) * 100 cm. A session is used from one thread at a time; sessions are independent.
 * Functions return 0 on success and -1 on failure (glp_last_error describes it), unless noted.
 */
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define GLP_ABI_VERSION 1

typedef intptr_t glp_session; /* 0 is no session */

/* DriverInputs. gear: -1 reverse, 0 neutral, 1..4, or GLP_NO_GEAR_CHANGE to keep the current one. */
#define GLP_NO_GEAR_CHANGE 99
typedef struct glp_inputs
{
    float steer;      /* -1 left .. 1 right */
    float throttle;   /* 0..1 */
    float brake;      /* 0..1 */
    float clutch;     /* 0..1, 1 = pedal down */
    int32_t gear;
    int32_t ignition; /* non-zero: turning the key */
    int32_t shift_up;
    int32_t shift_down;
} glp_inputs;

/* SessionPhase */
enum { GLP_PHASE_SELECT_TRANSMISSION, GLP_PHASE_START_ENGINE, GLP_PHASE_DRIVING, GLP_PHASE_REPLAY, GLP_PHASE_FINISHED };
/* TransmissionMode */
enum { GLP_AUTOMATIC, GLP_MANUAL_AUTO_CLUTCH, GLP_MANUAL_CLUTCH };

typedef struct glp_state
{
    float position[3];        /* car's centre of gravity */
    float orientation[4];     /* quaternion x, y, z, w */
    float velocity[3];
    float speed;              /* m/s along the car's forward axis */
    float steer_angle;        /* rad of the front wheels, positive to the right */
    float rpm;
    int32_t gear;
    int32_t phase;
    int32_t lap;
    int32_t on_road;
    int32_t airborne;
    float time_remaining;     /* s */
    float lap_time;           /* s */
    float best_lap_time;      /* s, +inf before the first lap */
    int64_t score;
    float wheel_spin[4];      /* rad, accumulated; front left, front right, rear left, rear right */
    float wheel_compression[4]; /* m of suspension travel used; 0 when the wheel is in the air */
    int32_t wheel_contact[4];
    float hub_height;         /* m, the wheel hubs' rest plane below the centre of gravity (VehicleSpec.HubHeight) */
    float suspension_rest;    /* m of travel at full droop (VehicleSpec.SuspensionRest) */
} glp_state;

int32_t glp_abi_version(void);
/* car: 0 flat-12, 1 V8. Traffic is left out: the caller has no cars to show for it yet. Returns 0 on failure. */
glp_session glp_create(int32_t car);
void glp_destroy(glp_session session);
int32_t glp_choose_transmission(glp_session session, int32_t mode);
/* The attract-mode driver: the inputs it would give now (it turns the key, then drives the lap). */
int32_t glp_scripted_inputs(glp_session session, glp_inputs* out);
/* Advances the session by dt seconds (the vehicle integrates at 240 Hz inside). */
int32_t glp_step(glp_session session, const glp_inputs* inputs, float dt);
int32_t glp_get_state(glp_session session, glp_state* out);
/* The HUD message as UTF-8, cut to capacity - 1 bytes and terminated; returns its full length in bytes. */
int32_t glp_message(glp_session session, char* buffer, int32_t capacity);
/* The last failure on this thread, like glp_message. */
int32_t glp_last_error(char* buffer, int32_t capacity);

#ifdef __cplusplus
}
#endif
