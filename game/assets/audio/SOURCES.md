# Recordings

## Engines (`flat12/engine_*`, `v8/engine_*`, `hothatch/engine_*`, `turbohatch/engine_*`, `v6/engine_*`)

Each car has two grain banks: a recording of the engine at idle and one running up through a gear, cut into
its firing pulses by `tools/make-engine-bank.py` (a 16-bit mono WAV at 44.1 kHz and, beside it, a JSON file
with the position and the firing frequency of every pulse). The game's engine voice (`audio/GranularEngine.cs`,
played by Godot's `SampledEngine` and by the Unreal version through the simulation core's DLL) lays one pulse
of a bank per firing period of the simulated revs, taken from the pulses recorded at the nearest revs, and
overlap-adds them; nothing loops and nothing is pitch-shifted.

Since banks2 (3 November 2026, 0.27.0) every bank of these five cars is cut from a recording whose maker describes
the car, the engine and the recording: seven Wikimedia Commons uploads of YouTube videos' soundtracks and two Freesound
recordings. The Internet Archive item `car-engines` the first banks were cut from (below, on its provenance) is used by
no bank of the game any more.

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz [@ harmonic followed]) |
|---|---|---|
| `v8/engine_idle` | Freesound #585215 "Car Engine: Maserati GranTurismo Switch to Sport Mode and Launch" by myeclecticself, https://freesound.org/s/585215/ (CC0 1.0) | 0.5@4.8@56@40@80 |
| `v8/engine_drive` | Wikimedia Commons "McLaren 570S with Fi Exhaust system - engine and exhaust sound (YouTube dBRIVYIzRm4).opus", the audio track of FiExhaust's video "McLaren 570S w/ Full Catless Fi EXHAUST X One Belief X BBT Tuning" (a 570S's M838TE 3.8 twin-turbocharged V8, the engine of the car the game's V8 is drawn as, on a catless Fi Exhaust system and a BBT tune, revved and driven at a tuning shop), https://commons.wikimedia.org/wiki/File:McLaren_570S_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_(YouTube_dBRIVYIzRm4).opus (CC BY 3.0, credit FiExhaust), its two full-throttle pulls levelled over half-second windows; and for the top, past the 570S's 8,500 rpm limiter, Wikimedia Commons "Ferrari 458 Speciale - engine and exhaust sound (YouTube nltPXKAN6BI).opus", the audio track of TvdL Films' video "Ferrari 458 Speciale with F1 Exhaust FLAMES & Revs" (a 458 Speciale's F136 FL 4.5 flat-plane V8 revved standing to its 9,000 rpm limiter at Top Marques Monaco 2017), https://commons.wikimedia.org/wiki/File:Ferrari_458_Speciale_-_engine_and_exhaust_sound_(YouTube_nltPXKAN6BI).opus (CC BY 3.0, credit TvdL Films) | the 570S's first pull, 64.0-68.4 s, in 9 half-second stretches (3,200-8,560 rpm), its second, 81.8-83.2 s, in 5 stretches of 0.3 s (3,910-8,150), and the Speciale held against its limiter, 17.75-20.25 s, in 10 quarter-second stretches (7,500-9,210) |
| `v8/engine_overrun` | Wikimedia Commons "McLaren 540C with Fi Exhaust system - engine and exhaust sound (YouTube CGrl3VILOGc).opus", the audio track of FiExhaust's video "McLaren 540C Catless DownPipes Fi EXHAUST sound X StanceTech" (a 540C, the same M838TE in its 540 PS tune, on Fi Exhaust catless downpipes, revved standing), https://commons.wikimedia.org/wiki/File:McLaren_540C_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_(YouTube_CGrl3VILOGc).opus (CC BY 3.0, credit FiExhaust) | the revs falling after two blips of the throttle, 43.3-44.0 s (6,710 to 4,060 rpm) and 47.3-48.0 s (6,430 to 3,860), each in two stretches |
| `flat12/engine_idle` | Freesound #112075 "Lambo idle and rev.mp3" by cheesepuff, https://freesound.org/s/112075/ (CC0 1.0) | 4.0@16.3@121@98@128 |
| `flat12/engine_drive` | Wikimedia Commons "Ferrari F12 - engine and exhaust sound (YouTube JNvonF5LGYQ).opus", the audio track of Supercars1989's video "Ferrari F12 TDF LOVELY SOUND - Start up, accelerations and hard revs!" (an F12tdf's F140 FC 6.3 V12 accelerating hard on the road, filmed from outside), https://commons.wikimedia.org/wiki/File:Ferrari_F12_-_engine_and_exhaust_sound_(YouTube_JNvonF5LGYQ).opus (CC BY 3.0, credit Supercars1989), the pull levelled over half-second windows | 100.5-104.0 s in 7 half-second stretches, 2,350-5,690 rpm |
| `flat12/engine_overrun` | the same track, the throttle lifted at the top of that pull | 104.25-105.25 s in four quarter-second stretches, 5,800 to 4,540 rpm |
| `hothatch/engine_idle` | Freesound #639227 "Engine Start Idle Rev Shut Off (From Outside Hood Closed) on 1990 Volvo 240 DL" by j_soundeffects, https://freesound.org/s/639227/ (CC BY 4.0): a 240's B230F 2.3 eight-valve four idling, heard outside the car with the bonnet closed; its floor taken out (`clean-recording.py`, below) | 4.0-13.0 s in two stretches, the second harmonic followed over 25-33 Hz (750-990 rpm; below) |
| `hothatch/engine_drive` | Wikimedia Commons "Alfa Romeo Spider - engine and exhaust sound (YouTube ZQQRnxRINac).opus", the audio track of Luciano's Classics' video "1982 Alfa Romeo Spider 2000 Exhaust Sound" (a Spider 2000's 1,962 cc twin-cam eight-valve four revved standing, from behind the exhaust), https://commons.wikimedia.org/wiki/File:Alfa_Romeo_Spider_-_engine_and_exhaust_sound_(YouTube_ZQQRnxRINac).opus (CC BY 3.0 as Commons tags it, CC BY 4.0 on YouTube; credit Luciano's Classics; below), two blips of the throttle levelled over quarter-second windows | the blips from 15.6 s (2,620-4,760 rpm) and 20.35 s (2,920-6,780) in stretches of 0.15 s |
| `hothatch/engine_overrun` | the same track, the revs falling after the two blips | 16.35-17.85 s (3,990 to 2,030 rpm) in quarter-second stretches, 21.4-24.9 s (5,970 to 1,500) in half-second ones |
| `turbohatch/engine_idle` | Wikimedia Commons "Fiat Coupé Turbo - engine and exhaust sound (YouTube S1IzD5dTWng).opus", the audio track of Witu Tech (SimsonSBL)'s video "Fiat Coupe Turbo 2.0 16vt 0-200km/h Blowoff! Acceleration!" (a Coupé 2.0 16v Turbo, the Delta Integrale's engine family: Fiat's two-litre sixteen-valve turbo four), https://commons.wikimedia.org/wiki/File:Fiat_Coupé_Turbo_-_engine_and_exhaust_sound_(YouTube_S1IzD5dTWng).opus (CC BY 3.0, credit Witu Tech (SimsonSBL)), its floor taken out | 17.0-21.5 s in two stretches, the second harmonic followed over 27-33 Hz (850-990 rpm) |
| `turbohatch/engine_drive` | Wikimedia Commons "Audi S1 - engine and exhaust sound (YouTube 2axudw2mPHk).opus", the audio track of Calvin MotorSport's video "Audi S1 Acceleration 0 to 240 Km/h" (an S1's 2.0 TFSI turbocharged four, 231 PS, through its gears), https://commons.wikimedia.org/wiki/File:Audi_S1_-_engine_and_exhaust_sound_(YouTube_2axudw2mPHk).opus (CC BY 3.0, credit Calvin MotorSport); tracked on the fourth crank order, twice the firing, the strongest line of the recording (the harmonic field at 2) | first gear 3.5-6.8 s (4,390-5,700 rpm), third 14.0-19.4 (5,480-6,660) and fourth 20.0-28.0 (5,080-6,800), in half-second stretches |
| `turbohatch/engine_overrun` | the Coupé's track again, the throttle lifted at the end of its run, its floor taken out and low-passed at 4 kHz | 104.3-107.4 s in half-second stretches, 6,500 to 1,840 rpm |
| `v6/engine_idle` | Freesound #754775 "Sports Car Accelerating/Speeding Up Fast - Exhaust" by EvanBoyerman, https://freesound.org/s/754775/ (CC BY 4.0): a 3.2 V6 VTEC (a Honda J32, in an Acura) recorded with a lavalier microphone just above the exhaust, idling after the drive; its floor taken out | 83.2-86.2 s in one-second stretches, the second harmonic followed (770-900 rpm) |
| `v6/engine_drive` | the same recording, driving fast through two gears, levelled over half-second windows (0.6-11.5 s) | 2.3-8.8 s (1,570-4,130 rpm) and 9.3-11.3 s (4,370-5,160) in half-second stretches |
| `v6/engine_overrun` | the same recording, the long coast after the drive, the throttle shut | 58.0-80.5 s in one-second stretches, 2,150 to 870 rpm |

The overrun banks play in place of the run-up as the throttle closes (`GranularEngine`): off the throttle a real
engine stops firing and what is left is the exhaust's rasp, which no filter on the run-up gave.

