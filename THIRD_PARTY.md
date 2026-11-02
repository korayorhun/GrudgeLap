# Third-party components

| Component | Use | License |
|---|---|---|
| Godot Engine 4.7 (.NET edition) | game engine, shipped with the game | MIT — https://godotengine.org/license/ |
| .NET 8 runtime | shipped next to the exe (`data_GrudgeLap_windows_x86_64`) | MIT |
| Godot.NET.Sdk | build integration | MIT |
| "Car Engine Sounds" (Internet Archive item `car-engines`) | the engines' run-up grain banks, cut from the `3 Ferrari 458` (the V8's and the 458's, `v8/`, `v8flat/`), `15 Lamborghini Countach`, `honda civic type r`, `25 Nissan Pulsar`, `Jaguar f-type s` (also the V6 hatch's idle and overrun, `v6/engine_*`), `honda s2000` (the HGT's five, `five/engine_drive`, `engine_overrun`) and `mazda MX 5` (the Bravo 1.6 16V's three banks, `four16v/`) tracks (see `game/assets/audio/SOURCES.md`) | CC0 1.0 Universal, as declared by the uploader (the item names no recordist; what it does not say of its tracks' origin is in `game/assets/audio/SOURCES.md`) — https://archive.org/details/car-engines |
| Freesound #585215 "Car Engine: Maserati GranTurismo Switch to Sport Mode and Launch" by myeclecticself | the V8's idle grain bank (`v8/engine_idle`) | CC0 1.0 — https://freesound.org/s/585215/ |
| Freesound #112075 "Lambo idle and rev.mp3" by cheesepuff | the twelve's idle grain bank (`flat12/engine_idle`) | CC0 1.0 — https://freesound.org/s/112075/ |
| getoutlines.com blueprints of a 1984 coupe, the 2000 Clio, the 1995 Bravo, the 2007 Leon, the 1980 Fiat 131 Mirafiori saloon and the 1969 Dacia 1300 Break | the measurements the car lofts are keyed on (`CarView.BodyStyle`); no drawing is shipped | CC BY 4.0 — https://getoutlines.com |
| Wikimedia Commons photographs of the Tofas Sahin, the Renault 12 Toros and the Fiat Tipo saloon (by Mr.choppers, Emre Ayhan and others; listed in `game/assets/audio/SOURCES.md` under "References") | looked at for the proportions and the details of the everyday cars; no photograph is shipped | public domain / CC BY-SA, reference only |
| BigSoundBank "Cow moos #2"-"#6" by Joseph Sardin | the cows' moos (`cow/moo1.wav` ... `moo5.wav`) | CC0 1.0 — https://bigsoundbank.com/cow-moos-2-s2382.html |
| Freesound #187029 "car Niva.wav" by dobrymolodec | the 1.6 saloon's idle (`saloon/engine_idle`) | CC0 1.0 — https://freesound.org/s/187029/ |
| Freesound #255122 and #255126, Lada 1500 Combi recordings by YleArkisto | the 1.6 saloon's run-up and overrun (`saloon/engine_drive`, `engine_overrun`) | CC BY 4.0 — https://freesound.org/s/255122/ (credit: YleArkisto) |
| Freesound #141245 "Reault 19 driving 2" and #141240 "Renault 19 underhood acceleration" by lovretta | the 1.4 estate's banks (`estate/engine_*`) | CC0 1.0 — https://freesound.org/s/141245/ |
| Freesound #243773 "Diesel-drive2.wav" and #243783 "Diesel-drive.wav" by fastson (a TDI's engine bay) | the fleet saloon's diesel banks (`diesel4/engine_*`), cleaned of the recorder's hiss and, for the idle, of a steady 161-163 Hz auxiliary tone (`tools/clean-recording.py`) | CC0 1.0 — https://freesound.org/s/243773/ |
| Freesound #241083 "Ferrari 360 Spider - engine sound" by PritzProductions | the 458's idle (`v8flat/engine_idle`) | CC0 1.0 — https://freesound.org/s/241083/ |
| Freesound #95838 "RG Volvo Engine.wav" by cmusounddesign | the Bravo HGT's five's idle (`five/engine_idle`), low-passed at 1.5 kHz (`tools/clean-recording.py`) | CC BY 4.0 — https://freesound.org/s/95838/ (credit: cmusounddesign) |
| Freesound #268622 "VW Golf R (Zoom H1, OST Lav).wav" by EverydaySounds | the Leon Cupra's 2.0 TSI banks (`tsi4/engine_*`) | CC0 1.0 — https://freesound.org/s/268622/ |
| Freesound #183654 "DynoJet Scion FR-S" by dezoris | the BRZ's run-up and overrun (`boxer4/engine_drive`, levelled by `tools/level-recording.py`, and `engine_overrun`) | CC0 1.0 — https://freesound.org/s/183654/ |
| Freesound #191194 "FR-S BRZ GT-86 Vortech Supercharged Dyno Run" by dezoris | the BRZ's idle (`boxer4/engine_idle`) | CC BY 4.0 — https://freesound.org/s/191194/ (credit: dezoris) |
| ambientCG Ground110 (1K) | the gravel shoulders (`shoulder_*.jpg`) | CC0 1.0 — https://ambientcg.com/view?id=Ground110 |
| Freesound #383476 "Car interior driving 02.wav" by Daphne_in_Wonderland | the tyres' rolling roar on the road (`tyres/road.wav`) | CC0 1.0 — https://freesound.org/s/383476/ |
| Freesound #850041 "tire-squeal" by JeffPearson | the cornering squeal loop (`tyres/squeal.wav`) | CC0 1.0 — https://freesound.org/s/850041/ |
| "Sounds Effects 2" (Internet Archive item `Sound_Effects_2`, Wayzata Technology, 1995), file "squealing tires" | the hard-cornering howl (`tyres/squeal_hard.wav`) | CC0 1.0, as declared in the item's metadata — https://archive.org/details/Sound_Effects_2 |
| BigSoundBank #2370 "Screeching tires #3" | the wheelspin and locked-wheel screech (`tyres/skid.wav`) | CC0 1.0 — https://bigsoundbank.com/detail-2370-screeching-tires-3.html |
| Freesound #341069 "gravel road" by seth-m | the off-road gravel loop (`tyres/gravel.wav`) | CC0 1.0 — https://freesound.org/s/341069/ |
| Freesound #797835 "Car Antilag (No cleanup, 32float)" by modusmogulus | the V8's exhaust pops (`v8/pop1.wav` … `v8/pop5.wav`, `v8/crackle.wav`) | CC0 1.0 — https://freesound.org/s/797835/ |
| Freesound #209972 "Wales steam train.flac" by jrosin | the train's running sound (`train/steam.wav`) | CC0 1.0 — https://freesound.org/s/209972/ |
| Freesound #407394 "Steam Whistle.wav" by mike_stranks | the train's whistle (`train/whistle.wav`) | CC0 1.0 — https://freesound.org/s/407394/ |
| Freesound #592388 "Car Crash (with Glass)" by magnuswaker | a crash (`crash/crash1.wav`) | CC0 1.0 — https://freesound.org/s/592388/ |
| Freesound #807438 "car crash short 1" by Logicogonist | a crash (`crash/crash2.wav`) | CC0 1.0 — https://freesound.org/s/807438/ |
| Freesound #237375 "Car Crash" by squareal | a crash (`crash/crash3.wav`) | CC0 1.0 — https://freesound.org/s/237375/ |
| ambientCG textures Grass004, Asphalt004, Concrete034, Ground037, Bark012, WoodSiding009, RoofingTiles003, Bricks078, MetalPlates006, LeafSet001, Leather037, Carpet016, Fabric030 (1K) | ground, road, shoulders, fields, trees, buildings, the train, the cabin's leather, carpet and roof lining; the leaf-cluster atlas is composited from LeafSet001 (`game/assets/textures/SOURCES.md`) | CC0 1.0 — https://ambientcg.com |
| Poly Haven HDRI "kloofendal_43d_clear_puresky" (2K) | sky, lighting and reflections | CC0 1.0 — https://polyhaven.com |
| "Ferrari Testarossa" by li_osh | the flat-twelve's body (`tools/car_models.json`) | CC BY 4.0 — https://sketchfab.com/3d-models/762c958bd80f4cac8e4dc76f847fca49; modified: re-scaled, re-rigged and given the Esprit's cabin for GrudgeLap. The Sketchfab page has since been taken down; the licence it was published under stands for the copy used |
| "Lotus Esprit Turbo 1983" by esprit3d.website (Shogos_esprit) | the flat-twelve's cabin (mirrored to left-hand drive) | CC BY 4.0 — https://sketchfab.com/3d-models/886bcf8ce52f46e3b36115e01319ec44; modified: re-scaled and re-rigged for GrudgeLap |
| "Mclaren 570S Coupè" by MMC Works (mmcworks) | the V8's body and cabin | CC BY 4.0 — https://sketchfab.com/3d-models/869feee8011d4803894318f6f0ddf2c6; modified: re-scaled and re-rigged for GrudgeLap |
| "Peugeot 205 Gti" by la_freizz | the hot hatch's body and cabin | CC BY 4.0 — https://sketchfab.com/3d-models/b52c8e5e8070463ab1485039246f527c; modified: re-scaled and re-rigged for GrudgeLap |
| "FREE Lancia Delta HF Integrale evo 2" by TARANTULA (begemot.988888) | the rally hatch's body and cabin | CC BY 4.0 — https://sketchfab.com/3d-models/85614131e0dc4613a948472aaa935fc7; modified: re-scaled and re-rigged for GrudgeLap |
| "Golf R32" by Nick Broad (nickbroad) | the V6 hatch's body (with the SAAB 9-5's cabin) | CC BY 4.0 — https://sketchfab.com/3d-models/8171332b39be45e896a7fdf3c3e66354; modified: re-scaled and re-rigged for GrudgeLap |
| "SAAB 9-5 sedan 1997-2001" by blendffnike | the V6 hatch's cabin | CC BY 4.0 — https://sketchfab.com/3d-models/4db0bcbc183a452eb3ef692cce2fe36f; modified: re-scaled and re-rigged for GrudgeLap |
| "Renault 12" by boitaloran | the 1.4 estate's body and cabin (the saloon: the Toros shares everything ahead of the B-pillar) | CC BY 4.0 — https://sketchfab.com/3d-models/f8c26a68a3dd45b492d6d9a33cda6fcd; modified: re-scaled and re-rigged for GrudgeLap |
| "VAZ-2106" by domhathair | the 1990s saloon's body and cabin | CC BY 4.0 — https://sketchfab.com/3d-models/a73b3cdeddd846209bf7c73d5f04d20f; modified: re-scaled, re-rigged and lit for GrudgeLap |
| "Mercedes-Benz E-Class W212" by Peter_D (better_peter) | the fleet saloon's body and cabin | CC BY 4.0 — https://sketchfab.com/3d-models/119c5e10733142b197aa53b86f6aeb04; modified: re-scaled and re-rigged for GrudgeLap |
| xunit, Microsoft.NET.Test.Sdk | tests only, not shipped | Apache-2.0 / MIT |
| Inno Setup 6 | builds the installer, not shipped | Inno Setup License |
| ffmpeg, numpy, Pillow, SciPy | offline tools (grain bank, loop and one-shot cutting, icon, the replay music's render and its Vorbis encoding, the car models' paint masks), not shipped | LGPL/GPL, BSD, MIT-CMU, BSD |

