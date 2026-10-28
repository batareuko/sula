using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.OneHp;

namespace AionMeter.Tests;

public class CubeRadarTests
{
    private static CubeRadar Radar() => new([
        new CubeRadar.Cube(2100001, CubeKind.Red),
        new CubeRadar.Cube(2100002, CubeKind.Yellow),
    ]);

    [Fact]
    public void Tracks_cubes_in_view_until_they_die()
    {
        var r = Radar();
        r.Observe(new SelfIdentifiedEvent(0, 1, "Me", 2305, GameClass.Elementalist));
        r.Observe(new NpcSeenEvent(1000, 10, 2100001, X: 500, Y: 0, Z: 100));
        r.Observe(new NpcSeenEvent(1000, 11, 2100002, X: 900, Y: 0, Z: 0));
        r.Observe(new NpcSeenEvent(1000, 12, 2999999)); // not a cube
        Assert.Equal(2, r.InView(2000).Count);

        r.Observe(new DeathEvent(3000, 10));
        Assert.Single(r.InView(3000));
        Assert.Equal(2100002, r.InView(3000)[0].Cube.NpcCode);
    }

    [Fact]
    public void Arrow_points_from_your_position_to_the_nearest_cube()
    {
        var r = Radar();
        r.Observe(new SelfIdentifiedEvent(0, 1, "Me", 2305, GameClass.Elementalist));
        r.Observe(new MoveEvent(5000, 1, 100, 200, 0)); // you
        r.Observe(new NpcSeenEvent(5000, 10, 2100001, X: 400, Y: 600, Z: 300));
        r.Observe(new NpcSeenEvent(5000, 11, 2100002, X: 5000, Y: 5000, Z: 0));

        var a = r.Nearest(6000)!;
        Assert.NotNull(a);
        Assert.True(a.SelfFresh);
        Assert.False(a.Gone);
        Assert.Equal(2100001, a.Cube.NpcCode);
        Assert.Equal(300.0, (double)a.Dx, 1);
        Assert.Equal(400.0, (double)a.Dy, 1);
        Assert.Equal(300.0, (double)a.Dz, 1);
        Assert.Equal(Math.Sqrt(300 * 300 + 400 * 400 + 300 * 300) / 100, (double)a.DistanceM, 2);
    }

    [Fact]
    public void Stale_position_means_detected_not_a_direction()
    {
        var r = Radar();
        r.Observe(new SelfIdentifiedEvent(0, 1, "Me", 2305, GameClass.Elementalist));
        r.Observe(new MoveEvent(0, 1, 100, 200, 0)); // old: the game only sends it on skill / dash
        r.Observe(new NpcSeenEvent(20_000, 10, 2100001, X: 400, Y: 600, Z: 300));

        Assert.True(r.Nearest(21_000)!.SelfFresh);
        Assert.False(r.Nearest(31_000)!.SelfFresh); // position stale, cube still in view
    }

    [Fact]
    public void A_cube_that_disappeared_stays_for_the_fade()
    {
        var r = Radar();
        r.Observe(new NpcSeenEvent(0, 10, 2100001, X: 500, Y: 0));
        r.Observe(new DeathEvent(5000, 10));
        Assert.Empty(r.InView(6000)); // gone from the radar…
        var a = r.Nearest(5_200, fadeMs: 500)!;
        Assert.True(a.Gone); // …but still there for the fade-out
        Assert.Null(r.Nearest(5_200, fadeMs: 100)); // after the fade it is fully gone
    }

    [Fact]
    public void Zone_change_clears_everything()
    {
        var r = Radar();
        r.Observe(new NpcSeenEvent(0, 10, 2100001, X: 500, Y: 0));
        r.Observe(new ZoneChangedEvent(10, 1110, "Altgard", false));
        Assert.Empty(r.InView(20));
        Assert.Null(r.Nearest(20));
    }
}