The files are used as cut. Since 0.20 the voice evens a bank when it loads it (`GrainBank`: each segment tilted and
levelled onto the bank's trend across the revs, so the joins between recordings do not step) and trims each car to
one loudness (`GranularEngine`, BS.1770); since soundfix it also levels every grain of the run-up and the overrun onto
the run-up's trend, against the drift of level within a recording (`GrainBank.LevelGains`), and since banks the idle's
too when the engine pulls, so that from the idle's revs to the run-up's the voice follows one trend. Nothing is written back.

Rebuild a bank (the cylinder count turns firing frequency into rpm: rpm = Hz x 120 / cylinders). The Wikimedia Commons
files are saved by their videos' YouTube ids (`dBRIVYIzRm4.opus` from
https://upload.wikimedia.org/wikipedia/commons/1/1d/McLaren_570S_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_%28YouTube_dBRIVYIzRm4%29.opus,
`CGrl3VILOGc.opus` from
https://upload.wikimedia.org/wikipedia/commons/5/5e/McLaren_540C_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_%28YouTube_CGrl3VILOGc%29.opus,
`nltPXKAN6BI.opus` from
https://upload.wikimedia.org/wikipedia/commons/b/bd/Ferrari_458_Speciale_-_engine_and_exhaust_sound_%28YouTube_nltPXKAN6BI%29.opus,
`JNvonF5LGYQ.opus` from
https://upload.wikimedia.org/wikipedia/commons/a/ab/Ferrari_F12_-_engine_and_exhaust_sound_%28YouTube_JNvonF5LGYQ%29.opus,
`ZQQRnxRINac.opus` from
https://upload.wikimedia.org/wikipedia/commons/5/5e/Alfa_Romeo_Spider_-_engine_and_exhaust_sound_%28YouTube_ZQQRnxRINac%29.opus,
`2axudw2mPHk.opus` from
https://upload.wikimedia.org/wikipedia/commons/6/6e/Audi_S1_-_engine_and_exhaust_sound_%28YouTube_2axudw2mPHk%29.opus,
`S1IzD5dTWng.opus` from
https://upload.wikimedia.org/wikipedia/commons/a/ae/Fiat_Coup%C3%A9_Turbo_-_engine_and_exhaust_sound_%28YouTube_S1IzD5dTWng%29.opus),
the Freesound previews by their numbers (`754775.mp3` from https://cdn.freesound.org/previews/754/754775_6823432-hq.mp3,
`639227.mp3` from https://cdn.freesound.org/previews/639/639227_14030247-hq.mp3); the preparation first:

    python tools/level-recording.py dBRIVYIzRm4.opus dBRIVYIzRm4-levelled.wav 63.5 69.5 0.5
    python tools/level-recording.py dBRIVYIzRm4.opus dBRIVYIzRm4-levelled-b.wav 81.5 84.0 0.5
    python tools/level-recording.py JNvonF5LGYQ.opus JNvonF5LGYQ-levelled.wav 100.3 104.2 0.5
    python tools/level-recording.py ZQQRnxRINac.opus ZQQRnxRINac-levelled-1.wav 15.2 16.25 0.25
    python tools/level-recording.py ZQQRnxRINac-levelled-1.wav ZQQRnxRINac-levelled-2.wav 19.8 21.4 0.25
    python tools/level-recording.py ZQQRnxRINac-levelled-2.wav ZQQRnxRINac-levelled.wav 27.8 29.4 0.5
    python tools/clean-recording.py S1IzD5dTWng.opus S1IzD5dTWng-clean.wav
    python tools/clean-recording.py S1IzD5dTWng.opus S1IzD5dTWng-clean4k.wav 700 4000
    python tools/clean-recording.py 754775.mp3 754775-clean.wav
    python tools/level-recording.py 754775.mp3 754775-levelled.wav 0.6 11.5 0.5
    python tools/clean-recording.py 639227.mp3 639227-clean.wav
    python tools/make-engine-bank.py game/assets/audio/hothatch/engine_idle 4 "639227-clean.wav@4.00@8.50@28.8@25@33@2" "639227-clean.wav@8.50@13.00@28.8@25@33@2"
    python tools/make-engine-bank.py game/assets/audio/hothatch/engine_drive 4 "ZQQRnxRINac-levelled.wav@15.60@15.75@87.6@82.4@125.0" "ZQQRnxRINac-levelled.wav@15.75@15.90@113.6@106.8@159.3" "ZQQRnxRINac-levelled.wav@15.90@16.05@144.8@136.1@174.5" "ZQQRnxRINac-levelled.wav@16.05@16.20@156.5@139.0@172.2" "ZQQRnxRINac-levelled.wav@20.35@20.50@98.4@92.5@138.2" "ZQQRnxRINac-levelled.wav@20.50@20.65@125.7@118.1@172.0" "ZQQRnxRINac-levelled.wav@20.65@20.80@156.4@147.0@198.3" "ZQQRnxRINac-levelled.wav@20.80@20.95@180.2@169.4@224.1" "ZQQRnxRINac-levelled.wav@20.95@21.10@203.7@191.5@245.3" "ZQQRnxRINac-levelled.wav@21.10@21.25@223.0@202.3@248.6" "ZQQRnxRINac-levelled.wav@21.25@21.30@215.3@202.3@236.8" "ZQQRnxRINac-levelled.wav@27.90@28.40@123.0@110.9@148.8" "ZQQRnxRINac-levelled.wav@28.40@28.90@135.2@127.1@161.2" "ZQQRnxRINac-levelled.wav@28.90@29.30@146.3@136.9@163.7"
    python tools/make-engine-bank.py game/assets/audio/hothatch/engine_overrun 4 "ZQQRnxRINac.opus@16.35@16.60@128@100@140" "ZQQRnxRINac.opus@16.60@16.85@113@90@125" "ZQQRnxRINac.opus@16.85@17.10@100@80@112" "ZQQRnxRINac.opus@17.10@17.35@90@70@100" "ZQQRnxRINac.opus@17.35@17.60@78@62@88" "ZQQRnxRINac.opus@17.60@17.85@70@55@78" "ZQQRnxRINac.opus@21.40@21.90@198.2@145.6@218.0" "ZQQRnxRINac.opus@21.90@22.40@155.2@116.5@174.4" "ZQQRnxRINac.opus@22.40@22.90@123.9@93.9@138.0" "ZQQRnxRINac.opus@22.90@23.40@102.7@74.7@114.7" "ZQQRnxRINac.opus@23.40@23.90@80.2@60.9@88.2" "ZQQRnxRINac.opus@23.90@24.40@66.1@49.6@74.6" "ZQQRnxRINac.opus@24.40@24.90@55.2@46.5@122.4"
    python tools/make-engine-bank.py game/assets/audio/turbohatch/engine_idle 4 "S1IzD5dTWng-clean.wav@17.00@19.25@29.5@27@33@2" "S1IzD5dTWng-clean.wav@19.25@21.50@29.5@27@33@2"
    python tools/make-engine-bank.py game/assets/audio/turbohatch/engine_drive 4 "2axudw2mPHk.opus@3.50@4.00@148.9@137.0@194.1@2.0" "2axudw2mPHk.opus@4.00@4.50@173.8@138.7@205.7@2.0" "2axudw2mPHk.opus@4.50@5.00@157.6@140.6@189.2@2.0" "2axudw2mPHk.opus@5.00@5.50@168.4@148.8@203.6@2.0" "2axudw2mPHk.opus@5.50@6.00@178.5@146.2@206.2@2.0" "2axudw2mPHk.opus@6.00@6.50@173.9@163.5@208.5@2.0" "2axudw2mPHk.opus@6.50@6.80@189.6@140.5@208.9@2.0" "2axudw2mPHk.opus@14.00@14.50@183.4@172.4@220.7@2.0" "2axudw2mPHk.opus@14.50@15.00@200.6@188.6@238.6@2.0" "2axudw2mPHk.opus@15.00@15.50@216.9@192.8@242.6@2.0" "2axudw2mPHk.opus@15.50@16.00@205.1@159.8@225.6@2.0" "2axudw2mPHk.opus@16.00@16.50@170.0@159.8@196.1@2.0" "2axudw2mPHk.opus@16.50@17.00@178.3@167.6@206.7@2.0" "2axudw2mPHk.opus@17.00@17.50@187.9@176.6@216.2@2.0" "2axudw2mPHk.opus@17.50@18.00@196.6@184.8@225.5@2.0" "2axudw2mPHk.opus@18.00@18.50@205.0@192.7@233.9@2.0" "2axudw2mPHk.opus@18.50@19.00@212.7@199.9@242.8@2.0" "2axudw2mPHk.opus@19.00@19.40@220.7@186.9@244.1@2.0" "2axudw2mPHk.opus@20.00@20.50@169.8@159.4@190.9@2.0" "2axudw2mPHk.opus@20.50@21.00@173.6@163.2@196.2@2.0" "2axudw2mPHk.opus@21.00@21.50@178.3@167.6@200.9@2.0" "2axudw2mPHk.opus@21.50@22.00@182.6@171.6@205.8@2.0" "2axudw2mPHk.opus@22.00@22.50@187.1@175.9@210.3@2.0" "2axudw2mPHk.opus@22.50@23.00@191.2@179.7@214.7@2.0" "2axudw2mPHk.opus@23.00@23.50@195.0@183.3@219.3@2.0" "2axudw2mPHk.opus@23.50@24.00@199.4@187.4@223.5@2.0" "2axudw2mPHk.opus@24.00@24.50@203.2@190.8@227.1@2.0" "2axudw2mPHk.opus@24.50@25.00@206.4@194.0@230.7@2.0" "2axudw2mPHk.opus@25.00@25.50@209.7@197.1@234.3@2.0" "2axudw2mPHk.opus@25.50@26.00@213.0@200.1@237.7@2.0" "2axudw2mPHk.opus@26.00@26.50@216.1@203.1@240.8@2.0" "2axudw2mPHk.opus@26.50@27.00@218.9@205.7@244.2@2.0" "2axudw2mPHk.opus@27.00@27.50@221.9@208.6@247.4@2.0" "2axudw2mPHk.opus@27.50@28.00@224.9@210.9@249.1@2.0"
    python tools/make-engine-bank.py game/assets/audio/turbohatch/engine_overrun 4 "S1IzD5dTWng-clean4k.wav@104.30@104.80@205.7@167.5@231.4" "S1IzD5dTWng-clean4k.wav@104.80@105.30@210.3@169.6@238.4" "S1IzD5dTWng-clean4k.wav@105.30@105.80@180.4@132.3@206.9" "S1IzD5dTWng-clean4k.wav@105.80@106.30@140.8@103.3@155.1" "S1IzD5dTWng-clean4k.wav@106.30@106.80@110.6@77.5@121.7" "S1IzD5dTWng-clean4k.wav@106.80@107.30@82.5@57.1@90.7" "S1IzD5dTWng-clean4k.wav@107.30@107.40@62.6@58.9@69.3"
    python tools/make-engine-bank.py game/assets/audio/v6/engine_idle 6 "754775-clean.wav@83.20@84.20@44.1@39.7@49.6@2.0" "754775-clean.wav@84.20@85.20@42.9@37.9@48.7@2.0" "754775-clean.wav@85.20@86.20@41.6@37.7@53.9@2.0"
    python tools/make-engine-bank.py game/assets/audio/v6/engine_drive 6 "754775-levelled.wav@2.30@2.80@78.6@73.7@98.7" "754775-levelled.wav@2.80@3.30@89.7@83.5@100.6" "754775-levelled.wav@3.30@3.80@91.5@86.0@110.3" "754775-levelled.wav@3.80@4.30@98.5@91.6@109.7" "754775-levelled.wav@4.30@4.80@99.7@93.7@121.7" "754775-levelled.wav@4.80@5.30@109.5@102.5@124.5" "754775-levelled.wav@5.30@5.80@113.2@106.2@129.6" "754775-levelled.wav@5.80@6.30@116.9@109.9@138.1" "754775-levelled.wav@6.30@6.80@125.5@117.7@146.1" "754775-levelled.wav@6.80@7.30@132.3@124.4@189.3" "754775-levelled.wav@7.30@7.80@172.1@158.0@202.6" "754775-levelled.wav@7.80@8.30@184.2@172.2@214.5" "754775-levelled.wav@8.30@8.80@195.0@183.2@225.0" "754775-levelled.wav@9.30@9.80@218.4@205.3@253.6" "754775-levelled.wav@9.80@10.30@230.5@216.5@266.5" "754775-levelled.wav@10.30@10.80@242.3@227.2@276.8" "754775-levelled.wav@10.80@11.30@251.7@236.6@283.5"
    python tools/make-engine-bank.py game/assets/audio/v6/engine_overrun 6 "754775.mp3@58.00@59.00@104.1@94.8@118.4" "754775.mp3@59.00@60.00@102.9@95.0@114.0" "754775.mp3@60.00@61.00@101.2@92.6@111.3" "754775.mp3@61.00@62.00@98.6@89.6@108.5" "754775.mp3@62.00@63.00@95.4@86.4@105.3" "754775.mp3@63.00@64.00@92.2@83.8@101.4" "754775.mp3@64.00@65.00@89.1@81.6@98.4" "754775.mp3@65.00@66.00@86.9@78.9@96.0" "754775.mp3@66.00@67.00@84.1@76.6@92.8" "754775.mp3@67.00@68.00@81.5@74.9@89.7" "754775.mp3@68.00@69.00@79.8@73.0@88.0" "754775.mp3@69.00@70.00@77.8@71.1@86.1" "754775.mp3@70.00@71.00@75.6@69.9@83.8" "754775.mp3@71.00@72.00@74.4@68.5@82.2" "754775.mp3@72.00@73.00@72.9@67.1@80.5" "754775.mp3@73.00@74.00@71.3@66.2@78.9" "754775.mp3@74.00@75.00@70.4@65.0@77.4" "754775.mp3@75.00@76.00@69.3@63.2@76.2" "754775.mp3@76.00@77.00@67.5@60.7@74.2" "754775.mp3@77.00@78.00@64.5@55.0@71.0" "754775.mp3@78.00@79.00@60.3@49.3@66.4" "754775.mp3@79.00@80.00@56.8@48.9@62.8" "754775.mp3@80.00@80.50@52.2@40.9@58.4"
    python tools/make-engine-bank.py game/assets/audio/v8/engine_drive 8 "dBRIVYIzRm4-levelled.wav@64.00@64.50@213.3@200.5@245.1" "dBRIVYIzRm4-levelled.wav@64.50@65.00@222.8@208.8@256.7" "dBRIVYIzRm4-levelled.wav@65.00@65.50@233.4@219.4@276.9" "dBRIVYIzRm4-levelled.wav@65.50@66.00@251.7@236.6@302.8" "dBRIVYIzRm4-levelled.wav@66.00@66.50@275.3@258.7@341.2" "dBRIVYIzRm4-levelled.wav@66.50@67.00@310.1@291.5@394.5" "dBRIVYIzRm4-levelled.wav@67.00@67.50@358.7@337.2@469.5" "dBRIVYIzRm4-levelled.wav@67.50@68.00@426.9@401.2@575.2" "dBRIVYIzRm4-levelled.wav@68.00@68.40@522.9@491.6@629.6" "dBRIVYIzRm4-levelled-b.wav@81.80@82.10@261.5@245.2@295.8" "dBRIVYIzRm4-levelled-b.wav@82.10@82.40@264.2@248.3@333.1" "dBRIVYIzRm4-levelled-b.wav@82.40@82.70@302.8@284.6@404.3" "dBRIVYIzRm4-levelled-b.wav@82.70@83.00@357.6@336.1@544.2" "dBRIVYIzRm4-levelled-b.wav@83.00@83.20@494.8@461.3@571.0" "nltPXKAN6BI.opus@17.75@18.00@548.8@490.3@640.1" "nltPXKAN6BI.opus@18.00@18.25@549.6@516.6@660.5" "nltPXKAN6BI.opus@18.25@18.50@555.8@506.8@665.2" "nltPXKAN6BI.opus@18.50@18.75@599.9@515.2@670.4" "nltPXKAN6BI.opus@18.75@19.00@548.1@512.8@669.0" "nltPXKAN6BI.opus@19.00@19.25@589.0@513.0@674.4" "nltPXKAN6BI.opus@19.25@19.50@547.9@512.2@668.2" "nltPXKAN6BI.opus@19.50@19.75@604.7@512.8@665.8" "nltPXKAN6BI.opus@19.75@20.00@561.0@527.4@672.7" "nltPXKAN6BI.opus@20.00@20.25@574.3@494.9@631.8"
    python tools/make-engine-bank.py game/assets/audio/v8/engine_overrun 8 "CGrl3VILOGc.opus@43.30@43.65@425.9@351.9@478.8" "CGrl3VILOGc.opus@43.65@44.00@428.3@262.6@472.2" "CGrl3VILOGc.opus@47.30@47.65@430.2@305.3@473.2" "CGrl3VILOGc.opus@47.65@48.00@324.8@242.0@357.2"
    python tools/make-engine-bank.py game/assets/audio/flat12/engine_drive 12 "JNvonF5LGYQ-levelled.wav@100.50@101.00@237.8@223.5@307.3" "JNvonF5LGYQ-levelled.wav@101.00@101.50@279.3@262.6@363.3" "JNvonF5LGYQ-levelled.wav@101.50@102.00@330.3@310.5@402.6" "JNvonF5LGYQ-levelled.wav@102.00@102.50@366.0@344.0@445.3" "JNvonF5LGYQ-levelled.wav@102.50@103.00@404.8@380.5@494.6" "JNvonF5LGYQ-levelled.wav@103.00@103.50@442.0@409.1@502.2" "JNvonF5LGYQ-levelled.wav@103.50@104.00@444.4@417.8@626.3"
    python tools/make-engine-bank.py game/assets/audio/flat12/engine_overrun 12 "JNvonF5LGYQ.opus@104.25@104.50@575.0@483.9@632.5" "JNvonF5LGYQ.opus@104.50@104.75@553.8@505.2@630.5" "JNvonF5LGYQ.opus@104.75@105.00@537.5@432.7@591.2" "JNvonF5LGYQ.opus@105.00@105.25@460.3@427.4@619.7"

(The third pass of the Spider's levelling, 27.8-29.4 s, prepared a hold at 3,540-4,460 rpm that is no longer cut: laid
beside the blips' pulses it stepped the level 2.6 LU at 3,500 rpm. The file is still made that way so the segments'
samples match.)

The banks are imported uncompressed (`compress/mode=0` in their `.import` files) because the voice reads their
samples; a compressed import would not give them back.

Which bank a car plays is its engine's `EngineSpec.SoundFolder` (physics/Vehicle/EngineModel.cs), with its
`Cylinders` turning revs into firing pulses; the Unreal version's table in `GlpAudio.cpp` follows it.

SHA-256 of the nine recordings, each fetched from its page on 3 November 2026 under a descriptive User-Agent (the
Commons files are the uploads themselves, Opus at 48 kHz, their SHA-1 checked against the file pages' metadata):
`dBRIVYIzRm4.opus` 0ff8a519dd090f5c2123db855358bbe96217777101e7d0eadbbdfeb32b5d4e29,
`CGrl3VILOGc.opus` c0f3b53ca001ac2dfc307f0e435990223b14e114a12f7c1561703ed999fc13a2,
`nltPXKAN6BI.opus` 6c357f7e94ba68467dd4f82baf6a2307203ced7baa1f6507855cc5dbe69afe49,
`JNvonF5LGYQ.opus` 012f00b4f320fe5d569beaee91f26b0944dfbdbde1f74b6f29e45f459d76e264,
`ZQQRnxRINac.opus` f10308cd4c6416ea160d96fc5453db29de70cc3f94d69f1944894c50fc2aed4c,
`2axudw2mPHk.opus` 8ed0b230faa0c9a038722306e45f0f81b301734ae7e7419b33970303ea5fa723,
`S1IzD5dTWng.opus` 7799e139bc75f1d16699c30c11d34452a1bde159940685928ac0be6743239708,
`754775.mp3` 6bb2ac28ffb644be408ed5a04a889a752f01e25c7948a0e6b5698d4b03d7f48f,
`639227.mp3` af341d6ebd914606fef6e38717e5d11c585d46af6af8a94141d76226f4740e62.

**Licences (banks2).** Commons' "engine and exhaust sound (YouTube ...)" files carry the YouTube licence of their
video, and their Commons licence review is pending (all seven are in "License review needed (audio)"; uploaded by Braiion
on 22 and 26 October 2026, each tagged CC BY 3.0). Each video's licence was read from YouTube's own metadata on 3
November 2026 ("Creative Commons Attribution license (reuse allowed)"). Six of the videos were published before 1
August 2025, when YouTube's Creative Commons option was 3.0: FiExhaust's 570S (17 July 2021) and 540C (31 August
2019), TvdL Films' 458 Speciale (22 April 2017), Supercars1989's F12tdf (1 July 2016), Calvin MotorSport's S1 (12
October 2020) and Witu Tech's Coupé (26 November 2017). Luciano's Classics' Spider was published on 28 December 2025,
under the 4.0 option, so its source is CC BY 4.0 where Commons' template tags it 3.0; the credit given here meets both.
The Commons pages name the author and link the video, and each video describes its own recording (an exhaust maker's
cars at tuning shops, a classic-car dealer's Spider, a show's 458 Speciale, a spotter's F12tdf, an owner's S1 and Coupé),
so the uploaders are plausibly the authors of their soundtracks. The two Freesound recordings are their recordists' own
by their descriptions (a lavalier above the exhaust of a 3.2 V6 driven fast; a Volvo 240 idling outside with the bonnet
closed), CC BY 4.0.

**What each stands in for, and why these recordings (banks2).** The V8 is drawn as a McLaren 570S and now plays its
engine: the 570S's M838TE pulling twice from 3,200 to about 8,500 rpm, the 570S's limiter, on a catless Fi Exhaust
system like the 458's F430 track; the 540C's revs falling after its blips for the overrun (the 540C is the same engine
at 540 PS). The game's V8 redlines at 9,000 rpm, as the 458-type engine of its name does and no McLaren: cut to the
570S's top alone the voice laid the 8,500 rpm pulses closer at 9,000 and held less of the sound as a tone there (-6.0
dB, -4.77 asked, `EngineVoiceTests.Pulls_as_a_tone_up_to_the_redline`), and the tracker's labels past the limiter
followed the half order (620 Hz where the firing was 545, the engine bouncing off the cut) and were cut off; so the
top, 7,500-9,210 rpm, is a 458 Speciale's flat-plane 4.5 held against its 9,000 rpm limiter at a show, ten quarter-second
stretches laid beside the 570S's pulls (with them the voice holds the third the test asks). The flat-twelve: no clean recording
of a flat-twelve was found (below), so the Testarossa's 1984 flat-twelve plays a Ferrari V12 of 2016, an F12tdf's F140
FC 6.3 (an even-firing twelve like the flat-twelve, each bank's exhaust pulsing at three times the crank's speed),
accelerating hard from 2,350 to 5,690 rpm on the road and lifting off at the top; its idle stays the Lamborghini V12's
(CC0). The hot hatch (the 205 GTi's eight-valve 1.9): an 8-valve two-litre twin cam of the same years, a 1982 Alfa Romeo
Spider 2000's, revved standing with the microphone at its exhaust, for the run-up and the overrun; its idle a 1990 Volvo
240's 2.3 eight-valve four heard outside with the bonnet closed, because the Spider's idle at the exhaust is the brightest
idle of the catalogue (its 1-3 kHz -5.0 dB of its sound, where the diesel's idle holds -9.3 and the game asks every petrol
four's to lie 6 dB under the diesel's: `DieselCharacterTests`; the Volvo's, cleaned, -16.6). The rally hatch (the Delta
Integrale's 2.0 16-valve turbo four): an Audi S1's 2.0 TFSI turbocharged four pulling through first, third and fourth
gears on a 0-240 km/h run for the run-up (4,390-6,800 rpm; the S1 is the Integrale's modern counterpart, a small
four-wheel-drive hatch with a two-litre turbo four), and a Fiat Coupé 2.0 16v Turbo, the Integrale's own engine family,
for the idle (850-990 rpm) and the lift-off at the end of its 0-200 run (6,500 to 1,840 rpm). The V6 hatch (the Golf
R32's 3.2 VR6): a 3.2 V6 of the same displacement and cylinder count, a Honda J32 in an Acura, driven fast with a
lavalier above its exhaust, for all three banks: a pull from 1,570 to 4,130 rpm and the next gear to 5,160, a coast from
2,150 rpm to a stop with the throttle shut, and its idle after the drive. A VR6 fires its six cylinders every 120
degrees of crank like any even-firing six, so the voice fires at rpm x 6 / 120 as the real engine does; the Acura's pulls
replace the Jaguar's blips of a standing car (3,600 rpm at most) with a car under load to 5,160, so past the R32's 5,160
rpm the voice lays the run-up's top pulses closer, up to 1.3 times at its 6,600 rpm redline (2 times before).

*Levelled, cleaned, and where each starts.* The 570S's pulls, the F12tdf's pull and the Acura's are levelled over
half-second windows (`level-recording.py`, as the 458's F430 pull is), the Spider's blips over quarter-second ones (a
blip gains 1,000 rpm in a tenth of a second). The Acura's run-up starts at 2.3 s (1,570 rpm), not at the throttle's
opening at 0.8 s (1,360): its first second and a half, the throttle opening, is its dullest, and cut from 0.8 s the level
at the idle revs rose 0.2 LU from a load of 0.3 to 0.45 and 1.8 LU to full throttle (2.5 asked); from 2.3 s it passes
(`Opening_the_throttle_at_the_idle_revs_raises_the_level`), and cut without levelling it did not rise at all. The Spider's
blips start at 2,620 and 2,920 rpm, not at the throttle's opening (1,430 and 2,100): cut from there, the level at the idle
revs fell with the throttle (-0.8 LU from a load of 0.3 to 0.45), the blips' first pulses being soft; and the hold at
3,540-4,460 rpm (27.9-29.3 s) stepped the level 2.6 LU at 3,500 rpm beside the blips' pulses, and is left out. The
Acura's idle, the Coupé's idle and lift-off and the Volvo's idle are quiet recordings (-32 to -45 dBFS RMS; the Coupé's
lift -40 to -48) and are cut from copies with their floor taken out (`clean-recording.py`, from 700 Hz up and low-passed
at 6 kHz; the lift-off at 4 kHz): cut from the raw Coupé the rally hatch's overrun was as bright above 2 kHz as its
run-up (2.9 dB under it, 3 asked, `GranularEngineTests.Overrun_is_quieter_than_the_throttle_at_the_same_revs_in_every_band`),
the recorder's hiss brought up with the segments, and low-passed at 6 kHz still within 0.1 dB of that line. The Coupé's
first idle stretch (1.3-4.6 s) is not an idle: its lines fall from 5,600 to 1,000 rpm, the engine spinning down after a
rev, and cut as the idle the voice at 950 rpm peaked at 36 Hz, not its 31.7 (`Idles_at_the_idle_firing_frequency`); the
idle is 17.0-21.5 s. The Acura's gear-change lifts (11.3-11.75 and 15.95-16.4 s, 5,160 to 3,510 and 4,310 to 2,960
rpm) were cut into the overrun first and are left out: lifting off from the redline the voice ticked 14 to 28 times in
five seconds on them (one asked, `Lifts_off_from_the_redline_without_a_run_of_ticks`); on the coast alone it does not.

*The idles' labels.* The Volvo's and the Coupé's idles are quiet and their firing line is weak beside its harmonics
(the Volvo's at 27 Hz under its 2.5th-order line at 43-48 Hz and its second at 59; the Coupé's second order at 59 Hz
the strongest line of 17-21.5 s), so both are tracked on the second harmonic (the harmonic field at 2) over a band wide
enough for the tracker to move (25-33 and 27-33 Hz of firing: under about 45 Hz a band of 12 % is narrower than a
frame's bin, and the tracker sticks at its seed, as the tool's own notes say). Tracked over 24-31 Hz the Volvo's labels
sat at 27.0 Hz (810 rpm) to the pulse, and the voice took the top of its idle as 810 rpm: held at the hot hatch's governed
idle, 930 rpm, it was already across into the run-up's pulses, 5 to 6 LU louder than every other car's idle (-18.6 LUFS
against -21 to -29), and the throttle lowered the level from there (`The_idle_governors_throttle_keeps_a_quiet_idle`,
`Opening_the_throttle_at_the_idle_revs_raises_the_level`). Tracked over the wider band the labels span 750-990 rpm,
the idle's top lies over the governed idle, and both pass; the Coupé's over 25-35 Hz spanned 760-1,050 and stepped
the level 1.4 LU at 1,000 rpm (`The_level_follows_the_revs_evenly_from_the_idle_to_the_redline`), over 27-33 it does not.

*The S1's firing line.* In the S1's and the Coupé's tracks the strongest line is the fourth crank order, twice the
firing, with the firing a weaker line under it: at the S1's 0-240 km/h run the pairs (183 and 366 Hz at 14 s, 221 and
441 at 19 s) are the firing and twice it, the firing 5,480 to 6,630 rpm at the top of third gear, where the dominant
line taken as the firing would put the car at 11,000-13,000 rpm. The S1's run-up is therefore tracked on that line
with the bank tool's harmonic field at 2 (the field now takes a fraction too, 0.5, to follow the half order of a V8
heard from inside; no bank uses it). The S1's fifth gear (29-38 s, 5,410-6,250 rpm) and its second run (73-79 s, 2,700-
7,060) were cut in and are left out: with the fifth the evening of the banks did not even the timbre (the spread of
brightness about its trend 1.85 dB evened against 1.78 raw, `The_timbre_follows_the_revs_more_evenly`) and the limiter's
cut fell in 12 ms (10 asked); with the second run the level stepped 1.8 LU at 4,600 rpm and the overrun was as bright as
the run-up above 2 kHz. The Coupé's own run (46-101 s) was not cut: through most of it the two lines are of equal
strength and neither reading gave the revs of a 0-200 run in its gears (the lower line read as the firing puts the car
at 3,200-3,700 rpm through fourth gear, the upper at 6,500-7,400), so only its idle and its lift-off, whose line is one
and falls alone, are used.

