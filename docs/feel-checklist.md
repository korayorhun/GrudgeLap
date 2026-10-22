# Feel checklist

What "drives like the cabinet" means in numbers. Every line is pinned by a test in `physics.tests`, so a
tuning change that breaks one shows up before anyone sits in the car. Values are the current baseline.

| Behaviour | Target | Current | Test |
|---|---|---|---|
| 0-60 mph, flat-twelve, stick with assisted clutch | 4-7.5 s | 5.6 s | `Accelerates_to_60_mph_in_a_sports_car_time` |
| Top speed in 4th, flat-twelve | 135-175 mph | 162 mph @ 6900 rpm | `Top_speed_lands_in_the_150_mph_class` |
| 0-60 mph, V8 (traction control, seven gears) | 3-5 s | 4.5 s | `V8_launches_and_tops_out_like_a_modern_supercar` |
| Top speed in 7th, V8 | 180-215 mph | 201 mph @ 8800 rpm | same |
| 60-0 mph braking | 30-70 m | 35 m | `Brakes_from_60_mph_in_a_reasonable_distance` |
| Dump the clutch at idle (stick + clutch) | engine stalls, car barely moves | stalls in 0.17 s, rolls 1.2 m | `Dumping_the_clutch_at_idle_stalls_the_engine` |
| Feed the clutch in with revs | launches | > 15 mph after 5 s | `Clutch_fed_in_with_revs_launches_without_stalling` |
| Vertical loop (R 15 m, 30 m tall) | ~60 mph minimum | 68 mph completes, 42 mph falls at 25 m | `Vertical_loop_needs_about_60_mph` |
| Open drawbridge (30 m ramp @ 7°, 18 m gap, 34 m landing) | 60-80 mph window | 62 and 76 land; 48 hits the edge; 90 slams the flat road | `Drawbridge_wants_a_60_to_80_mph_run_up` |
| Banked 180 (R 85 m, 32°) | holds 90 mph | holds, 1.4 m max lane error | `Banked_180_holds_90_mph` |
| Full lock at 95 mph | rear breaks away | 28° rear slip | `Full_lock_at_high_speed_spins_the_car` |
| Grass | like mud | 5x the coasting loss of asphalt | `Grass_is_like_mud` |
| Braking in gear over a jump | clutch slips, engine keeps running | min 2390 rpm over bridge and overpass | `Engine_keeps_running_over_jumps_in_top_gear` |
| Rules: scripted lap, stunt route (3.0 km), empty road | checkpoint, lap bonus, no crash | 104 s, 0 crashes | `Scripted_driver_completes_a_lap` |
| Rules: scripted lap, speed route (3.2 km), empty road | same | 104 s, 0 crashes | `Scripted_driver_completes_a_lap` |
| Scripted lap, speed route, 8 drones both ways | lap completed, at most one crash | 0 crashes, follows and overtakes | `Scripted_driver_threads_the_traffic_on_the_speed_route` |
| Drones | keep their lane, never enter the stunt route | 300 s, every step | `Drones_keep_their_lane_and_never_enter_the_stunt_route` |
| Ram a drone | crash → replay → return point clear of traffic | yes | `Hitting_a_drone_crashes_and_replays` |
| Hit a barn | crash → replay → key turn at last return point | yes | `Driving_into_a_barn_crashes_and_returns_to_the_last_point` |
| Best lap | recorded at 20 Hz, replayed as the ghost, survives a save/load | yes | `Best_lap_is_recorded_and_its_ghost_drives_the_next_credit` |
| Skid pad | asphalt, no off-road clock, no wrong-way | 25 s of donuts | `Skid_pad_is_asphalt_with_no_off_road_clock_and_no_wrong_way` |
| Ground | meets the road edges, bridges float, dips cut in, rolls elsewhere | holds | `Ground_meets_the_road_edges_and_rolls_elsewhere` |
| Roadside props and the pad | no solid prop within 2 m of any road; pad 15 m clear of roads | holds for every prop | `Solid_props_stay_clear_of_every_road`, `Skid_pad_sits_clear_of_roads_and_props` |
| Fields | every field patch clear of every road by half a road width plus 6 m | holds | `Fields_stay_clear_of_every_road` |
| Train | crosses the speed route on its schedule, pauses, comes back; the scripted driver still laps | holds | `Train_runs_across_the_speed_route_on_a_schedule` |
| Soft props | a cow moos and a sign tips over, no crash; the train at the crossing is a crash | holds | `Train_at_the_crossing_is_a_crash_and_signs_and_cows_are_not` |

Known gaps (next tuning passes): loop entry still spikes the suspension (a clothoid entry would soften it);
the keyboard steering rates are a first guess; the scripted driver corners at 68 % of grip, a human will push harder.

Why these numbers: the 1989 cabinet's own figures are documented only loosely (operator's manual timers,
the Lynx manual's "at least 60 mph for the loop", reviews of the bridge window); the rest is tuned to a
late-80s flat-twelve sports car, a 2010s V8, and to what felt right. See `research-notes.md`.
