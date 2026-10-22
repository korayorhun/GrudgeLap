# Third-party components

| Component | Use | License |
|---|---|---|
| Godot Engine 4.7 (.NET edition) | game engine, shipped with the game | MIT — https://godotengine.org/license/ |
| .NET 8 runtime | shipped next to the exe (`data_GrudgeLap_windows_x86_64`) | MIT |
| Godot.NET.Sdk | build integration | MIT |
| "Car Engine Sounds" (Internet Archive item `car-engines`) | engine loops cut from the `3 Ferrari 458` and `15 Lamborghini Countach` tracks (see `game/assets/audio/SOURCES.md`) | CC0 1.0 Universal, as declared by the uploader — https://archive.org/details/car-engines |
| ambientCG textures Grass004, Asphalt004, Concrete034 (1K) | ground, road and concrete surfaces (`game/assets/textures/SOURCES.md`) | CC0 1.0 — https://ambientcg.com |
| Poly Haven HDRI "kloofendal_48d_partly_cloudy_puresky" (2K) | sky, lighting and reflections | CC0 1.0 — https://polyhaven.com |
| xunit, Microsoft.NET.Test.Sdk | tests only, not shipped | Apache-2.0 / MIT |
| Inno Setup 6 | builds the installer, not shipped | Inno Setup License |
| ffmpeg, numpy, Pillow | offline tools (loop cutting, icon), not shipped | LGPL/GPL, BSD, MIT-CMU |

Every mesh, the track and all other sounds in the game are generated at run time by the code in this
repository; the only files brought in are the CC0 textures, the CC0 sky and the engine loops. The loops are
the only recorded sound material; they were cut and normalised from the
CC0 tracks above and carry no other claim. If you own a cleaner recording of a flat-twelve and are willing
to license it CC0 or CC-BY, an issue is welcome.

The car names ("FLAT-12 GT '84", "V8 GTB '12 SPORT EXHAUST") are descriptions of an engine layout, a body
style and a year; they are not the names of any make or model, and the bodies are our own lofts.

Not affiliated with, endorsed by, or derived from the assets of any arcade manufacturer or car maker. The
game mechanics it pays homage to are described in expired U.S. patents (5,044,956; 5,005,148; 5,269,687;
5,354,202; 5,577,913) and in the public record.