**Checked and not used (banks2, 3 November 2026; Commons' "Sounds of automobiles" category, 386 "engine and exhaust
sound" tracks, and Freesound, CC0 and CC BY only):**

- *for the V8:* a 540C onboard from 0 to 215 km/h (L'argus, g8dJtzLeb4o: clean, no sample past full scale, but heard
  from inside the half order is its strongest line and the cabin's boom is on every pulse; its three gears, 3,500-8,000
  rpm, were tracked and kept as a fallback), the 600LT (RXj9PUkF0gk: 1.36 million decoded samples past full scale in
  351 s, a limiter on everything), the 650S (yjsN6YoJm2o: 27,600, and held at one speed at a show), the 675LT
  (Uc0FP4UDuQ0, 75,700 in its first ten seconds), the 720S with Novitec exhaust (XfgP-gX7KNg, 9,800), the P1
  (M1xuT1ue10c: revs held near 4,000 rpm standing), the 765LT (5oSRv66QFW0: drive-bys with squealing tyres, Doppler on
  every pass), the 488 GTB with Novitec exhaust, the 488 Spider and the F8 Tributo with Fi Exhaust (Ei59DHJ5acU,
  52YexV8ube4, Qlx_Eay0xeM: 8,000 rpm engines, 16, 348 and 587 flat-topped samples). The 570S's, 540C's and Speciale's
  own counts of decoded samples past full scale (39,000, 24 and 3,200) are a loud master, not clipping: 101, 0 and 10
  flat-topped samples.
- *for the flat-twelve:* the Commons Testarossa (denkblockade, jOiYsgNQvEM) is a phone at a distance from an open engine
  bay, -40 to -55 dBFS, its blips reaching 2,400 rpm; the Countach LP500S in Singapore (uYv1l31kgKc) peaks at 0.19 and is
  an idle and a departure at a distance; the Enzo (lbF-36BGnNc) and the Espada (3D3ryefwttM) are spotters' drive-bys
  with Doppler; the FF at a show (PsNPUYhhWU0) holds 1,100-2,600 rpm with one rev to 5,600; the 599 SA Aperta
  (DW4EhNXlkoM, 10,100 samples past full scale), the Aventador SV (qll6WBfaXvM, blips at a show) and the 812 Superfast
  (UbQ5e4z5WXc, a spotter's pulls with the car passing) were passed over for the F12tdf, whose pull is one car, one
  gear, 42 flat-topped samples in 157 s and none in the stretch cut.
- *for the hot hatch:* the Spider's idle (41-57 s, above); a Peugeot 205 started and idling close-miked (#140418,
  Thurcke, CC BY 4.0: 1-3 kHz -8.7 dB of its sound), a 2006 Peugeot idling (#811898, CC0, -6.0), a Twingo (1iwuiuqE2QY,
  -4.2), an Alfa 156 Twin Spark idling (UwnBSjQfJt8, -46 dBFS and -3.5), a Fiat Uno 1.3 ridden in binaurally (#670868,
  CC BY 4.0: its stops are dark enough, -26 dB, but their lines are the cabin's, no firing line to follow), a 106 Rallye
  (Z2iFd9mSyhE: held at 1,500-4,000 rpm, loud) and an Escort RS2000 on a Supersprint exhaust (B2swcWkMOMM: a 2.0
  eight-valve Pinto, but held at 2,500-3,000 rpm throughout, no idle and no pull); the Golf GTI (NrA1GgbjFRg, a tuning
  shop's video), the Saxo (launch control) and the Alfa 147 Twin Spark (flames) were not needed once the Spider's blips
  were cut.
- *for the rally hatch:* the Commons Integrale (Grzegorz Kalinecki, 0qq7MZ00ipI) is 80 s held within 5.6 dB by a limiter
  and cruising at 1,600-1,800 rpm, no pull; the S1's own idle (1.5-3.4 s) is 1.4 s and as bright as the Spider's
  (-10.4 dB, 3.3 dB short of the diesel test's margin even cleaned); the Mégane RS 0-250 (TplQIptv-ME, 41,300 samples
  past full scale), the Sierra Cosworth (7e3q50wwmNE, idle only), the Leon Cupra R (jAD7wLKT5KE, Motor1: a low idle
  whose every half order shows, no pull told), the 200SX (QF3N_McYdwk: an SR20DET on a straight pipe held at 3,700-6,000
  rpm), the 406 turbo, the Corsa OPC, the Clio RS Trophy and the second Mégane RS were passed over for the S1's run.
- *for the V6 hatch:* a BMW E46 M3's S54 pulling through its gears on an airstrip (#415276 and #415275, ikbenraar, CC BY
  4.0: clean, 3,900-7,000 rpm, a straight six) is the next candidate if the Acura's 5,160 rpm top is ever raised; the
  GTV6's Busso (kX2PYswTWdg, 15,800 decoded samples past full scale and 260 flat tops), the Omega 3.0 V6 and the Laguna
  II V6 (short, standing), the Touareg VR6 and the Lexus IS300 (clipped, the banks review) stay out.

The Internet Archive item **"Car Engine Sounds"** (identifier `car-engines`, dated 2011-02-22, uploaded in two batches:
27 files on 2015-09-01 and 44 on 2019-03-14) was published by its uploader under **CC0 1.0 Universal** (`licenseurl` in
the item's metadata: http://creativecommons.org/publicdomain/zero/1.0/). Item page:
https://archive.org/details/car-engines. Until banks2 the banks of these five cars were cut from it (`3 Ferrari 458`
for the V8's run-up and overrun, `15 Lamborghini Countach` for the flat-twelve's, `honda civic type r` for the hot hatch's
three, `25 Nissan Pulsar` for the rally hatch's three, `Jaguar f-type s` for the V6 hatch's three), as were the cars13
banks until cars13 audio2 (below).

The CC0 is as declared by the uploader, and the item says nothing else of where its tracks come from: its description
is "Car engine sounds - free use", and no track names a recordist, a microphone or a date. Read against the Freesound
recordings used here (cars13 review, 2 November 2026), it looks like a compilation of others' recordings:

- its MP3s carry ID3 track numbers, as a numbered collection would (`3 Ferrari 458` 03, `15 Lamborghini Countach` 15,
  `25 Nissan Pulsar` 25, `27 Toyota GT86` 27; `honda civic type r` 46, `honda s2000` 47, `Jaguar f-type s` 48 and
  `mazda MX 5` 57, these four of the 2019 batch);
- one of them, `27 Toyota GT86`, is a video's soundtrack (its level held flat by a limiter; below, under the BRZ);
- the S2000 and MX-5 tracks sound mastered rather than recorded: both decode to peaks of +2.2 dBFS, the MX-5 idles
  within 1 dB of its full-throttle pull (-9.1 against -8.1 dBFS RMS, mono), and over 0.1 s windows its level spans
  6.6 dB between the 10th and the 90th percentile and the S2000's 12.2, where the Freesound recordings of an engine
  changing revs used here span 16 to 25 dB (#183654, #191194, #241083, #268622);
- `3 Ferrari 458` (20.4 s, mono, 128 kbps) has the form of the engine ringtones Ferrari gave away as MP3s from the
  458 Italia's site at its launch in 2009 (accelerating, overtaking, on the track:
  https://www.motorauthority.com/news/1034233_put-the-458-italia-in-your-pocket-ferrari-releases-ringtones); no archived
  copy of those files was found to compare it with.

None of this shows that a track is someone else's, but no bank is cut from the item any more: the three cars13 engines
that leaned on it moved in cars13 audio2 (below), and the five first cars' banks in banks2 (above). The drones' engine
loop (`traffic/drone.wav`), which was the flat-twelve voice itself playing the Countach bank, was recorded again from the
voice on the F12tdf's bank (below, under Traffic).

History: every bank was re-cut on 2 November 2026 (banks), before banks2 replaced the five first cars' recordings (above), from the same recordings with the tool seeking exactly. Those cut
before the V6's re-cut began their segments with the MP3 decoder's silence: 50 grains 12 dB or more under their
neighbours, most 40 to 160 dB under (the V8's run-up 15 and its overrun 7, the flat-twelve's run-up 8 and its overrun
7, the rally hatch's overrun 5, the hot hatch's run-up 4 and its overrun 3, the estate's overrun 1), each a drop-out of a
firing period wherever the voice picked it. Cut exactly, some start points fell on what the decoder's silence had hidden
or the tool had not warned of, and moved:

- the V8's run-up from 0.15 to 0.3 s: its first 30 pulses are the recording fading in from silence, up to 20 dB under the
  rest (the voice stepped 2.7 LU at 2,300 rpm); its second segment is split around 5.6-5.95 s, where the recording dips
  9 dB (it stepped 1.9 LU at 6,700 rpm);
- the hot hatch's overrun from 0.6 to 0.85 s and to 9.75 s from 10.0: the blip before 0.8 s overloads the recording
  (samples past full scale) and its end crackles; its idle from 2.7 to 3.05 s, past a waver up to 1,370 rpm that set
  the top of the idle 200 rpm too high;
- the rally hatch's idle from 0.4 to 0.55 s and its overrun's second segment from 16.5 to 16.65 s: the Pulsar track
  starts with the engine catching, 13 dB quieter and twice as sharp, and the overrun's segment with the end of a blip;
- the V6's overrun from 0.13 to 0.15 s later (above);
- the saloon's idle to 19.75-21.2 s of its second stretch (19.6-25.0 s): around it three bursts of a tick 10 times as
  sharp as the recording (the Niva's idle is otherwise smooth), and after them 22.55-25.0 s is 12.5 dB duller than the
  rest, more than the evening's tilt of 6 dB could take back;
- the fleet saloon's Bravo run-up and overrun without the instants of its ticks (10.74, 10.85, 12.21 and 12.75 s; 57.97,
  58.58, 60.63, 60.88 and 61.2 s), the Punto's run-up without a tick at 74.98 s.

The tool now also fills the pulses it lost: where two expected pulses moved onto the same peak, one was dropped and the
grains on either side spanned two periods (12 % of the fleet saloon's run-up, 17 % of the flat-twelve's spanned over 1.3
periods), which laid at the revs played quieter than the rest. It fills only a gap of 1.75 periods or more, with pulses
three quarters of a period or more from either side, and of two peaks closer than 0.6 of a period it keeps the stronger
(banks review): filling from 1.5 periods with half a period either side, and keeping peaks half a period apart, left
grains reaching half a period on a side, and laid under their recorded revs two such sides left gaps (the hot hatch's
idle, 6 ms of silence at 900 rpm). Every bank was re-cut with it from the segments above (the recordings and the
segments' samples are unchanged, only the marks moved). No bank now has a grain more than 9.8 dB under its neighbours,
a crackle by the tool's measure or a grain reaching under 0.6 of its period on a side
(`EngineVoiceTests.Every_bank_is_cut_without_drop_outs_or_crackles`, `Every_grain_reaches_most_of_a_period_either_side`).

The five-cylinder hatch of 0.9-0.19 (a Volvo five at idle, credit cmusounddesign, CC BY 4.0, and the S2000 track
laid at a five's firing frequency, `GranularEngine.FivePattern`) left the game with the car it voiced in 0.21; its banks
were shipped again in cars13 stages 1 and 2, re-cut, for the Bravo HGT's five, and gave way in cars13 audio2 to a real
five's (below). Neither the Volvo nor the S2000 is shipped now, and the voice weights no cylinder's pulse of its own
(`FivePattern` is gone, below).

## The cars13 engines (`v8flat/`, `inline5/`, `four16v/`, `tsi4/`, `boxer4/`)

Five banks for the five cars of cars13 (catalogue 8-12: the 458 Italia, the Bravo HGT 2.0 20V, the Bravo 1.6 16V, the
2018 Leon Cupra and the first BRZ). They were cut a stage ahead of the catalogue entries that name them, and each
engine's `EngineSpec.SoundFolder` names its folder since cars13 stage 2, so the catalogue's voice tests hold each of them
as its car's; the bank-level tests (`EngineVoiceTests.Every_bank_is_cut_without_drop_outs_or_crackles`,
`Every_grain_reaches_most_of_a_period_either_side`) take every bank folder on disk. The HGT's five was cut as `five/`
and renamed `inline5/` when the car took it: `--car=five` is the Delta's short name (its model files carry the key of
the five-cylinder hatch it replaced) and keeps it. The 458's run-up and overrun and the HGT's and the 1.6's three banks
were cut again in cars13 audio2 from recordings whose recordists describe them (below); the Internet Archive item is
used by none of the five, and since banks2 by no bank of the game.

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz [@ harmonic followed]) |
|---|---|---|
| `v8flat/engine_idle` | Freesound #241083 "Ferrari 360 Spider - engine sound" by PritzProductions (a Zoom H6 directly above the engine of the standing car), https://freesound.org/s/241083/ (CC0 1.0) | 18.8@20.2@77@60@80, 20.35@26.1@72@60@80, 27.5@29.4@74@55@80 |
| `v8flat/engine_drive` | Wikimedia Commons "Ferrari F430 Scuderia with Fi Exhaust system - engine and exhaust sound (YouTube 5Te9AlCfhto).opus", the audio track of FiExhaust's video "Ferrari F430 Scuderia Bumper Conversion w/ Fi EXHAUST Dual Exit X PP-Performance Dyno X HitZ" (an F430 Scuderia's F136 4.3 flat-plane V8, an aftermarket valved exhaust, on a chassis dynamometer), https://commons.wikimedia.org/wiki/File:Ferrari_F430_Scuderia_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_(YouTube_5Te9AlCfhto).opus (CC BY 3.0, credit FiExhaust), its first pull levelled over half-second windows (below) | 27 half-second stretches of the first full-throttle pull, 2.5-16.0 s, 2,020-8,770 rpm (the rebuild lines below) |
| `v8flat/engine_overrun` | the same track (the dynamometer's coast-downs after the first pull, the throttle shut) | 19.6-21.7 s and 22.4-25.9 s in 0.7 s stretches, 5,160-8,160 rpm |
| `inline5/engine_idle` | Wikimedia Commons "Audi TT RS - engine and exhaust sound (YouTube MH4ek7tBg6I).opus", the audio track of Speedster Turbo Berlin's video "Audi TT RS '18 Exhaust Sound" (a stock TT RS's 2.5 TFSI turbocharged five with the sport exhaust, a microphone on the rear number plate into a field recorder), https://commons.wikimedia.org/wiki/File:Audi_TT_RS_-_engine_and_exhaust_sound_(YouTube_MH4ek7tBg6I).opus (CC BY 3.0, credit Speedster Turbo Berlin) | 71.5@76.0@45.5@40@51@2 (990-1,180 rpm, settled after a coast-down) |
| `inline5/engine_drive` | the same track, its two pulls levelled over half-second windows (below) | 36.92-42.60 s (1,560-3,100 rpm) and 172.50-179.45 s (2,370-6,590 rpm) in 25 stretches of about half a second |
| `inline5/engine_overrun` | the same track (the throttle lifted, the revs falling) | 258.6-265.4, 292.6-299.0 and 139.6-145.4 s in 39 stretches of about half a second, 2,700-5,120 rpm |
| `four16v/engine_idle` | Wikimedia Commons "Mazda MX-5 Miata - engine and exhaust sound (YouTube wKeTXxwpkS0).opus", the audio track of Ride in Japan's video "MAZDA MX5 Miata 1.6L exhaust sound sample" (the owner's NA6CE, its stock B6-ZE 1.6 sixteen-valve four with a Fujitsubo header and a custom exhaust), https://commons.wikimedia.org/wiki/File:Mazda_MX-5_Miata_-_engine_and_exhaust_sound_(YouTube_wKeTXxwpkS0).opus (CC BY 3.0 as Commons tags it, CC BY 4.0 on YouTube; credit Ride in Japan; below), its steady floor taken out (`clean-recording.py`, below) | 7.0@16.5@27.8@26@30@6 (780-880 rpm) |
| `four16v/engine_drive` | the same track (four full-throttle pulls through first, second and third gear) | 469.6-472.6, 473.8-477.6, 478.8-484.4 and 490.2-498.4 s in 41 stretches of about half a second, 1,460-6,050 rpm |
| `four16v/engine_overrun` | Freesound #425869 "Volkswagen Golf V 1.6 FSI engine reverse mono.wav" by Soundholder (a Golf V's 1.6 FSI, from Soundholder's recording library of the car, five run-ups in reverse gear), https://freesound.org/s/425869/ (CC BY 3.0, credit Soundholder) | the revs falling after each run-up: 9.4-11.9, 21.0-24.93, 33.6-36.51, 44.6-47.24 and 57.0-63.8 s in 38 stretches of about half a second, 1,460-5,990 rpm |
| `tsi4/engine_idle` | Freesound #268622 "VW Golf R (Zoom H1, OST Lav).wav" by EverydaySounds (a Golf R, a lavalier microphone taped to its back close to the exhaust for a lap of the Kyalami circuit), https://freesound.org/s/268622/ (CC0 1.0) | 492.6@500.1@27@23@31@2, 500.3@503.5@27@23@31@2 |
| `tsi4/engine_drive` | #268622, as above (its full-throttle stretches, and one on a part throttle at the bottom) | 31.2@33.3@102@90@130, 33.4@36.0@124@110@165, 36.8@41.1@167@150@230, 16.0@20.1@145@135@215, 55.0@58.4@165@150@215, 66.4@71.3@135@120@215 |
| `tsi4/engine_overrun` | #268622 (the revs falling after four of the run-ups) | 263.0@265.9@167@95@175, 305.4@307.7@174@130@185, 20.2@22.9@208@140@220, 110.2@113.4@188@150@200 |
| `boxer4/engine_idle` | Freesound #191194 "FR-S BRZ GT-86 Vortech Supercharged Dyno Run" by dezoris (a GT 86's FA20 idling on the dynamometer before its run), https://freesound.org/s/191194/ (CC BY 4.0) | 0.4@2.3@27@22@32@2, 2.6@3.0@27@22@32@2, 3.2@4.35@27@22@32@2 |
| `boxer4/engine_drive` | Freesound #183654 "DynoJet Scion FR-S" by dezoris (a stock FR-S run up through one gear on a DynoJet, the page not saying which; a Tascam DR-40 at the exhaust and a Sennheiser ME-66 in the engine bay, mixed), https://freesound.org/s/183654/ (CC0 1.0), its run levelled (below) | 3.6@5.0@64.5@60@78.1@2, 5.3@6.5@75.4@72@92@2, 6.5@8.0@86.3@82@106, 8.0@9.0@102@94.9@120.9, 9.0@10.0@113@105.1@132.7, 10.0@11.0@124@115.3@141.2, 11.0@12.0@132@128@145, 12.0@13.0@140@130.2@161, 13.0@14.0@150.5@145@166, 14.0@15.0@161@149.7@182.4, 15.0@16.0@170.5@165@185, 16.0@17.0@180@175@195, 17.0@18.0@189.5@184@206, 18.0@19.0@199@185.1@224.7, 19.0@20.0@210@195.3@236.5, 20.0@21.0@221@205.5@245.5, 21.0@22.0@229.4@213.3@254.4, 22.0@23.2@237.8@221.2@267.5 |
| `boxer4/engine_overrun` | #183654 (the revs falling from the limiter after the run) | 24.1@25.9@245@125@250 |

Every segment's labels were checked against its recording's spectrogram (the firing line under the labels, not a line at
half or one and a half times it); the BRZ's run-up, re-cut after the cars13 review, against anchors read off it too, all
its labels within 8 % of them (below); the banks cut again in cars13 audio2 by the order of their cycle as well (below,
"How the firing lines were confirmed"). The five engines' banks hold no grain more than 6.3 dB under its neighbours (the
458's overrun), no crackle by the tool's measure (the sharpest 3.8 times its segment's median, the Cupra's overrun), no
grain reaching under 0.60 of its period on a side, and no crackle warning from the tool. Their revs (rpm = Hz x 120 /
cylinders): the 458's idle 950-1,170, run-up 2,020-8,770, overrun 5,160-8,160 (eight cylinders); the HGT's idle
990-1,180, run-up 1,560-6,590, overrun 2,700-5,120 (five); the 1.6's idle 780-880, run-up 1,460-6,050, overrun
1,460-5,990 (four); the Cupra's idle 790-840, run-up 2,880-6,720, overrun 2,930-6,360 (four); the BRZ's idle 790-840,
run-up 1,900-7,490, overrun 3,880-7,490 (four). Past the top of a run-up the voice lays its highest pulses closer
(`GranularEngine`), up to the redline: the 458's 9,000, the HGT's 7,000, the 1.6's 6,500 and the Cupra's 6,800 rpm.

Rebuild (the Freesound previews renamed by their numbers:
https://cdn.freesound.org/previews/241/241083_3450345-hq.mp3 as `241083.mp3`,
https://cdn.freesound.org/previews/268/268622_2273054-hq.mp3 as `268622.mp3`,
https://cdn.freesound.org/previews/183/183654_89652-hq.mp3 as `183654.mp3`,
https://cdn.freesound.org/previews/191/191194_89652-hq.mp3 as `191194.mp3` and
https://cdn.freesound.org/previews/425/425869_4421503-hq.mp3 as `425869.mp3`; the Wikimedia Commons files by their
YouTube ids:
https://upload.wikimedia.org/wikipedia/commons/d/d0/Ferrari_F430_Scuderia_with_Fi_Exhaust_system_-_engine_and_exhaust_sound_%28YouTube_5Te9AlCfhto%29.opus
as `5Te9AlCfhto.opus`,
https://upload.wikimedia.org/wikipedia/commons/8/8e/Audi_TT_RS_-_engine_and_exhaust_sound_%28YouTube_MH4ek7tBg6I%29.opus
as `MH4ek7tBg6I.opus` and
https://upload.wikimedia.org/wikipedia/commons/e/ee/Mazda_MX-5_Miata_-_engine_and_exhaust_sound_%28YouTube_wKeTXxwpkS0%29.opus
as `wKeTXxwpkS0.opus`; so named, the commands rebuild all fifteen banks to the byte):

    python tools/level-recording.py 5Te9AlCfhto.opus 5Te9AlCfhto-levelled.wav 2.0 16.0 0.5
    python tools/level-recording.py MH4ek7tBg6I.opus MH4ek7tBg6I-levelled-1.wav 36.92 42.6 0.5
    python tools/level-recording.py MH4ek7tBg6I-levelled-1.wav MH4ek7tBg6I-levelled.wav 172.5 179.45 0.5
    python tools/clean-recording.py wKeTXxwpkS0.opus wKeTXxwpkS0-clean.wav
    python tools/level-recording.py 183654.mp3 frs-dyno-levelled.wav 2.6 24.2
    python tools/make-engine-bank.py game/assets/audio/v8flat/engine_idle 8 "241083.mp3@18.8@20.2@77@60@80" "241083.mp3@20.35@26.1@72@60@80" "241083.mp3@27.5@29.4@74@55@80"
    python tools/make-engine-bank.py game/assets/audio/v8flat/engine_drive 8 "5Te9AlCfhto-levelled.wav@2.50@3.00@136.0@129.2@153.9" "5Te9AlCfhto-levelled.wav@3.00@3.50@146.5@139.2@163.8" "5Te9AlCfhto-levelled.wav@3.50@4.00@156.0@148.0@176.4" "5Te9AlCfhto-levelled.wav@4.00@4.50@168.0@159.6@192.7" "5Te9AlCfhto-levelled.wav@4.50@5.00@183.5@174.3@205.0" "5Te9AlCfhto-levelled.wav@5.00@5.50@195.0@185.2@220.4" "5Te9AlCfhto-levelled.wav@5.50@6.00@209.9@199.4@238.7" "5Te9AlCfhto-levelled.wav@6.00@6.50@227.3@216.0@257.1" "5Te9AlCfhto-levelled.wav@6.50@7.00@244.8@232.6@276.7" "5Te9AlCfhto-levelled.wav@7.00@7.50@263.5@250.3@295.0" "5Te9AlCfhto-levelled.wav@7.50@8.00@280.9@266.9@316.8" "5Te9AlCfhto-levelled.wav@8.00@8.50@301.8@286.7@337.4" "5Te9AlCfhto-levelled.wav@8.50@9.00@321.4@305.3@358.9" "5Te9AlCfhto-levelled.wav@9.00@9.50@341.8@323.9@376.5" "5Te9AlCfhto-levelled.wav@9.50@10.00@358.6@340.7@399.9" "5Te9AlCfhto-levelled.wav@10.00@10.50@380.9@361.8@418.1" "5Te9AlCfhto-levelled.wav@10.50@11.00@398.2@378.3@438.7" "5Te9AlCfhto-levelled.wav@11.00@11.50@417.8@396.9@458.8" "5Te9AlCfhto-levelled.wav@11.50@12.00@436.9@415.1@477.4" "5Te9AlCfhto-levelled.wav@12.00@12.50@454.6@431.9@496.0" "5Te9AlCfhto-levelled.wav@12.50@13.00@472.4@448.7@514.3" "5Te9AlCfhto-levelled.wav@13.00@13.50@489.8@465.3@532.7" "5Te9AlCfhto-levelled.wav@13.50@14.00@507.3@482.0@551.9" "5Te9AlCfhto-levelled.wav@14.00@14.50@525.7@499.4@565.9" "5Te9AlCfhto-levelled.wav@14.50@15.00@539.0@512.0@584.7" "5Te9AlCfhto-levelled.wav@15.00@15.50@556.9@529.0@601.0" "5Te9AlCfhto-levelled.wav@15.50@16.00@572.4@543.8@612.8"
    python tools/make-engine-bank.py game/assets/audio/v8flat/engine_overrun 8 "5Te9AlCfhto.opus@19.60@20.30@534.8@499.0@561.5" "5Te9AlCfhto.opus@20.30@21.00@527.2@488.6@553.6" "5Te9AlCfhto.opus@21.00@21.70@514.3@478.3@540.0" "5Te9AlCfhto.opus@22.40@23.10@406.7@377.2@427.1" "5Te9AlCfhto.opus@23.10@23.80@397.1@368.7@416.9" "5Te9AlCfhto.opus@23.80@24.50@388.1@355.9@407.5" "5Te9AlCfhto.opus@24.50@25.20@374.6@341.2@393.4" "5Te9AlCfhto.opus@25.20@25.90@359.2@328.0@377.2"
    python tools/make-engine-bank.py game/assets/audio/inline5/engine_idle 5 "MH4ek7tBg6I.opus@71.5@76.0@45.5@40@51@2"
    python tools/make-engine-bank.py game/assets/audio/inline5/engine_drive 5 "MH4ek7tBg6I-levelled.wav@36.92@37.43@64.1@62.1@72.1" "MH4ek7tBg6I-levelled.wav@37.43@37.95@69.1@65.6@78.1" "MH4ek7tBg6I-levelled.wav@37.95@38.47@74.4@70.7@83.4" "MH4ek7tBg6I-levelled.wav@38.47@38.98@79.4@76.0@89.1" "MH4ek7tBg6I-levelled.wav@38.98@39.50@85.6@81.3@96.4" "MH4ek7tBg6I-levelled.wav@39.50@40.02@91.8@87.2@103.4" "MH4ek7tBg6I-levelled.wav@40.02@40.53@98.5@94.3@110.2" "MH4ek7tBg6I-levelled.wav@40.53@41.05@105.6@100.3@117.3" "MH4ek7tBg6I-levelled.wav@41.05@41.57@111.7@106.1@124.0" "MH4ek7tBg6I-levelled.wav@41.57@42.08@118.1@112.8@129.8" "MH4ek7tBg6I-levelled.wav@42.08@42.60@124.1@117.9@135.8" "MH4ek7tBg6I-levelled.wav@172.50@173.01@102.6@93.9@107.7" "MH4ek7tBg6I-levelled.wav@173.01@173.53@98.9@94.9@108.7" "MH4ek7tBg6I-levelled.wav@173.53@174.04@104.0@95.7@116.0" "MH4ek7tBg6I-levelled.wav@174.04@174.56@111.3@105.7@128.4" "MH4ek7tBg6I-levelled.wav@174.56@175.07@122.2@116.8@139.8" "MH4ek7tBg6I-levelled.wav@175.07@175.59@133.1@127.9@154.3" "MH4ek7tBg6I-levelled.wav@175.59@176.10@148.8@141.4@174.4" "MH4ek7tBg6I-levelled.wav@176.10@176.61@166.1@157.8@192.3" "MH4ek7tBg6I-levelled.wav@176.61@177.13@183.1@175.6@209.9" "MH4ek7tBg6I-levelled.wav@177.13@177.64@201.6@191.5@227.4" "MH4ek7tBg6I-levelled.wav@177.64@178.16@218.4@207.5@247.0" "MH4ek7tBg6I-levelled.wav@178.16@178.67@235.3@225.0@263.5" "MH4ek7tBg6I-levelled.wav@178.67@179.19@250.9@239.8@279.8" "MH4ek7tBg6I-levelled.wav@179.19@179.45@268.0@254.6@297.6"
    python tools/make-engine-bank.py game/assets/audio/inline5/engine_overrun 5 "MH4ek7tBg6I.opus@258.60@259.09@212.6@197.2@223.3" "MH4ek7tBg6I.opus@259.09@259.57@206.7@189.7@217.0" "MH4ek7tBg6I.opus@259.57@260.06@200.5@182.2@210.4" "MH4ek7tBg6I.opus@260.06@260.54@191.8@174.0@200.3" "MH4ek7tBg6I.opus@260.54@261.03@183.3@167.7@192.6" "MH4ek7tBg6I.opus@261.03@261.51@176.2@159.9@185.0" "MH4ek7tBg6I.opus@261.51@262.00@168.4@150.1@177.0" "MH4ek7tBg6I.opus@262.00@262.49@158.0@140.7@165.9" "MH4ek7tBg6I.opus@262.49@262.97@147.2@132.2@154.6" "MH4ek7tBg6I.opus@262.97@263.46@139.2@123.6@145.0" "MH4ek7tBg6I.opus@263.46@263.94@130.1@117.7@135.5" "MH4ek7tBg6I.opus@263.94@264.43@123.2@113.6@129.3" "MH4ek7tBg6I.opus@264.43@264.91@119.2@110.2@125.1" "MH4ek7tBg6I.opus@264.91@265.40@116.0@106.6@121.4" "MH4ek7tBg6I.opus@292.60@293.09@184.6@172.4@193.9" "MH4ek7tBg6I.opus@293.09@293.58@181.2@167.9@190.3" "MH4ek7tBg6I.opus@293.58@294.08@176.1@161.1@184.9" "MH4ek7tBg6I.opus@294.08@294.57@169.2@157.2@177.7" "MH4ek7tBg6I.opus@294.57@295.06@165.5@152.5@173.9" "MH4ek7tBg6I.opus@295.06@295.55@160.6@147.5@168.1" "MH4ek7tBg6I.opus@295.55@296.05@155.2@142.8@161.8" "MH4ek7tBg6I.opus@296.05@296.54@149.6@138.0@157.5" "MH4ek7tBg6I.opus@296.54@297.03@144.8@134.0@152.1" "MH4ek7tBg6I.opus@297.03@297.52@140.8@129.7@147.8" "MH4ek7tBg6I.opus@297.52@298.02@136.5@126.8@143.0" "MH4ek7tBg6I.opus@298.02@298.51@133.5@124.3@139.8" "MH4ek7tBg6I.opus@298.51@299.00@130.8@122.0@137.1" "MH4ek7tBg6I.opus@139.60@140.08@141.8@133.4@149.1" "MH4ek7tBg6I.opus@140.08@140.57@139.9@131.4@147.2" "MH4ek7tBg6I.opus@140.57@141.05@138.3@128.2@144.5" "MH4ek7tBg6I.opus@141.05@141.53@134.9@125.3@141.7" "MH4ek7tBg6I.opus@141.53@142.02@130.9@121.3@137.8" "MH4ek7tBg6I.opus@142.02@142.50@127.7@117.5@133.2" "MH4ek7tBg6I.opus@142.50@142.98@123.6@115.3@129.8" "MH4ek7tBg6I.opus@142.98@143.47@121.2@113.7@127.6" "MH4ek7tBg6I.opus@143.47@143.95@119.7@111.9@126.0" "MH4ek7tBg6I.opus@143.95@144.43@117.8@110.9@124.1" "MH4ek7tBg6I.opus@144.43@144.92@116.9@109.2@122.8" "MH4ek7tBg6I.opus@144.92@145.40@115.0@107.2@120.5"
    python tools/make-engine-bank.py game/assets/audio/four16v/engine_idle 4 "wKeTXxwpkS0-clean.wav@7.0@16.5@27.8@26@30@6"
    python tools/make-engine-bank.py game/assets/audio/four16v/engine_drive 4 "wKeTXxwpkS0.opus@469.60@470.10@48.1@44.7@70.0" "wKeTXxwpkS0.opus@470.10@470.60@65.4@60.8@93.3" "wKeTXxwpkS0.opus@470.60@471.10@87.2@81.1@118.2" "wKeTXxwpkS0.opus@471.10@471.60@110.5@102.8@145.2" "wKeTXxwpkS0.opus@471.60@472.10@135.7@126.2@177.9" "wKeTXxwpkS0.opus@472.10@472.60@166.3@154.6@202.3" "wKeTXxwpkS0.opus@473.80@474.28@133.0@114.6@142.3" "wKeTXxwpkS0.opus@474.28@474.75@129.9@120.9@149.3" "wKeTXxwpkS0.opus@474.75@475.23@139.5@129.8@160.5" "wKeTXxwpkS0.opus@475.23@475.70@151.1@140.5@172.7" "wKeTXxwpkS0.opus@475.70@476.18@161.4@150.1@182.7" "wKeTXxwpkS0.opus@476.18@476.65@172.0@160.0@194.7" "wKeTXxwpkS0.opus@476.65@477.12@181.9@169.2@204.8" "wKeTXxwpkS0.opus@477.12@477.60@191.4@179.3@216.5" "wKeTXxwpkS0.opus@478.80@479.31@143.3@132.5@159.0" "wKeTXxwpkS0.opus@479.31@479.82@148.6@138.9@163.8" "wKeTXxwpkS0.opus@479.82@480.33@153.1@142.6@167.7" "wKeTXxwpkS0.opus@480.33@480.84@157.1@146.1@171.6" "wKeTXxwpkS0.opus@480.84@481.35@160.8@149.5@175.5" "wKeTXxwpkS0.opus@481.35@481.85@164.5@153.0@179.5" "wKeTXxwpkS0.opus@481.85@482.36@167.7@156.3@183.2" "wKeTXxwpkS0.opus@482.36@482.87@171.2@159.7@186.6" "wKeTXxwpkS0.opus@482.87@483.38@174.4@162.7@190.4" "wKeTXxwpkS0.opus@483.38@483.89@178.4@165.9@194.1" "wKeTXxwpkS0.opus@483.89@484.40@181.6@168.9@198.5" "wKeTXxwpkS0.opus@490.20@490.71@123.6@115.0@135.8" "wKeTXxwpkS0.opus@490.71@491.22@126.9@118.5@141.3" "wKeTXxwpkS0.opus@491.22@491.74@132.0@123.1@146.1" "wKeTXxwpkS0.opus@491.74@492.25@137.2@127.5@152.2" "wKeTXxwpkS0.opus@492.25@492.76@142.3@132.3@158.1" "wKeTXxwpkS0.opus@492.76@493.27@147.7@137.9@163.6" "wKeTXxwpkS0.opus@493.27@493.79@152.9@142.6@169.6" "wKeTXxwpkS0.opus@493.79@494.30@158.9@147.7@175.8" "wKeTXxwpkS0.opus@494.30@494.81@164.3@152.8@181.0" "wKeTXxwpkS0.opus@494.81@495.32@169.2@157.6@186.1" "wKeTXxwpkS0.opus@495.32@495.84@173.9@162.2@191.1" "wKeTXxwpkS0.opus@495.84@496.35@178.9@166.4@196.7" "wKeTXxwpkS0.opus@496.35@496.86@183.8@171.0@201.5" "wKeTXxwpkS0.opus@496.86@497.38@188.4@175.4@206.3" "wKeTXxwpkS0.opus@497.38@497.89@192.8@179.6@210.8" "wKeTXxwpkS0.opus@497.89@498.40@197.5@183.7@216.0"
    python tools/make-engine-bank.py game/assets/audio/four16v/engine_overrun 4 "425869.mp3@9.40@9.90@138.9@129.2@149.6" "425869.mp3@9.90@10.40@139.2@118.1@149.0" "425869.mp3@10.40@10.90@127.0@94.5@135.9" "425869.mp3@10.90@11.40@101.7@74.3@108.8" "425869.mp3@11.40@11.90@79.9@55.8@85.5" "425869.mp3@21.00@21.49@117.2@108.6@125.7" "425869.mp3@21.49@21.98@116.6@100.6@124.8" "425869.mp3@21.98@22.47@111.6@94.4@119.4" "425869.mp3@22.47@22.96@101.7@85.2@108.4" "425869.mp3@22.96@23.45@91.6@73.0@96.8" "425869.mp3@23.45@23.95@78.5@65.6@83.2" "425869.mp3@23.95@24.44@69.5@55.4@74.4" "425869.mp3@24.44@24.93@58.4@45.2@62.5" "425869.mp3@33.60@34.09@194.5@159.3@211.4" "425869.mp3@34.09@34.57@168.1@129.5@179.8" "425869.mp3@34.57@35.06@139.2@107.2@146.2" "425869.mp3@35.06@35.54@115.3@87.4@120.3" "425869.mp3@35.54@36.03@91.7@67.0@98.2" "425869.mp3@36.03@36.51@69.7@48.6@74.5" "425869.mp3@44.60@45.13@189.0@162.2@202.3" "425869.mp3@45.13@45.66@172.8@139.0@184.9" "425869.mp3@45.66@46.19@149.5@114.6@156.3" "425869.mp3@46.19@46.71@120.8@88.7@129.2" "425869.mp3@46.71@47.24@95.4@65.0@99.5" "425869.mp3@57.00@57.49@125.4@116.6@135.3" "425869.mp3@57.49@57.97@126.2@114.1@135.1" "425869.mp3@57.97@58.46@124.3@112.0@133.6" "425869.mp3@58.46@58.94@120.4@108.6@128.6" "425869.mp3@58.94@59.43@116.3@103.9@124.4" "425869.mp3@59.43@59.91@111.3@100.4@119.1" "425869.mp3@59.91@60.40@107.9@97.0@115.0" "425869.mp3@60.40@60.89@104.3@93.8@111.6" "425869.mp3@60.89@61.37@100.6@90.5@107.6" "425869.mp3@61.37@61.86@97.3@86.8@103.7" "425869.mp3@61.86@62.34@93.4@83.7@99.5" "425869.mp3@62.34@62.83@89.7@80.5@95.9" "425869.mp3@62.83@63.31@86.3@77.2@92.3" "425869.mp3@63.31@63.80@83.0@74.3@88.6"
    python tools/make-engine-bank.py game/assets/audio/tsi4/engine_idle 4 "268622.mp3@492.6@500.1@27@23@31@2" "268622.mp3@500.3@503.5@27@23@31@2"
    python tools/make-engine-bank.py game/assets/audio/tsi4/engine_drive 4 "268622.mp3@31.2@33.3@102@90@130" "268622.mp3@33.4@36.0@124@110@165" "268622.mp3@36.8@41.1@167@150@230" "268622.mp3@16.0@20.1@145@135@215" "268622.mp3@55.0@58.4@165@150@215" "268622.mp3@66.4@71.3@135@120@215"
    python tools/make-engine-bank.py game/assets/audio/tsi4/engine_overrun 4 "268622.mp3@263.0@265.9@167@95@175" "268622.mp3@305.4@307.7@174@130@185" "268622.mp3@20.2@22.9@208@140@220" "268622.mp3@110.2@113.4@188@150@200"
    python tools/make-engine-bank.py game/assets/audio/boxer4/engine_idle 4 "191194.mp3@0.4@2.3@27@22@32@2" "191194.mp3@2.6@3.0@27@22@32@2" "191194.mp3@3.2@4.35@27@22@32@2"
    python tools/make-engine-bank.py game/assets/audio/boxer4/engine_drive 4 "frs-dyno-levelled.wav@3.6@5.0@64.5@60@78.1@2" "frs-dyno-levelled.wav@5.3@6.5@75.4@72@92@2" "frs-dyno-levelled.wav@6.5@8.0@86.3@82@106" "frs-dyno-levelled.wav@8.0@9.0@102@94.9@120.9" "frs-dyno-levelled.wav@9.0@10.0@113@105.1@132.7" "frs-dyno-levelled.wav@10.0@11.0@124@115.3@141.2" "frs-dyno-levelled.wav@11.0@12.0@132@128@145" "frs-dyno-levelled.wav@12.0@13.0@140@130.2@161" "frs-dyno-levelled.wav@13.0@14.0@150.5@145@166" "frs-dyno-levelled.wav@14.0@15.0@161@149.7@182.4" "frs-dyno-levelled.wav@15.0@16.0@170.5@165@185" "frs-dyno-levelled.wav@16.0@17.0@180@175@195" "frs-dyno-levelled.wav@17.0@18.0@189.5@184@206" "frs-dyno-levelled.wav@18.0@19.0@199@185.1@224.7" "frs-dyno-levelled.wav@19.0@20.0@210@195.3@236.5" "frs-dyno-levelled.wav@20.0@21.0@221@205.5@245.5" "frs-dyno-levelled.wav@21.0@22.0@229.4@213.3@254.4" "frs-dyno-levelled.wav@22.0@23.2@237.8@221.2@267.5"
    python tools/make-engine-bank.py game/assets/audio/boxer4/engine_overrun 4 "183654.mp3@24.1@25.9@245@125@250"

The Freesound files are the high-quality previews (MP3s, downloadable without a login; the originals need one). The
Wikimedia Commons files are the uploads themselves (Opus, 48 kHz stereo, as Commons serves them; ffmpeg 9.0.1 decodes
them to the bytes above). SHA-256 of the four recordings new in cars13 audio2, each fetched from its page on
2 November 2026 under a descriptive User-Agent and the same as the copies the research had saved:
`5Te9AlCfhto.opus` 463741e08854f505b42e4850215a52ed22c26f0aa34277d8ebcef83e26df70a8,
`MH4ek7tBg6I.opus` 318312b12ae36ff605c8cc6809f445a7701812481839d1a3ac1945f06fb1ba5e,
`wKeTXxwpkS0.opus` a38c2cbbda980b6d2611dc7a0c7b772207257a88743f8fd8f88573ef875eedf1,
`425869.mp3` 99dac3fd64767b45de25d20ad6bf49e1ca832f44b11c7b9b52c7c7c1728a58d4.

**Why these three were cut again (cars13 audio2).** Until audio2 the 458's run-up and overrun were the CC0 item's `3
Ferrari 458`, the HGT's run-up and overrun its `honda s2000` laid at a five's firing, and all three of the 1.6's banks its
`mazda MX 5` (above, on the item's provenance). Each is now cut from a recording whose maker describes the car, the
engine and how it was recorded: three Wikimedia Commons uploads of YouTube videos' soundtracks and one Freesound
recording. Commons' "engine and exhaust sound (YouTube ...)" files carry the YouTube licence of their video, and their
Commons licence review is still pending; each video's own page was read on 2 November 2026 (its "License" row: "Creative
Commons Attribution license (reuse allowed)"; a non-CC control video shows no such row). The Commons pages name the
author and link the video, and each video describes its own recording (an exhaust maker's car on a dynamometer, a
microphone on a TT RS's number plate, an owner's MX-5): the uploaders are plausibly the authors of their soundtracks.

**The 458 (`v8flat`).** The run-up and the overrun are an F430 Scuderia's on a chassis dynamometer: FiExhaust's video
"Ferrari F430 Scuderia Bumper Conversion w/ Fi EXHAUST Dual Exit X PP-Performance Dyno X HitZ" (published 6 July 2022,
52 s; https://www.youtube.com/watch?v=5Te9AlCfhto), the soundtrack uploaded to Commons as "Ferrari F430 Scuderia with Fi
Exhaust system - engine and exhaust sound (YouTube 5Te9AlCfhto).opus", CC BY 3.0, credit FiExhaust. The F430 Scuderia's
F136 E is a 4.3 flat-plane V8 (510 PS at 8,500 rpm), the 458's F136 FB its 4.5 successor (570 PS at 9,000). The track
holds two full-throttle pulls cut together (an edit at 26.0 s): the first, 2.0-16.0 s from 1,950 to 8,770 rpm, is the
run-up; the coast-downs after it, 19.6-21.7 s (8,020 to 7,550 rpm) and 22.4-25.9 s after a change of gear (6,100 to 5,160),
the overrun; the second pull (28.0-41.6 s) is not needed. Cut in half-second stretches searched about 5 % either side of
the line (the research's one-second stretches with 7 % bands strayed onto a neighbouring line, up to 13 % off).

- *Levelled, and from 2.5 s.* Cut as it is, the pull's level rises within its stretches (the voice's trend took 1.9 dB
  an octave, `GrainBank.LevelTrend`), and its first half second, 1,950-2,040 rpm as the throttle opens, is its darkest
  (7.3 dB under the bank's trend of brightness, 3.2 dB under after the voice's largest tilt): the voice then stepped 1.2
  LU at 2,100 rpm outside, and full throttle at the idle revs was 0.5 LU quieter than the governor's (the test asks 2.5
  louder). The pull is levelled over half-second windows (`level-recording.py`, as the BRZ's run is over one-second
  windows; a half second holds 65 pulses at 2,000 rpm, so the cylinders' own pulses against their neighbours stay) and
  the run-up starts at 2.5 s (2,020 rpm): its trend's slope is then 0.2 dB an octave, full throttle at the idle revs 3.4
  LU over the governor's and the level steps 1.0 LU at most (the 458 before audio2: 3.9 and 1.0). Levelled over one-second
  windows the slope stayed 1.3 (a one-second window barely moves a half-second stretch); from 2.0 s, levelled, the step was
  1.4 LU and the rise 2.0.
- *Caveats.* The soundtrack is held by a limiter: 242 decoded samples past full scale across the file (none in a bank:
  each segment is brought to one loudness and the bank peaks under full scale), and the coast-down within about 3 dB of
  the pull, so the overrun is levelled onto the run-up by the voice as every overrun is. Its level spans 5.5 dB between
  the 10th and the 90th percentile, a mastered track's spread, though here the source is named and one car throughout.
  The aftermarket valved exhaust lifts the half orders, and a dynamometer's rollers are under it: it is a louder,
  rawer 4.3 than a stock 458. No music and no speech. The 360's idle (#241083) stays: the F430 track has no idle.
- Checked and not used: Freesound #43484 (an F355 revved standing, binaural, a narrow range on the older F129), Commons'
  "Ferrari 430 Spider with Capristo exhaust" and "Ferrari 458 Spider with Capristo exhaust" (onboard, wind, 2,000 and
  11,326 samples past full scale), Freesound #370278 (biholao: no model, stitched from many passes), Edvvc's Goodwood
  clips (CC BY-SA) and a California T (the turbocharged F154). The 458 Speciale and the F430 Spider on Commons were not
  fetched (Commons' upload server limited the research's requests).

**The Bravo HGT's five (`inline5`).** All three banks are a real five's: Speedster Turbo Berlin's "Audi TT RS '18 Exhaust
Sound" (published 26 December 2017, 432 s; https://www.youtube.com/watch?v=MH4ek7tBg6I), on Commons as "Audi TT RS -
engine and exhaust sound (YouTube MH4ek7tBg6I).opus", CC BY 3.0, credit Speedster Turbo Berlin. The recordist describes
a stock car with the sport exhaust and an external microphone on the rear number plate into a field recorder. Its
engine is the 2.5 TFSI turbocharged five of 400 PS, standing in for the HGT's 2.0 20V naturally aspirated five (155 PS):
the same firing order and 144-degree spacing, a deeper and louder sound with a turbo's hush under it. No naturally
aspirated five under load with a usable licence was found (Commons' Audi 100 quattro NF, the 2.3 NA five, is 46 s
onboard with wind and road noise over the 0-200 Hz its firing lies in). The run-up is a moderate pull, 36.92-42.60 s
(1,560-3,100 rpm), and a hard one, 172.50-179.45 s (2,370-6,590 rpm); the overrun three stretches with the throttle lifted
and the revs falling, 6 to 8 dB under the pulls (258.6-265.4 s from 5,100 to 2,700 rpm, 292.6-299.0 s from 4,430 to
3,080, 139.6-145.4 s from 3,400 to 2,710; whether the throttle is fully shut cannot be told). 10-22 s (a silent edit) and
206-236 s (the cabin's microphone) are left out. Peak -1.6 dBFS, nothing past full scale, its level spanning 19.1 dB (a
field recording's range), its floor about -40 dBFS; road and tyre noise lie under the engine.

- *The idle is the TT RS's own.* 71.5-76.0 s, settled after a coast-down at 1,090 rpm (its firing line 44.6-46.1 Hz in
  every half second; labels 990-1,180 rpm), against the Volvo's #95838 of 0.9-cars13: both pass every check, the TT RS's
  stepping 0.6 and 0.5 LU outside and in the cabin (the Volvo 0.6 and 0.7), sagging 0.7 and 0.8 (0.7 and 1.1) and rising
  7.9 LU from the governor's throttle to full at the idle revs (7.6), and it is one engine with its run-up: the mean third-
  octave spectrum of its grains lies 5.2 dB (RMS over 50 Hz-6.4 kHz) from the run-up's lowest revs, the Volvo's,
  recorded under a bonnet and low-passed at 1.5 kHz against its valve train's ticks, 21.5 dB. It is 4.5 s long (202
  grains) and idles higher than the HGT's 850 rpm, laid 22 % under its revs (the Volvo 16 %). The Volvo is not shipped.
- *Levelled.* Cut as they are, the pulls rise within their stretches (the trend took the 3 dB an octave in full) and
  full throttle at the idle revs was only 2.3 LU over the governor's; each pull is levelled over half-second windows
  (two passes of `level-recording.py`): slope 0, the rise 7.9 LU, the level stepping 0.6 LU at most.
- *The five's beat is the recording's.* The voice weighted each cylinder's pulse of a five (`GranularEngine.FivePattern`,
  meant for the S2000's four laid at a five's firing). On these banks it changed nothing audible: the ring the grains are
  overlap-added in divides their shares back out (`GrainRing.Take`), so the weights only tilted the crossfade between
  neighbouring grains, and the voice with them differed from the voice without by 27 to 32 dB under its level. The TT
  RS's pulses carry the five's beat themselves: held at full throttle every 500 rpm from 2,500 to 6,000, the voice holds
  the cycle's orders that are not the firing's (k = 1-4, 6-9, 11-14 of rpm / 120) 6.2 dB over the half orders between
  them on average, its recording 7.8 (`EngineVoiceTests.The_five_beats_as_its_recording_does`, which holds them within
  2.5 dB). The pattern is gone from the voice; a recording of another engine laid at a five's firing would need a gain
  per pulse outside the ring.
- Checked and not used: Commons' RS3 by Jay Chesterman (cruising with wind rumble, its level held to a 5.9 dB spread, 266
  samples past full scale) and TT RS by L'argus (a launch in the cabin, the level held flat), the Volvo 850 T5 on
  Commons and the archive's "VOLVO850" (CC BY-SA), a V70 D5 and an Audi 80 TDI (diesels), a Focus ST (a four) and
  Freesound #772235 (a phone, the engine unknown).

**The Bravo 1.6 16V (`four16v`).** The idle and the run-up are an MX-5's: Ride in Japan's "MAZDA MX5 Miata 1.6L exhaust
sound sample" (published 18 April 2026, 551 s; https://www.youtube.com/watch?v=wKeTXxwpkS0), on Commons as "Mazda MX-5
Miata - engine and exhaust sound (YouTube wKeTXxwpkS0).opus", credit Ride in Japan. The owner describes his NA6CE with
its stock B6-ZE (1.6 litre, naturally aspirated, sixteen valves), a Fujitsubo header and a custom exhaust. Its licence is
CC BY 4.0: Commons tags it CC BY 3.0 under its template for videos licensed before August 2025, but the video was
published after YouTube's Creative Commons option moved from 3.0 to 4.0 on 1 August 2025 (the template says so itself);
both are CC BY. THIRD_PARTY.md lists the hosting page's CC BY 3.0 first, as the file is fetched from there, and
the video's 4.0 beside it (cars13 review); the credit and the changes it gives meet both. The run-up is its four full-throttle pulls, first gear 469.6-472.6 s (1,440 to
5,670 rpm), second 473.8-477.6 s (3,700 to 6,070), third 478.8-484.4 s (4,280 to 5,570) and 490.2-498.4 s (3,710 to
6,060), in half-second stretches; the idle 7.0-16.5 s, after the start at 4-5 s, about 830 rpm, followed on its sixth
harmonic (on the second the tracker stuck 11 % high). Peak -2.9 dBFS, nothing past full scale; over the driving the
level is held within 2.4 dB, so the soundtrack is processed (the idle lies 29 dB under the pulls). No Fiat or Lancia 16V
running up under load was found; the MX-5's is the mild sixteen-valve 1.6 of the Bravo's kind (the Bravo's 1,581 cc,
the B6-ZE's 1,597 cc), but its header and custom exhaust make it rawer than a stock Bravo.

- *The idle's steady floor taken out* (`clean-recording.py` with its defaults: from 700 Hz up, low-passed at 6 kHz). As
  recorded, its 1-3 kHz band is 13.1 dB of its sound under the rest and steady: folded on its marks it swings 0.12, white
  noise on the same marks 0.09 (the diesel's idle 0.67). Cut into grains, a steady band is laid at every firing like a
  knock (the diesel's tests' control), and the voice idled with 1-3 kHz 4.8 dB under the diesel's, where the diesel's
  knock test asks 6. The file's floor in every bin, its 10th percentile, is that band's level at the idle, and taken out
  it leaves the idle's 1-3 kHz 29.9 dB under the rest (the voice's 31.2 against the diesel's 9.3), and the idle nearer
  its run-up's lowest revs in timbre (6.6 dB RMS over the third octaves, 7.4 as recorded). Only the idle is cut from the
  cleaned copy. A low-pass alone did too little or cost the timbre (at 2.5 kHz the voice idled 5.7 dB under the diesel's
  band; at 2 kHz 7.4, but 10.1 dB from its run-up's lowest revs).
- *The overrun is a Golf's.* Freesound #425869 "Volkswagen Golf V 1.6 FSI engine reverse mono.wav" by Soundholder, CC BY
  3.0 (credit Soundholder), the HQ preview: from Soundholder's own recording library of the car ("Volkswagen Golf V 1.6
  FSI", 37 files recorded onboard and outside); its engine the 1,598 cc naturally aspirated sixteen-valve four of 115 PS,
  the nearest to the Bravo's found. The overrun is the revs falling after each of its five run-ups in reverse gear, 1,460
  to 5,990 rpm, much wider than the MX-5's one lifted stretch (484.7-489.9 s, 5,620 to 3,630 rpm). Peak -5.6 dBFS,
  nothing past full scale, its level spanning 16.5 dB. A Golf's direct-injected 1.6 lifting off behind an MX-5's run-up is
  two engines; the voice levels the overrun onto the run-up as it does every overrun.
- *The MX-5's run-up rasps at every firing.* Folded on its marks its 1-3 kHz swings 0.70 (0.57 to 0.88 in each pull),
  the diesel's run-up 0.24, the other petrol fours' 0.04 to 0.22: the header's and the exhaust's rasp at full load. The
  diesel's knock test no longer compares run-ups on their marks (`DieselCharacterTests`: a knock is the combustion's,
  plainest at an idle and a light throttle, where the 1.6's voice is held 4.6 dB under the diesel's band at 1,500 rpm
  and 21.9 dB under idling). The Golf's run-ups, the one alternative, swing 0.05 but played 1.5 dB under the diesel at
  1,500 rpm on a light throttle (3.5 asked). In the game the rasp shows from behind the car, where the tailpipe's
  shelf lifts the top: 7 ticks in a 40 s lap (a millisecond's high end 12 dB over the 50 ms around it; none on the old
  banks), four of them at the launch; no click and no drop-out.
- Checked and not used: Freesound #106553, a Rover 216 GSi 1.6 16V by Tomlija (CC BY 3.0, the bonnet up, blips standing
  to 7,200 rpm, unloaded), #147243, an Opel Astra 1.6 16V by qubodup (CC BY 4.0, idling at about 860 rpm, recorded at -52
  dBFS), the Civic SI #55385 (revs held standing in a garage), the Scenic #702637 (a turbo, cruising, its level held within
  7 dB), the Elantra #865228 and the Mazda 3 #474486 (idles and standing revs); and before audio2 (cars13 review)
  han1's Mazda 323 (#19024, CC BY 4.0, pulling only from 1,800 to 3,500 rpm), a 4A-GE starting (#509628), a Peugeot 308's
  and a Lotus Elise's blips standing and a Peugeot 206's cabin. The Commons Fiat Bravo 1.4 T-Jet, Alfa 156 2.0 TS, Mazda 2
  1.3 and Alfa 147 1.6 TS were not fetched (Commons' upload server limited the research's requests).

