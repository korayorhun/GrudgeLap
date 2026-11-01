using GrudgeLap.Physics.Track;
using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Physics;

/// <summary>The weather of a credit (0.12): what the road is like under the tyres and what the view draws.</summary>
public enum Weather
{
    /// <summary>A clear day: the circuit as it always was.</summary>
    Dry,
    /// <summary>Overcast and dry: a grey sky and soft light, the road as it always was.</summary>
    Overcast,
    /// <summary>After rain: the sky still grey, the road wet, dark and mirror-like with puddles, the grip down.</summary>
    Wet,
    /// <summary>Raining: the wet road, rain in the air and on the glass, the grip down a little more.</summary>
    Rain,
}

/// <summary>
/// What the weather does to the simulation and to the view. The grip figures follow the measured drop of a road
/// tyre's peak friction on wet asphalt (about a quarter to a third below dry, more in standing water) and on wet
/// grass; the scripted driver plans its corners and braking with the same figures.
/// </summary>
public static class WeatherModel
{
    /// <summary>Multiplier on a surface's peak friction under this weather.</summary>
    public static float Grip(Weather weather, SurfaceType surface) => weather switch
    {
        Weather.Wet => surface == SurfaceType.Grass ? 0.80f : 0.74f,
        Weather.Rain => surface == SurfaceType.Grass ? 0.75f : 0.68f,
        _ => 1f,
    };

    /// <summary>How wet the ground is, 0 dry to 1 streaming: the view's darkening, roughness and puddles.</summary>
    public static float Wetness(Weather weather) => weather switch
    {
        Weather.Wet => 0.85f,
        Weather.Rain => 1f,
        _ => 0f,
    };

    /// <summary>Whether it is raining: rain in the air and on the glass, the rain's sound.</summary>
    public static bool Raining(Weather weather) => weather == Weather.Rain;

    /// <summary>Whether the sky is overcast: the grey sky and the soft light of every weather but a clear day.</summary>
    public static bool Overcast(Weather weather) => weather != Weather.Dry;

    /// <summary>
    /// A credit's weather drawn at random: most days are fine, as the original's always was (dry 55 %, overcast
    /// 20 %, wet 12 %, rain 13 %).
    /// </summary>
    public static Weather Draw(Random random)
    {
        var roll = random.NextDouble();
        return roll < 0.55 ? Weather.Dry : roll < 0.75 ? Weather.Overcast : roll < 0.87 ? Weather.Wet : Weather.Rain;
    }

    /// <summary>The weather's name on the screen; empty for a clear day.</summary>
    public static string Label(Weather weather) => weather switch
    {
        Weather.Overcast => "OVERCAST",
        Weather.Wet => "WET ROAD",
        Weather.Rain => "RAIN",
        _ => "",
    };

    /// <summary>Parses a weather name (dry, overcast, wet, rain), case-insensitively; null for anything else.</summary>
    public static Weather? Parse(string name) => Enum.TryParse<Weather>(name, ignoreCase: true, out var weather) ? weather : null;
}
