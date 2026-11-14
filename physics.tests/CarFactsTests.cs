using GrudgeLap.Physics.Native;
using GrudgeLap.Physics.Vehicle;
using Xunit;
using Xunit.Abstractions;

namespace GrudgeLap.Physics.Tests;

/// <summary>
/// The showroom's two lines about a car and the title screen's one (<see cref="CarFacts"/>, 0.79): read from the
/// specification the car is driven by, so what the player is told is what the simulation does.
/// </summary>
public sealed class CarFactsTests
{
    private readonly ITestOutputHelper _out;

    public CarFactsTests(ITestOutputHelper output) => _out = output;

    private static VehicleSpec Car(CarStyle style) => VehicleSpec.Catalogue.Single(spec => spec.Style == style);

    [Fact]
    public void Every_car_of_the_catalogue_has_both_lines()
    {
        foreach (var spec in VehicleSpec.Catalogue)
        {
            var layout = CarFacts.LayoutLine(spec);
            var figures = CarFacts.FiguresLine(spec);
            _out.WriteLine($"{spec.Name}\n  {layout}\n  {figures}");
            Assert.Contains("CYLINDERS", layout);
            Assert.Contains("-SPEED ", layout);
            Assert.Contains("FRONT / REAR", layout);
            Assert.Matches(@"^\d+ HP AT \d+ RPM", figures);
            Assert.Contains(" NM AT ", figures);
            Assert.Contains(" KG", figures);
            Assert.Contains("TYRES ", figures);
            // The lines are printed in the showroom's panel on one row each: 1600 units wide at the type's size.
            Assert.InRange(layout.Length, 40, 150);
            Assert.InRange(figures.Length, 40, 150);
        }
    }

    [Fact]
    public void The_year_is_read_from_the_name()
    {
        foreach (var spec in VehicleSpec.Catalogue)
        {
            var year = CarFacts.Year(spec);
            if (!spec.Name.Contains('\''))
            {
                Assert.Equal("", year);
                continue;
            }
            var number = int.Parse(year);
            Assert.InRange(number, 1960, 2030);
            Assert.StartsWith(year, CarFacts.LayoutLine(spec));
        }
        Assert.Equal("", CarFacts.Year(new VehicleSpec { Name = "A CAR WITHOUT A YEAR" }));
        Assert.Equal("", CarFacts.Year(new VehicleSpec { Name = "CUT SHORT '8" }));
        Assert.Equal("1984", CarFacts.Year(new VehicleSpec { Name = "COUPE '84" }));
        Assert.Equal("2018", CarFacts.Year(new VehicleSpec { Name = "HATCH '18" }));
    }

    [Fact]
    public void The_engine_s_place_follows_the_weight_on_the_axles()
    {
        // The flat-twelve carries most of its weight on the rear axle: its engine is behind the seats.
        var flatTwelve = Car(CarStyle.FlatTwelve);
        Assert.True(CarFacts.FrontWeightShare(flatTwelve) < 0.5f);
        Assert.Contains("ENGINE BEHIND THE SEATS", CarFacts.LayoutLine(flatTwelve));
        Assert.Contains("12 CYLINDERS", CarFacts.LayoutLine(flatTwelve));
        Assert.Contains("REAR-WHEEL DRIVE", CarFacts.LayoutLine(flatTwelve));
        var hotHatch = Car(CarStyle.HotHatch);
        Assert.True(CarFacts.FrontWeightShare(hotHatch) > 0.5f);
        Assert.Contains("FRONT ENGINE", CarFacts.LayoutLine(hotHatch));
        Assert.Contains("FRONT-WHEEL DRIVE", CarFacts.LayoutLine(hotHatch));
    }

    [Fact]
    public void Induction_drive_and_gearbox_are_named_as_the_car_has_them()
    {
        Assert.Contains("TURBO-DIESEL", CarFacts.LayoutLine(Car(CarStyle.Fleet)));
        var rally = CarFacts.LayoutLine(Car(CarStyle.FiveHatch));
        Assert.Contains("FOUR-WHEEL DRIVE", rally);
        Assert.Contains("TURBO", rally);
        Assert.DoesNotContain("DIESEL", rally);
        // The boxer coupe is the owner's automatic (0.76): the line names the box its AUTOMATIC drives through.
        var boxer = Car(CarStyle.Boxer);
        Assert.NotNull(boxer.Automatic);
        Assert.Equal($"{boxer.Automatic!.ForwardGearCount}-SPEED AUTOMATIC", CarFacts.Gearbox(boxer));
        Assert.EndsWith("MANUAL", CarFacts.Gearbox(Car(CarStyle.FlatTwelve)));
    }

    [Fact]
    public void The_figures_are_the_torque_curve_s_peaks()
    {
        foreach (var spec in VehicleSpec.Catalogue)
        {
            var curve = spec.Engine.FullThrottleCurve;
            var power = curve.Max(p => p.Rpm * p.Torque) / 7023f;
            var torque = curve.Max(p => p.Torque);
            var figures = CarFacts.FiguresLine(spec);
            Assert.StartsWith($"{power:0} HP AT ", figures);
            Assert.Contains($"{torque:0} NM AT ", figures);
            Assert.Contains($"{spec.MassKg:0} KG", figures);
        }
    }

    [Fact]
    public void The_title_screen_s_line_is_short_and_names_power_mass_drive_and_gears()
    {
        foreach (var spec in VehicleSpec.Catalogue)
        {
            var line = CarFacts.TitleLine(spec);
            _out.WriteLine($"{spec.Name}: {line}");
            var power = spec.Engine.FullThrottleCurve.Max(p => p.Rpm * p.Torque) / 7023f;
            Assert.StartsWith($"{power:0} HP", line);
            Assert.Contains($"{spec.MassKg:0} KG", line);
            Assert.Contains("-WHEEL DRIVE", line);
            Assert.EndsWith($"{spec.ForwardGearCount} GEARS", line);
            Assert.Equal(spec.Engine.BoostShare > 0f, line.Contains("TURBO"));
            // Under the car's name on the title screen, with the keys' hint after it on the same row.
            Assert.InRange(line.Length, 30, 70);
        }
    }

    [Fact]
    public void The_c_interface_hands_out_the_same_lines()
    {
        for (var car = 0; car < NativeApi.CarCount; car++)
        {
            Assert.Equal(CarFacts.LayoutLine(VehicleSpec.Catalogue[car]), NativeApi.CarFactLine(car, 0));
            Assert.Equal(CarFacts.FiguresLine(VehicleSpec.Catalogue[car]), NativeApi.CarFactLine(car, 1));
            Assert.Equal(CarFacts.TitleLine(VehicleSpec.Catalogue[car]), NativeApi.CarFactLine(car, 2));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.CarFactLine(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.CarFactLine(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeApi.CarFactLine(NativeApi.CarCount, 0));
    }
}