**How the firing lines were confirmed.** Every stretch's labels follow its firing line, told from its neighbours by the
spacing of the lines around it (half the crank's rate times eight for the V8, five for the five, the cam's rate times
four for the 1.6) and checked again on the cut banks by their cycle's orders: each segment resampled on its own labels
at 64 points a cycle (a four-stroke cycle, two turns of the crank) and its spectrum taken over its cycles, so that the
firing falls on order k = cylinders. An engine's sound repeats once a cycle, so it holds only whole orders: labels at
twice the firing would put its lines on the half orders, and at half the firing the odd orders would empty. On the banks:

- the 458's run-up: whole orders 12.4 dB over the half orders; the strongest k = 8 (the firing), 16 and 24, the crank's
  second order (k = 4, half the firing, the flat-plane's fours' own) 11.3 dB under; its overrun 10.9 dB, the firing and
  k = 4 within 1 dB; the run-up reaching 8,770 rpm at the end of a full pull where the F136 E makes its power at 8,500
  (twice the labels would be 17,500 rpm, half 4,400 at the top of a pull through the gear);
- the HGT's run-up: whole orders 15.5 dB over the half orders, the firing's orders 5, 10 and 15 the strongest and the
  five's own between them (12, 13, 14) 10 to 11 dB under, the odd orders 1.5 dB over the even (none would be left at half
  the firing); its overrun 11.3 dB and the same orders; its idle 13.0 dB, the firing strongest, at 1,090 rpm;
