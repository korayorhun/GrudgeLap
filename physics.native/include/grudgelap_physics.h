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

#define GLP_ABI_VERSION 6

typedef intptr_t glp_session; /* 0 is no session */
typedef intptr_t glp_engine;  /* 0 is no engine voice */

/* glp_create flags */
#define GLP_TRAFFIC 1 /* the drones of the original: five cars ahead, three oncoming */
#define GLP_SPEED_ROUTE 2 /* the attract-mode driver takes the speed route (past the level crossing), not the stunt route */

/* DriverInputs. gear: -1 reverse, 0 neutral, 1..7, or GLP_NO_GEAR_CHANGE to keep the current one. */
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
/* RouteKind */
enum { GLP_ROUTE_UNDECIDED, GLP_ROUTE_SPEED, GLP_ROUTE_STUNT };
/* SurfaceType */
enum { GLP_SURFACE_NONE, GLP_SURFACE_ROAD, GLP_SURFACE_GRASS, GLP_SURFACE_CHASM, GLP_SURFACE_PAD };
/* CrashReason */
enum { GLP_CRASH_NONE, GLP_CRASH_HARD_LANDING, GLP_CRASH_ROLLED_OVER, GLP_CRASH_FELL_OFF_BRIDGE, GLP_CRASH_HIT_TREE,
       GLP_CRASH_HIT_BUILDING, GLP_CRASH_HIT_TRAIN, GLP_CRASH_HIT_TRAFFIC, GLP_CRASH_HIT_COW };
/* RaceEvent: glp_state.events has bit (1 << event) set for each event of the last step. */
enum { GLP_EVENT_ENGINE_STARTED, GLP_EVENT_ENGINE_STALLED, GLP_EVENT_ROUTE_CHOSEN, GLP_EVENT_CHECKPOINT_PASSED,
       GLP_EVENT_LAP_COMPLETED, GLP_EVENT_NEW_BEST_LAP, GLP_EVENT_LEFT_ROAD, GLP_EVENT_OFF_ROAD_TIMEOUT,
       GLP_EVENT_WRONG_WAY, GLP_EVENT_CRASHED, GLP_EVENT_TIME_OUT, GLP_EVENT_COW_HIT, GLP_EVENT_SIGN_HIT,
       GLP_EVENT_KNOCKED, GLP_EVENT_COW_MOOED };
/* KnockKind: what the car knocked into (glp_state.knock_kind). */
enum { GLP_KNOCK_SOLID, GLP_KNOCK_DRONE, GLP_KNOCK_COW, GLP_KNOCK_LANDING };
/* PropKind */
enum { GLP_PROP_BARN, GLP_PROP_HOUSE, GLP_PROP_SHOP, GLP_PROP_HUT, GLP_PROP_TREE, GLP_PROP_COW, GLP_PROP_SIGN,
       GLP_PROP_SIGNPOST, GLP_PROP_TOWER, GLP_PROP_BARRIER, GLP_PROP_TRAIN, GLP_PROP_FLOWERS, GLP_PROP_FIELD,
       GLP_PROP_GATE, GLP_PROP_CROSSBUCK, GLP_PROP_PUMP };

/* A quaternion is x, y, z, w in simulation space throughout. */
typedef struct glp_state
{
    float position[3];        /* car's centre of gravity */
    float orientation[4];
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
    /* ABI 2 */
    int32_t car;              /* catalogue index */
    int32_t forward_gears;
    int32_t transmission;     /* GLP_AUTOMATIC.. */
    int32_t engine_running;
    float engine_torque;      /* N m at the crank, negative on the overrun */
    float lateral_acceleration;      /* m/s2, positive to the right */
    float longitudinal_acceleration; /* m/s2, positive forward */
    int32_t wheel_surface[4]; /* GLP_SURFACE_* */
    float wheel_slip_angle[4];       /* rad */
    float wheel_slip_ratio[4];
    int32_t route;            /* GLP_ROUTE_* */
    int32_t off_road;
    float off_road_timer;     /* s left before the car is put back */
    int32_t wrong_way;
    int32_t crash_count;
    int32_t last_crash;       /* GLP_CRASH_* */
    uint32_t events;          /* bit (1 << GLP_EVENT_*) per event of the last step */
    int32_t last_soft_hit;    /* prop index of the sign or cow hit in the last step, or -1 */
    int32_t grudge_match;     /* a best lap is loaded: its ghost races */
    int32_t ghost_visible;
    float ghost_position[3];
    float ghost_orientation[4];
    float ghost_steer;
    float replay_time;        /* s into the instant replay */
    float replay_seconds;     /* its length */
    float replay_position[3]; /* the recorded pose at replay_time */
    float replay_orientation[4];
    float replay_steer;
    float elapsed;            /* s since the session started */
    int32_t train_running;
    /* ABI 5 */
    float credit_best_lap;    /* s, the best lap of this credit alone (a loaded ghost's does not count); +inf before one */
    int32_t credit_best_route; /* GLP_ROUTE_* of that lap */
    /* ABI 6: knocks the car survives (below the crash speed it bounces off, dented) and the cows */
    float damage;             /* 0 sound .. 1 a write-off (a crash); repaired after the replay */
    int32_t knock_kind;       /* GLP_KNOCK_* of the last knock (GLP_EVENT_KNOCKED) */
    float knock_point[3];     /* where it struck, in the car's frame (x right, y up, z forward, from the centre of gravity) */
    float knock_normal[3];    /* the way the blow pushed in, car's frame: from the car towards what it hit */
    float knock_speed;        /* closing speed, m/s */
    int32_t knock_index;      /* prop, drone or cow index by knock_kind; -1 for a landing */
    int32_t cow_mooed;        /* index into glp_cows of a cow that mooed on its own in the last step (GLP_EVENT_COW_MOOED), or -1 */
} glp_state;

