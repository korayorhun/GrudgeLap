# Feel checklist

What "drives like the cabinet" means in numbers. Every line is pinned by a test in `physics.tests` (the sound's lines in `audio.tests`), so a
tuning change that breaks one shows up before anyone sits in the car. Values are the current baseline; since 0.18 the handling as a proving ground measures it (0-100 km/h, 100-0 km/h dry and
wet with and without ABS, understeer and roll on a skid pad, lift-off oversteer, lift at speed, for all eight cars) is
pinned in `HandlingTests` against real-car ranges, and the figures there are the reference.

| Behaviour | Target | Current | Test |
|---|---|---|---|
| 0-60 mph, flat-twelve, stick with assisted clutch | 4-7.5 s | 5.1 s from idle (0.19 launch assist) | `Accelerates_to_60_mph_in_a_sports_car_time` |
| Top speed in 4th, flat-twelve | 135-175 mph | 163 mph (0.18) | `Top_speed_lands_in_the_150_mph_class` |
| 0-60 mph, V8 (traction control, seven gears) | 3-5 s | 3.5 s (0.19: launch control, dual-clutch handover) | `V8_launches_and_tops_out_like_a_modern_supercar` |
| Top speed in 7th, V8 | 180-215 mph | 201 mph @ 8800 rpm | same |
| 60-0 mph braking | 30-70 m | 42 m on locked wheels (0.18: a locked tyre keeps about three quarters of its peak; with ABS, see HandlingTests) | `Brakes_from_60_mph_in_a_reasonable_distance` |
| Dump the clutch at idle (stick + clutch) | engine stalls, car barely moves | stalls in 0.17 s, rolls 1.2 m | `Dumping_the_clutch_at_idle_stalls_the_engine` |
| Feed the clutch in with revs | launches | > 15 mph after 5 s | `Clutch_fed_in_with_revs_launches_without_stalling` |
| Vertical loop (R 15 m, 30 m tall) | ~60 mph minimum | 68 mph completes, 42 mph falls at 25 m | `Vertical_loop_needs_about_60_mph` |
| Open drawbridge (30 m ramp @ 7°, 18 m gap, 34 m landing) | 60-80 mph window | 62 and 76 land; 48 hits the edge; 90 slams the flat road | `Drawbridge_wants_a_60_to_80_mph_run_up` |
| Banked 180 (R 85 m, 32°) | holds 90 mph | holds, 1.4 m max lane error | `Banked_180_holds_90_mph` |
| Full lock at 95 mph | rear breaks away | 83° rear slip (0.19) | `Full_lock_at_high_speed_spins_the_car` |
| Grass | like mud | 5x the coasting loss of asphalt | `Grass_is_like_mud` |
| Braking in gear over a jump | clutch slips, engine keeps running | min 2390 rpm over bridge and overpass | `Engine_keeps_running_over_jumps_in_top_gear` |
| Rules: scripted lap, stunt route (2.5 km), empty road | checkpoint, lap bonus, no crash | about 91 s, 0 crashes (0.18) | `Scripted_driver_completes_a_lap` |
| Rules: scripted lap, speed route (3.4 km), empty road | same | 104 s, 0 crashes (0.19) | `Scripted_driver_completes_a_lap` |
| Scripted laps at 20, 30, 90 and 144 fps (one driver call per frame, as in the game) | both routes, no crash, no run-off, no lap counted at the fork | 90 s and 102 s laps at every rate | `Scripted_driver_completes_a_lap_at_any_frame_rate` |
| Scripted lap, speed route, 8 drones both ways | lap completed, at most one crash | 111 s, 0 crashes, follows and overtakes (0.19) | `Scripted_driver_threads_the_traffic_on_the_speed_route` |
| A car without the pull for the loop (the 1.4 estate) | told before the fork, attract driver takes the speed route | warned, speed lap, 0 crashes (0.19) | `A_car_that_cannot_make_the_loop_is_told_and_takes_the_speed_route` |
| 0-100 km/h, the three hatchbacks: 205 GTi 1.9 / Delta Integrale Evoluzione / Golf R32 (cars3) | a tenth either side of 7.8 / 5.7 / 6.6 s | 8.36 / 6.27 / 6.62 s (the Delta at the top of its 6.3 s bound) | `Sprints_and_stops_within_the_real_cars_ranges` |
| 0-100 km/h and 100-0 km/h, the fleet saloon: E 250 CDI, six-speed manual, rear-wheel drive (fleet) | a tenth either side of 7.7 s and of 36.4 m (the E 250 CGI Coupe's stop) | 7.87 s, 37.7 m (rain 53.1 m) | `Sprints_and_stops_within_the_real_cars_ranges` |
| 0-60 mph and top speed, the fleet saloon (fleet) | 6.6-8.1 s from idle; 144-154 mph (240 km/h published) | 7.83 s; 152 mph in 6th at 4040 rpm | `Everyday_cars_match_their_published_figures` |
| Steady pull, four-wheel drive | the Delta about its epicyclic's 47 % in front; the R32 front-drive | 51 % / 100 % (cars3) | `A_steady_pull_splits_the_torque_as_the_centre_differential_does` |
| Fronts on grass, the rears on asphalt, the same torque in (clutch held part-way) | the coupling hands the drive to the rear, which grips, and the car gains more | Delta: rear 59 % (open centre 53 %), fronts 0.26 m/s ahead (8.0), 2.50 m/s gained (2.26); R32: rear 59 % (none), 0.35 m/s ahead (14.3), 2.55 m/s (0.99) (cars3) | `The_centre_coupling_hands_the_drive_to_the_axle_that_grips` |
| Launch assist, R32 with its fronts on grass | the Haldex closes and the rear drives | 3.38 m/s after 0.7 s, rear 48 % (open: 1.36 m/s) (cars3) | `The_launch_assist_counts_on_the_Haldex` |
| Haldex under braking | open | passes nothing (cars3) | `The_Haldex_coupling_opens_under_braking` |
| Split grip from rest, the V8's brake steer, the R32's EDS, the fleet saloon's ETS | the wheel on the grass braked, the car pulls away; the brake never hunting at the step rate | after 2.5 s: V8 12.0 m/s (open: 3.1), R32 13.4 (12.2) (dyn5), fleet 6.74 (5.19) (fleet: rear-driven since; the Egea's BLD 6.3 against 4.2) | `A_wheel_spinning_on_split_grip_is_braked_and_the_car_pulls_away` |
| Full throttle out of a 22 m bend | the inside driven wheel held | V8 0.67 m/s ahead (1.41 with its traction control alone) (dyn5); fleet's inside rear 0.93 (1.36) and 3.37 m/s gained (3.27; the test asks 2.5 % more) (fleet review: 3.38 against 3.23 while it had a petrol limiter) | `The_inside_wheel_out_of_a_tight_bend_is_held` |
| The fleet saloon's diesel held flat out with nothing to pull, free and in gear | it settles on its governor: no limiter cut, no stutter | 4755 rpm free on 22 % of its fuel, steady within 5 rpm, never a cut; the estate's petrol four cuts as before (fleet review) | `The_diesel_settles_on_its_governor_without_a_cut` |
| The R32's EDS window; the driver's brake | in below 40 km/h, out by 80; no function brakes under the pedal | brakes a spinning front wheel at 30 km/h, not at 45; from rest last at 18 km/h (dyn5) | `EDS_works_at_low_speed_only_and_no_function_brakes_under_the_pedal` |
| The V8 from rest with both rear tyres on the road, dry and wet | the launch the car makes without brake steer: nothing to hold, nothing braked | 15.686 m/s after 2 s dry, 9.861 wet, the same without the function; 0 N m (dyn5 review: 15.47 and 619 N m before) | `On_even_grip_the_launch_is_the_one_without_the_brake` |
| Fall off the loop, set down at the last return point | the run-up to make it from rest | 57-65 mph at the loop for the saloon, fleet, hot hatch and flat-twelve (0.19) | `A_car_set_down_after_falling_off_the_loop_has_the_run_up_to_make_it` |
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
| Engine loudness between cars (sound, `audio.tests`) | within 1.5 LU of the cars' mean in a full-throttle run-up from 40 to 95 % of the redline, outside and in the cabin | within 1.2 LU, the V8 the loudest (0.20; 0.19 in attract: estate 5.5 LU over the flat-twelve) | `Every_engine_is_about_as_loud_pulling_hard` |
| Engine peaks (sound) | -1 dBFS or under at full throttle from 30 % of the redline to the redline at the game's volume, outside and in the cabin; at most 1 sample in 1,000 rounded off | -1.0 dBFS at worst (V8, turbo hatch, fleet saloon outside), at most 5 in 10,000 rounded (0.20; before the review +2.8 dBFS) | `Full_throttle_peaks_under_minus_one_dbfs` |
| Engine at top speed (sound) | at most 4 LU under the roar and the wind at 250 km/h, its loudest third-octave 4 dB over them | 2.2 LU under at worst (fleet saloon's cabin), 5.3 dB (hot hatch's cabin) | `The_engine_stays_audible_over_the_roar_and_the_wind_at_top_speed` |
| Rev limiter (sound) | a stutter: 6 dB or more, falling within 10 ms; through the simulated engine the held load stays at 0.9 or more | 6.6-13.6 dB in 4-10 ms, load 1.0 (0.20) | `The_limiter_cuts_crisply`, `The_simulated_limiter_holds_the_load_through_its_cuts` |
| Turbo (sound) | whistle at the blades' passing frequency; flutter at 6-15 Hz without a valve; 12 LU under the engine on boost, yet its whine level with the engine in its own band | 5.2-8.8 kHz as it spools, 9.8 Hz surge, 14 LU under, whine +0.7 dB outside and +0.1 in the cabin | `TurboNoiseTests` |

Known gaps (next tuning passes): loop entry still spikes the suspension (a clothoid entry would soften it);
the keyboard steering rates are a first guess; the scripted driver corners at 68 % of grip, a human will push harder.

Why these numbers: the 1989 cabinet's own figures are documented only loosely (operator's manual timers,
the Lynx manual's "at least 60 mph for the loop", reviews of the bridge window); the rest is tuned to a
late-80s flat-twelve sports car, a 2010s V8, the published figures of the hatchbacks and everyday cars the game
draws (design.md, vehicle dynamics audit, items 18-20), and to what felt right. See `research-notes.md`.