- the 1.6's run-up: whole orders 22.3 dB over the half orders, the firing (k = 4) and its harmonics 8 and 12 strongest,
  the crank's first order (k = 2) 26 dB under, as an even four's; the idle 24.9 dB, the same pattern, at 830 rpm, an
  MX-5's idle, where the labels doubled would idle it at 1,660; the shifts keep the gearbox's ratios (1,440 to 5,670 rpm
  in first, 4,280 from 6,070 into third: 0.70, the B6 gearbox's third over second 1.330 / 1.888 = 0.70); the Golf's
  overrun 15.7 dB, the firing strongest.

The research's own check of the run-ups against anchors read off the spectrograms: the 458's labels 0.1 % more than 5 %
off them (at worst 8 %), the HGT's run-up at worst 4 %, the 1.6's 2 %, the overruns at worst 4 % (the 458), 6 % (the HGT,
0.4 % over 5 %) and 6 % (the Golf).

**The Leon Cupra's 2.0 TSI (`tsi4`).** The Golf R of #268622 has the Leon Cupra's engine, the EA888 two-litre turbo four
of the same generation (the Golf R's 300 PS in Europe; 206 kW, 280 PS, in South Africa, where it was recorded, until the
2017 facelift, https://sacarfan.co.za/2014/02/first-drive-volkswagen-golf-r; the 2018 Cupra's 290-300). The run-up is
cut from the lap's full-throttle stretches (their level 8 to 12 dB over the rest), 3,660 to 6,720 rpm, and one stretch
pulling out of a corner on a part throttle, 2,880-3,730 rpm (from 31.2 s: its first 0.8 s, the throttle still opening,
is 5 dB quieter); the lap's other part-throttle stretches (12.0-15.9 and 62.6-66.3 s, 10 dB quieter) are left out. The
lap never pulls hard under 2,900 rpm; its one low pull, out of the pit lane at 467.6-469.6 s (1,620-1,950 rpm), is 21 dB
darker than the run-up's lowest stretch (its energy over 1 kHz against under), where the bank's evening corrects 6 dB at
most, and is left out: under 2,880 rpm the voice crossfades from the idle, as the V8's does from 2,430. On the overrun
the crank's line (half the firing) is the louder: 20.2-22.9 and 110.2-113.4 s are followed on the firing (205 to 145 Hz,
the revs falling from 6,250 in second gear), which continues the run-up before them; 175.5-178.0 s (3,500 rpm held, the
throttle partly open) is left out. The idle is the car's at the lap's end, 808 rpm held as an electronic throttle holds
it. Checked and not used: #268623 "VW Golf GTI Pull Away" by EverydaySounds (CC0: the car passing a microphone outside,
Doppler-shifted), the CC0 item's `volkswagen golf GTI VII` (blips standing), Wikimedia Commons' "Cupra León", "Seat Leon
Cupra R" (the second Leon's 2.0 TFSI EA113) and "Volkswagen Golf GTI - engine and exhaust sound" (YouTube audio, CC BY
3.0, their licence review pending); Freesound's searches for Leon Cupra, Seat Leon engine, Cupra, TSI, 2.0 TSI, EA888,
Audi S3, Octavia RS, Golf GTI engine and Golf R engine found nothing else.