/* The catalogue's static facts about a car: what instruments and a car body need. */
typedef struct glp_car_spec
{
    int32_t style;            /* 0 flat-12, 1 V8, 2 hot hatch, 3 five-cylinder, 4 turbo hatch, 5 saloon, 6 estate, 7 fleet saloon (CarStyle) */
    int32_t forward_gears;
    float speedo_max_mph;
    float redline_rpm;
    float wheel_radius;       /* m */
    float half_track;         /* m, hub to centre line */
    float front_axle;         /* m, centre of gravity to the front hubs */
    float rear_axle;          /* m, centre of gravity to the rear hubs */
    float hub_height;         /* m, the hubs' rest plane below the centre of gravity */
    float suspension_rest;    /* m of travel at full droop */
    float cg_height;          /* m, the centre of gravity over the road at rest, with the static sag */
} glp_car_spec;

/* A moving body: a traffic car (colour: paint index), a car of the train (colour: 0 locomotive, 1 wagon) or a cow
 * (glp_cows; colour: the cow's prop index in glp_props, position on the ground under its middle, its head along +x). */
typedef struct glp_body
{
    float position[3];        /* traffic: centre of gravity; train: on the ground, mid-car */
    float orientation[4];
    float speed;              /* m/s */
    int32_t colour;
} glp_body;

typedef struct glp_prop
{
    int32_t kind;             /* GLP_PROP_* */
    float position[3];
    float yaw_degrees;
    float size[3];
    int32_t knocked;          /* a sign the car has tipped over */
} glp_prop;

int32_t glp_abi_version(void);
int32_t glp_car_count(void);
/* The car's name as UTF-8, like glp_message. */
int32_t glp_car_name(int32_t car, char* buffer, int32_t capacity);
int32_t glp_get_car_spec(int32_t car, glp_car_spec* out);
/* car: catalogue index; flags: GLP_TRAFFIC | GLP_SPEED_ROUTE or 0. Returns 0 on failure. */
glp_session glp_create(int32_t car, int32_t flags);
void glp_destroy(glp_session session);
int32_t glp_choose_transmission(glp_session session, int32_t mode);
/* Skips the instant replay, or puts the car back on the road while the off-road clock runs. */
int32_t glp_abort(glp_session session);
/* The attract-mode driver: the inputs it would give now (it turns the key, then drives the lap). */
int32_t glp_scripted_inputs(glp_session session, glp_inputs* out);
/* Advances the session by dt seconds (the vehicle integrates at 240 Hz inside). */
int32_t glp_step(glp_session session, const glp_inputs* inputs, float dt);
int32_t glp_get_state(glp_session session, glp_state* out);
/* The HUD message as UTF-8, cut to capacity - 1 bytes and terminated; returns its full length in bytes. */
int32_t glp_message(glp_session session, char* buffer, int32_t capacity);
/* These fill up to capacity entries and return how many there are (call with 0 to count). */
int32_t glp_traffic(glp_session session, glp_body* out, int32_t capacity);
int32_t glp_train(glp_session session, glp_body* out, int32_t capacity);
int32_t glp_props(glp_session session, glp_prop* out, int32_t capacity);
/* ABI 6: the cows as they stand now (they are shoved, stagger and trot off). */
int32_t glp_cows(glp_session session, glp_body* out, int32_t capacity);
/* The best lap (the ghost's recording) serialized; returns its size, 0 when there is none. */
int32_t glp_best_lap(glp_session session, uint8_t* buffer, int32_t capacity);
/* Installs a best lap from glp_best_lap's bytes before the start: its ghost races. */
int32_t glp_load_best_lap(glp_session session, const uint8_t* data, int32_t length);
/*
 * The engine sound (the repository's audio/GranularEngine): an engine rebuilt firing pulse by firing pulse from
 * recordings. folder (UTF-8) holds engine_drive.wav/.json and, optionally, engine_idle.wav/.json as
 * tools/make-engine-bank.py writes them; cylinders: 8 or 12. Returns 0 on failure. An engine voice is used from
 * one thread at a time (a host's audio thread) and is independent of sessions.
 */
glp_engine glp_engine_create(const char* folder, int32_t cylinders);
void glp_engine_destroy(glp_engine engine);
/* The rate the voice renders at, its recordings' (Hz). */
int32_t glp_engine_sample_rate(glp_engine engine);
/* Fills frames mono samples: the engine at rpm under load (0 closed throttle .. 1 full), gliding from the previous
 * call's values across the block. running 0 fades it out; interior non-zero hears it from the cabin. */
int32_t glp_engine_render(glp_engine engine, float* out, int32_t frames, float rpm, float load, int32_t running, int32_t interior, float volume);
/* The last failure on this thread, like glp_message. */
int32_t glp_last_error(char* buffer, int32_t capacity);

#ifdef __cplusplus
}
#endif
