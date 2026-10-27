# Third-party components

| Component | Use | License |
|---|---|---|
| Godot Engine 4.7 (.NET edition) | game engine, shipped with the game | MIT — https://godotengine.org/license/ |
| .NET 8 runtime | shipped next to the exe (`data_GrudgeLap_windows_x86_64`) | MIT |
| Godot.NET.Sdk | build integration | MIT |
| "Car Engine Sounds" (Internet Archive item `car-engines`) | the engines' run-up grain banks, cut from the `3 Ferrari 458` and `15 Lamborghini Countach` tracks (see `game/assets/audio/SOURCES.md`) | CC0 1.0 Universal, as declared by the uploader — https://archive.org/details/car-engines |
| Freesound #585215 "Car Engine: Maserati GranTurismo Switch to Sport Mode and Launch" by myeclecticself | the V8's idle grain bank (`v8/engine_idle`) | CC0 1.0 — https://freesound.org/s/585215/ |
| Freesound #112075 "Lambo idle and rev.mp3" by cheesepuff | the twelve's idle grain bank (`flat12/engine_idle`) | CC0 1.0 — https://freesound.org/s/112075/ |
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
| xunit, Microsoft.NET.Test.Sdk | tests only, not shipped | Apache-2.0 / MIT |
| Inno Setup 6 | builds the installer, not shipped | Inno Setup License |
| ffmpeg, numpy, Pillow | offline tools (grain bank, loop and one-shot cutting, icon), not shipped | LGPL/GPL, BSD, MIT-CMU |

Every mesh, the track and all other sounds in the game are generated at run time by the code in this
repository; the only files brought in are the CC0 textures, the CC0 sky, the engine grain banks, the tyre and
road loops, the exhaust pops, the train's steam and whistle and the crashes (the grass-tuft atlas, the road
wear sheet and the cloud sheet are drawn by scripts in `tools/`; the drones' engine loop is the game's own
voice recorded). These are the only recorded sound material; they were cut and normalised from the CC0
recordings above (the slices are listed in `game/assets/audio/SOURCES.md`) and carry no other claim. If you own a cleaner recording of a flat-twelve and are willing
to license it CC0 or CC-BY, an issue is welcome.

The car names ("FLAT-12 GT '84", "V8 GTB '12 SPORT EXHAUST") are descriptions of an engine layout, a body
style and a year; they are not the names of any make or model, and the bodies are our own lofts.

Not affiliated with, endorsed by, or derived from the assets of any arcade manufacturer or car maker. The
game mechanics it pays homage to are described in expired U.S. patents (5,044,956; 5,005,148; 5,269,687;
5,354,202; 5,577,913) and in the public record.