The track, the world and every other mesh in the game are generated at run time by the code in this
repository, but for the cars: the CC BY models above are drawn in their place (fetched from the Objaverse
mirror by `unreal/Scripts/fetch_models.py` and prepared by `tools/prepare_car_models.py` at build time; a build
without them draws the game's own lofted bodies). The only other files brought in are the
CC0 textures, the CC0 sky, the engine grain banks, the tyre and road loops, the exhaust pops, the train's steam
and whistle and the crashes (the grass-tuft atlas, the road
wear sheet and the cloud sheet are drawn by scripts in `tools/`; the drones' engine loop is the game's own
voice recorded). These are the only recorded sound material; they were cut and normalised from the CC0 and CC BY recordings
above (the slices are listed in `game/assets/audio/SOURCES.md`) and carry no other claim. If you own a cleaner recording of a flat-twelve and are willing
to license it CC0 or CC-BY, an issue is welcome.

The instant replay's music (`music/replay.ogg`, `music/replay.wav`) is the project's own: an original composition
written in the style of late-1980s arcade music, not a transcription or arrangement of any existing tune, synthesised
from its score by `tools/make-replay-music.py` (no recording or sample goes into it). It replaced an excerpt of
the CC0 "Horizon Surfer" by congusbongus (OpenGameArt).

The car names ("FLAT-12 GT '84", "V8 GTB '12 SPORT EXHAUST", "HOT HATCH 1.9 8V '88", "RALLY HATCH 2.0 TURBO
4WD '92", "V6 HATCH 3.2 4WD '03", "SALOON 1.6 '91", "ESTATE 1.4 '93", "SALOON 2.1 DIESEL '14") are descriptions of an engine layout, a body style and a year; they are not the
names of any make or model. The lofted bodies are our own, keyed on the proportions of the period cars; the
models drawn in their place show real cars (a Ferrari, a McLaren, a Peugeot, a Lancia, a Volkswagen, a VAZ, a
Renault, a Mercedes-Benz, and cabins of a Lotus and a SAAB), whose names
and designs belong to their makers, as stand-ins for the catalogue's cars.

Not affiliated with, endorsed by, or derived from the assets of any arcade manufacturer or car maker. The
game mechanics it pays homage to are described in expired U.S. patents (5,044,956; 5,005,148; 5,269,687;
5,354,202; 5,577,913) and in the public record.
