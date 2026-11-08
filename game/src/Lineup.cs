using GrudgeLap.Physics.Vehicle;

namespace GrudgeLap.Game;

/// <summary>
/// The cars the player can take (0.70): the catalogue less the four the owner asked to have out until they look like
/// the real ones, the VAZ-2106 that stands in for the 1.6 saloon (its cabin) and the three lofted cars13 bodies (the two
/// Bravos and the Leon Cupra, drawn from blueprints for want of a CC-licensed model).
/// </summary>
/// <remarks>
/// The catalogue keeps them, and its indices stay what the settings and the paint book hold: the physics tests drive
/// every car, the saloon still runs in the traffic (Bootstrap.TrafficCars), and only automation (--car=, --showroom=)
/// can still pick one of them. The title and the showroom step over them; a saved choice of one moves on to the next.
/// </remarks>
internal static class Lineup
{
    private static readonly int CatalogueLength = VehicleSpec.Catalogue.Length;

    /// <summary>The drivable cars, as indices into <see cref="VehicleSpec.Catalogue"/> in its order.</summary>
    public static readonly int[] Drivable = BuildDrivable();

    private static int[] BuildDrivable()
    {
        var catalogue = VehicleSpec.Catalogue;
        return [.. Enumerable.Range(0, catalogue.Length).Where(i => !Withdrawn(catalogue[i].Style))];
    }

    /// <summary>The cars out of the line-up: the saloon's VAZ model and the three lofted bodies.</summary>
    public static bool Withdrawn(CarStyle style) => style is CarStyle.Saloon or CarStyle.HgtFive or CarStyle.HatchSixteen or CarStyle.CupraTsi;

    public static int Count => Drivable.Length;

    public static bool IsDrivable(int index) => System.Array.IndexOf(Drivable, index) >= 0;

    /// <summary>
    /// The drivable car <paramref name="step"/> places on from <paramref name="index"/> in the catalogue's order, round
    /// the end; from a withdrawn car the first drivable one that way counts as the first step. A step of 0 is the car itself.
    /// </summary>
    public static int Step(int index, int step)
    {
        var direction = System.Math.Sign(step);
        var left = System.Math.Abs(step);
        while (left > 0)
        {
            index = ((index + direction) % CatalogueLength + CatalogueLength) % CatalogueLength;
            if (IsDrivable(index)) left--;
        }
        return index;
    }

    /// <summary>The car's place in the line-up from 1, or 0 for a withdrawn car.</summary>
    public static int Number(int index) => System.Array.IndexOf(Drivable, index) + 1;
}