**The BRZ's boxer (`boxer4`).** The FR-S and the GT 86 are the BRZ with other badges, their FA20 the same engine. A
Subaru's boxer burble comes from unequal-length exhaust headers: the cylinders' pulses reach the collector unevenly and
the exhaust beats at the engine's cycle rate (the EJ of the Impreza, the Legacy and the Forester). The FA20's headers
are of equal length and it fires as evenly as an inline four: its idle's lines are the firing's harmonics only
(multiples of 27 Hz, 807 rpm, the second the strongest), where an EJ Forester's idle (#181234 by mario1298, CC0, 1,030
rpm) has a comb every 8.6 Hz, its cycle's. The voice plays it as an even four. The run-up is the FR-S's on the
dynamometer, one gear from 1,900 to 7,490 rpm (the page does not say which; the same author's GT 86, #191194, pulls in
fourth), followed along its firing line: the line runs among others at 1.5 and 2.5 times it (the crank's half orders,
louder in places) and a steady 188 Hz tone of the room, so it was found from the top of the run, where it reaches the
FA20's 7,500 rpm limiter at 250 Hz, and anchored every few seconds down to 2,000 rpm (73, 81, 102, 124, 140, 161, 180,
199, 221 and 242 Hz at 5, 6, 8, 10, 12, 14, 16, 18, 20 and 22.5 s; from 14.6 to 18 s, where the firing line is weakest,
the 1.5 order's peak lies on 1.5 times them within 2 %). Cut at first in six stretches searched over bands up to
50 % wide, the tracker strayed onto the other lines in places (cars13 review): 8 % of its 2,866 labels lay more
than 5 % off the anchors and 3.9 % more than 15 %, at worst 45 % (5.33 s), with 24 and 28 jumps of over
3 % from one pulse to the next in the second and third stretches (0 to 5 in every other run-up). It is cut in
eighteen stretches of one to one and a half seconds, each searched from about 7 % under the line at its start to
7 % over it at its end, the room's tone kept out of the bands it would take (it held the labels at 188 Hz around
15.35 s): 0.5 % of the 2,830 labels lie more than 5 % off and none more than 8 %, with 8 such jumps in all.
5.0-5.3 s is left out: the firing line there is 7 to 20 dB under the 2.5 order, on which the room's tone lies. The run's
first seconds hold 2,000 rpm on a part throttle (5 dB quieter; the throttle opens at 3.6 s) and are left out. Its level
rises 9 to 13 dB an octave up the run (8.8 within the six stretches it was first cut in, 12.8 by one-second RMS from 4
to 23 s: the rollers' load and the room the microphones were in). The voice levels its grains onto a trend whose slope
is the one measured within the recordings' stretches, held between 0 and 3 dB an octave (`GrainBank.LevelTrend`), and
cut from the recording as it is the boxer's took the 3 in full. So do the flat-twelve's, the estate's and the diesel's
run-ups (5.6, 11.4 and 6.0 dB an octave within their stretches; the others' are 0 or nearly, the hot hatch's 0.3), but
the boxer's idle lies the furthest under its run-up of the four (2.75 octaves under its median firing, against 2.40,
1.72 and 1.01), and cut as it was its trend fell 6 dB from the run-up to the idle's revs, the idle was held 6 dB down
pulling, and opening the throttle at the idle revs raised the level 0.9 LU where every other car rises 3 LU or more. It
is cut from a copy levelled over one-second windows (`tools/level-recording.py`; the pulses against their neighbours
untouched): its slope within the stretches is then just under 0 (-0.4 dB an octave, held to 0), and the boxer rises with
the revs by the voice's own law, 3.9 LU from the governor's throttle to full at the idle revs. The overrun is the revs
falling from the limiter after the run. The idle is the GT 86's on the same author's dynamometer before its run through
the gears (its Vortech supercharger is centrifugal and gives next to nothing at idle), the bumps at 2.3-2.55 and
3.05-3.15 s left out; it reads 807 rpm, where the BRZ idles at about 700. Checked and not used: #191194's run itself
(supercharged); the CC0 item's `27 Toyota GT86` (a video's soundtrack, its level held flat by a limiter, blips and
passes) and `21 Subaru Impreza WRX` (an EJ turbo); Freesound's Forester recordings by mario1298 (#181233-#181240, CC0),
"Subaru Impreza STI idling" (#625702, MilanKovanda, CC BY 4.0), a 2003 Impreza (#369797, t33kno, CC0) and a 2003 WRX
(#273334, ulose2piranha, CC0), all EJ engines with unequal headers; Wikimedia Commons' "Subaru BRZ - engine and exhaust
sound" (YouTube audio, CC BY 3.0, its licence review pending), left for a later pass.

## The everyday cars (`saloon/`, `estate/`, 0.10.0)

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `saloon/engine_idle` | Freesound #187029 "car Niva.wav" by dobrymolodec (the Lada Niva's 1.6, of the Fiat-124 family the 1.6 saloon's pushrod four belongs to), https://freesound.org/s/187029/ (CC0 1.0) | 0.3@6.4@27@20@36, 19.75@21.2@27@20@36 |
| `saloon/engine_drive` | Freesound #255122 "A car pulling away on asphalt, Lada 1500 Combi 1981" by YleArkisto, https://freesound.org/s/255122/ (CC BY 4.0) | 11.2@14.8@66@55@140, 17.4@19.2@118@100@150 |
| `saloon/engine_overrun` | Freesound #255126 "A car approaching, stopping, shutting down, Lada 1500 Combi" by YleArkisto, https://freesound.org/s/255126/ (CC BY 4.0) | 10.8@18.0@130@65@150 |
| `estate/engine_idle` | Freesound #141240 "Renault 19 underhood acceleration" and #141245 "Reault 19 driving 2" by lovretta (the carburetted Cleon 1.4 of the same family as the estate's), https://freesound.org/s/141240/ (CC0 1.0) | 141240: 0.6@2.9@27@20@36; 141245: 3.0@7.2@36@28@44 |
| `estate/engine_drive` | Freesound #141245, as above | 10.8@16.2@50@40@105, 25.5@33.3@92@80@160, 121.2@126.8@91@80@170 |
| `estate/engine_overrun` | Freesound #141245, as above | 133.2@139.4@194@95@215 |

No CC0 or CC BY recording of the cars themselves was found (a phone clip of a Renault 12 was too short); the banks
are their engines' closest siblings: the Lada's Fiat-124 four for the saloon's Fiat 131 pushrod four and the Renault
19's carburetted Cleon 1.4 for the estate's.

The fleet saloon played the Punto's and the Bravo's 1.4 T-Jet petrol fours (`fleet/`, Wikimedia Commons, CC BY 3.0, by
Adri Drives and michael maurice) from 0.10 until it became the E 250 CDI, a diesel, and its own banks were cut (below);
those banks are no longer shipped.

## The fleet saloon's diesel (`diesel4/`, diesel6)

| Bank | Source | Segments (start@end s @ firing Hz at start @ search band Hz) |
|---|---|---|
| `diesel4/engine_idle` | Freesound #243773 "Diesel-drive2.wav" and #243783 "Diesel-drive.wav" by fastson, https://freesound.org/s/243773/ and https://freesound.org/s/243783/ (CC0 1.0), notched at 159-166 Hz | #243773 0.5@8.8@26@20@32@2; #243783 28.0@40.0@33.5@28@38@2 (the firing followed on its second harmonic) |
| `diesel4/engine_drive` | the same | #243773 94.4@98.2@45@38@75, 98.45@100.2@61@50@85; #243783 90.3@93.6@54@45@100; #243773 27.4@29.0@58@50@100, 29.0@29.88@91@85@130 |
| `diesel4/engine_overrun` | #243773 (the revs falling slowly after a change up, the level down: the fuel cut) | 32.3@42.0@62@38@70 |

Both recordings are a short drive of one car with a TDI engine (the uploader's tag), recorded with a Sony PCM-M10 and a
Shure SM-58 in the engine compartment (the uploader's description): idling, pulling away, accelerating through the
low gears to about 50 km/h, coasting. The make and model are not given; the firing reads as a four's: at idle the
spectrum is a comb of 6.5 Hz (the camshaft's turn, half the crank's), its strongest lines at 26 and 52 Hz, a four's
firing at 780 rpm and its second harmonic (a three or a five firing at that rate would turn its crank and cam at other
rates than the comb's), and that idle is where the TDIs' sit. Whether its injection is common-rail or by unit injectors
is not said. It is the only legitimately licensed recording found of a car's four-cylinder turbodiesel that holds an
idle, a run-up under load and an overrun, from close to the engine; none of an OM 651 (or of any W212 or W204 CDI) was
found.

The recordings were made at -55 to -60 dBFS, and above about 4 kHz they are the recorder's floor (those bands move by
under 3 dB between the idle and the run-up): cut as they were, the bank tool lifted the floor 40 dB with the engine,
and the grains' sharpest steps stood 2 to 3.6 times their RMS where every other bank's stand at 0.1 to 0.7: hiss at
every firing pulse. Each is first cleaned with `tools/clean-recording.py` (the floor measured in every frequency bin as
its 10th percentile over the file and taken away from 700 Hz up, then a low-pass at 6 kHz; nothing is moved in time),
and the banks are cut from the cleaned files. After it the grains' sharpest steps are 0.3 to 0.9 times their RMS, and
the knock at idle is the recording's: its 1-3 kHz follows the firing (the band's envelope beats 0.54 of its mean at the
firing rate in #243773's idle; in #243783's first idle, 5-21 s, 0.27, with the band above 3 kHz hiss, so that stretch
is not used). The high-quality previews were taken (MP3s of about 190 kbit/s; the WAV originals need a login).
Rebuild:

    python tools/clean-recording.py 243773.mp3 diesel-drive2.wav
    python tools/clean-recording.py 243783.mp3 diesel-drive.wav
    python tools/clean-recording.py 243773.mp3 diesel-drive2-idle.wav 700 6000 159-166
    python tools/clean-recording.py 243783.mp3 diesel-drive-idle.wav 700 6000 159-166
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_idle 4 "diesel-drive2-idle.wav@0.5@8.8@26@20@32@2" "diesel-drive-idle.wav@28.0@40.0@33.5@28@38@2"
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_drive 4 "diesel-drive2.wav@94.4@98.2@45@38@75" "diesel-drive2.wav@98.45@100.2@61@50@85" "diesel-drive.wav@90.3@93.6@54@45@100" "diesel-drive2.wav@27.4@29.0@58@50@100" "diesel-drive2.wav@29.0@29.88@91@85@130"
    python tools/make-engine-bank.py game/assets/audio/diesel4/engine_overrun 4 "diesel-drive2.wav@32.3@42.0@62@38@70"

What the recordings give and what they do not: the idle at 780 and about 1,000 rpm (the game's diesel idles at 750),
the run-up under load from 1,300 to 3,425 rpm in five stretches (#243773's run-up is split at 98.2-98.45 s, where the
firing tracker jumped to a partial for a few pulses, and at 29.0 s, past which it follows the firing only within a
higher band), and the overrun from 2,000 down to 1,150 rpm. The fleet saloon's governor is at 4,800 rpm: past 3,425 the
voice lays the run-up's highest pulses closer, as for the V6 hatch, and lifting from high revs plays the overrun's
highest pulses closer still. A recording of the same kind of engine revving higher under load would serve better.

Both recordings carry a steady tone that does not follow the revs (an auxiliary of the car's): 162.7 Hz idling at 780
rpm in #243773, 161.3 at about 980 in #243783, the strongest line of either idle, level with the second firing
harmonic. Cut into grains and laid at the firing rate it was chopped into a buzz: the voice idled with 160-167 Hz 7.1 dB
over its firing line outside and 8.9 in the cabin, where the petrol cars' sit well under theirs (diesel6 review). The
idle is cut from copies notched at 159-166 Hz (the last argument above: a zero-phase second-order band-stop), and the
voice now idles with that band 10.4 dB under its firing line outside and 8.6 in the cabin
(`The_diesel_idles_without_a_steady_hum`). #243783's fifth firing harmonic (about 163 Hz) goes with it; #243773's
sixth (156 Hz) is kept. The run-up and the overrun are cut from the un-notched files: through them every firing harmonic
passes the band in turn, and a notch would cut each as it went by; the tone is no louder than the harmonics around it
there.

The bank tool's tracker cannot follow a firing frequency under about 45 Hz (1,350 rpm for a four): its search band of
12 % either side is then narrower than a bin of its 4,096-sample frames, often holds none, and the estimate stays where it
was. The run-up starts at 1,300 rpm for that reason, and the voice crossfades the idle into it. The idles are followed
on their second harmonic (the segment's last field, 2: a band twice as wide in hertz), since the steady idle did not
save them from it (diesel6 review): followed on the firing, #243773's idle was labelled 26.00 Hz on every pulse and
#243783's 28.63 Hz on 150 of its 352 (its pulses' spacing gives 30.9 there and 32.6 where the tracker followed, its
spectrum 32.5 to 33), 7 to 12 % slow, with 104 of the bank's grains spanning over 1.4 periods on a side where a pulse was
missed. On the second harmonic the labels' medians are 26.07 and 32.84 Hz, as the spectra and the spacing give (26.0 and
32.5-33; 25.6 and 32.8), and 45 grains span over 1.4 periods.

What was searched (2 November 2026), CC0 and CC BY only:

- the Internet Archive's CC0 item `car-engines` (above): 71 tracks, all petrol sports and muscle cars, no diesel;
- Wikimedia Commons (its search for diesel, TDI, CDI, dCi, HDi, CRDi, W212, W204, E-Class and "engine and exhaust
  sound"; its category "Sounds of automobiles", 456 files): four YouTube "engine and exhaust sound" tracks of diesels or
  likely ones, the "Volkswagen Passat B5 1.9 TDI (remapped, 185 hp)" (tomeko0o3769, CC BY 3.0), an "Audi A3" (Importex
  Cartuning & Sound), a "Seat Toledo" and a "Chevrolet Cruze", all with their licence review pending and edited from
  videos (cuts, drive-bys, blips standing, music over the A3's first half); none holds a steady loaded run-up near the
  engine, and none is an OM 651;
- Freesound (searches for diesel car, TDI, CDI, CDTi, CRDi, dCi, HDi, JTD, Multijet, TDCi, 320d, Mercedes diesel,
  Sprinter, C220, E220, diesel engine bay, diesel clatter, diesel rev): besides fastson's, a Mercedes diesel starting
  outside (#213729, PITFACTORY, CC0; model not given, start and idle), a Mercedes diesel's idle and blips (#399773,
  Iridiuss, CC0), a 2026 Mercedes Sprinter revved standing up to about 2,700 rpm (#873236, Robo9418, CC0; its OM 654 is
  the OM 651's successor, but nothing under load), an Opel Astra 1.9 CDTi started, idled and revved in a garage
  (#28641, digifishmusic, CC BY 4.0), a Citroen Jumpy 2.0 HDi likewise (#500193, chlund, CC0), a BMW 320d passing two
  microphones and revved in neutral (#33459-33461, pempi, CC BY 4.0), recordings in the cabin of a 2007 Passat diesel
  (#89948-89951, Loffy, CC BY 3.0), a Hyundai i40 1.7 CRDi on the motorway (#569683, RadioOAF, CC0), a 1.9 diesel in
  Belgium (#521768 and #521769, gecop, CC0) and a Renault Master dCi 135 (#425851, Soundholder, CC BY 3.0), where the
  road and the wind cover the firing; and phone clips of a Skoda Octavia TDI idling ten metres away (#827862,
  Mihacappy, CC0) and a New Beetle TDI (#448217, Evilmikie, CC0).

## Cows (`cow/moo1.wav` ... `cow/moo5.wav`)

Five moos of one cow recorded by Joseph Sardin (BigSoundBank #2382-#2386 "Cow moos #2"-"#6",
https://bigsoundbank.com/cow-moos-2-s2382.html, CC0 1.0), cut from the FLAC originals with `tools/make-oneshot.py`
(0.35 s fade-out, peak 0.85): 2382 at 0.55 s + 2.0 s, 2383 at 0.45 + 2.3, 2384 at 0.55 + 2.1, 2385 at 0.95 + 2.4, 2386
at 0.35 + 2.1. `WorldAudio` places them on the cows: loud when the car shoves one, softer now and then from one grazing
within 70 m. 1989 mode keeps the cabinet's synthesised moo.

## Instant replay music (`music/replay.ogg`, `music/replay.wav`)

An original composition of this project, written for it in the style of late-1980s arcade music; it is not a
transcription, an arrangement or a sample of any existing piece (the original game's replay music is Atari's and is
neither copied nor used). Nothing in it is recorded: `tools/make-replay-music.py` composes and renders it from its
score and writes both files (and `game/src/ReplayScore.cs`, the same score for the 1989 mode's synthesiser), so it is
under the project's own licence like the code.

Ten seconds in A mixolydian at 136 bpm with swung sixteenths: an intro hit of the band and a tom fill, a four-bar hook
(a syncopated FM lead over slap bass, horn stabs and a clav comp: A, G to D/F#, A, then F and G as band hits) and a
button on A with the brass falling off. The palette follows what is documented of the 1989 cabinet's sound board (a
68000 and a TMS32010 DSP mixing sampled instruments at 20 kHz into a 12-bit AM6012 DAC, mono; MAME's
`atari/harddriv_a.cpp`) and the studio sounds such samples were taken from: four-operator FM in the YM2151's layout for
the brass, the lead, the slap bass and the clav, drum-machine drums rebuilt from oscillators and noise with gated
reverb, an orchestra hit. The mix is then passed through the board as the script models it (20 kHz, truncated to 12
bits, held between samples, the amplifier's low-pass): peak -1 dBFS, -17 LUFS integrated (mono). `replay.ogg` is
Vorbis 44.1 kHz with two identical channels, `replay.wav` mono 22.05 kHz 16-bit for the Unreal version's reader (the
Godot export leaves it out). Rebuild:

    python -I tools/make-replay-music.py [--stems <folder>]

It replaced eleven seconds of the CC0 "Horizon Surfer" by congusbongus (OpenGameArt). Since 0.11 a player
can put a `replay.ogg`, `replay.mp3` or `replay.wav` of their own into the user folder (`user://music/`), which that
copy plays instead; it is never part of the game.

## References (not shipped)

Photographs looked at for the everyday cars' proportions and details, from Wikimedia Commons: "Tofaş Şahin.jpg"
(public domain), "Tofaş Şahin (late), Firüzağa.jpg" (Mr.choppers, CC BY-SA 3.0), "Renault 12TSW Toros front.jpg"
(CC BY-SA 3.0), "Renault 12 Break rear Turkey.JPG" (CC BY-SA 3.0), "2019 Fiat Tipo berlina 4-porte.jpg" (CC BY-SA 4.0),
"2017 Fiat Tipo Sedan on Mt. Tabor, Israel.jpg" (CC BY-SA 4.0), "2018 Fiat Tipo SW S-Design interior.jpg" (CC BY-SA 4.0).

## Tyres and road (`tyres/`)

| Loop | Source | Slice | License |
|---|---|---|---|
| `tyres/road.wav` | Freesound #383476 "Car interior driving 02.wav" by Daphne_in_Wonderland, https://freesound.org/s/383476/ | 40.0 s + 8.0 s | CC0 1.0 |
| `tyres/squeal.wav` | Freesound #850041 "tire-squeal" by JeffPearson, https://freesound.org/s/850041/ | 18.6 s + 3.0 s | CC0 1.0 |
| `tyres/squeal_hard.wav` | Internet Archive item `Sound_Effects_2` ("Sounds Effects 2", Wayzata Technology, 1995), file "squealing tires", https://archive.org/details/Sound_Effects_2 | 2.0 s + 4.0 s | CC0 1.0 (item `licenseurl`) |
| `tyres/skid.wav` | BigSoundBank #2370 "Screeching tires #3", https://bigsoundbank.com/detail-2370-screeching-tires-3.html | 0.35 s + 1.2 s | CC0 1.0 |
| `tyres/gravel.wav` | Freesound #341069 "gravel road" by seth-m, https://freesound.org/s/341069/ | 1.0 s + 4.0 s | CC0 1.0 |

Cut with `tools/make-loop.py` (mono 22.05 kHz, the tail cross-faded into the head so the loop is seamless,
peak-normalised; the road loop with a 0.5 s cross-fade). The game pitches and fades them from the speed and the
slip of the four wheels (`TyreAudio`); the road loop is the tread's roar as heard in a cabin, recorded at about
motorway speed. Wind and bumps are synthesised.

## Traffic (`traffic/drone.wav`)

The drones' engine: 2.4 s of the flat-twelve voice itself (`GranularEngine` on the flat-twelve's banks, held at 3,200
rpm and 0.35 of the throttle, rendered to a WAV by a one-off test of the audio project and, since banks2, from the
F12tdf's run-up: until then the Countach bank's, recorded from the game) looped with a 0.4 s cross-fade by
`tools/make-loop.py` (`make-loop.py drone-raw.wav 0.8 2.4 traffic/drone.wav 0.4 0.7`); no new source material. `WorldAudio` places it on every drone, each a touch off the others in pitch, with
inverse-distance attenuation and the Doppler shift computed from the simulation.

## Train (`train/steam.wav`, `train/whistle.wav`)

| Clip | Source | Slice | License |
|---|---|---|---|
| `train/steam.wav` | Freesound #209972 "Wales steam train.flac" by jrosin (binaural, from the window of a running steam train), https://freesound.org/s/209972/ | 52.0 s + 6.0 s, 0.5 s cross-fade (`make-loop.py`) | CC0 1.0 |
| `train/whistle.wav` | Freesound #407394 "Steam Whistle.wav" by mike_stranks (a vintage traction engine's whistle), https://freesound.org/s/407394/ | 3.7 s + 2.8 s, 0.5 s fade-out (`make-oneshot.py`) | CC0 1.0 |

Both are placed on the locomotive by `WorldAudio`: the loop while the train runs, the whistle twice as it
approaches the crossing (nine and five seconds out).

## Crash (`crash/crash1.wav` … `crash/crash3.wav`)

| Clip | Source | Slice | License |
|---|---|---|---|
| `crash/crash1.wav` | Freesound #592388 "Car Crash (with Glass)" by magnuswaker, https://freesound.org/s/592388/ | 0.0 s + 2.3 s, 0.5 s fade-out | CC0 1.0 |
| `crash/crash2.wav` | Freesound #807438 "car crash short 1" by Logicogonist (made from #237375 below), https://freesound.org/s/807438/ | 0.0 s + 0.6 s, 0.15 s fade-out | CC0 1.0 |
| `crash/crash3.wav` | Freesound #237375 "Car Crash" by squareal, https://freesound.org/s/237375/ | 0.0 s + 1.6 s, 0.4 s fade-out | CC0 1.0 |

Cut with `tools/make-oneshot.py` (peak 0.9) from the high-quality previews. `EngineAudio` plays one at random
per crash with a spread of pitch (x/÷ 1.08) and level (±2 dB) and a thump under it; 1989 mode keeps the
cabinet's burst of noise.

## Exhaust pops (`v8/pop1.wav` … `v8/pop5.wav`, `v8/crackle.wav`)

One-shots cut from Freesound #797835 "Car Antilag (No cleanup, 32float)" by modusmogulus,
https://freesound.org/s/797835/ , licensed **CC0 1.0** (taken from its high-quality preview).

| One-shot | Slice | Fade-out |
|---|---|---|
| `v8/pop1.wav` | 1.400 s + 0.13 s | 0.08 s |
| `v8/pop2.wav` | 1.555 s + 0.13 s | 0.08 s |
| `v8/pop3.wav` | 1.755 s + 0.13 s (a double crack) | 0.08 s |
| `v8/pop4.wav` | 1.960 s + 0.13 s | 0.08 s |
| `v8/pop5.wav` | 2.165 s + 0.13 s | 0.08 s |
| `v8/crackle.wav` | 0.305 s + 0.33 s (a burst of crackles) | 0.10 s |

Cut with `tools/make-oneshot.py` (mono 22.05 kHz, 2 ms ramp in, cosine fade-out so the recording's
background dies with the bang, peak-normalised to 0.8). The V8 fires two to four of them, drawn at random
with a spread of pitch and level, when the driver lifts off above 4800 rpm.
