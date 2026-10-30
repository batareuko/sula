using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.OneHp;
using AionMeter.Core.Protocol;

namespace AionMeter.Tests;

public class CubeRadarTests
{
    private static List<GameEvent> Parse(ushort opcode, string hex)
    {
        var events = new List<GameEvent>();
        var parser = new PacketParser(GameData.Empty, events.Add) { TimeMs = 1_000 };
        parser.Handle(opcode, Wire.FromHex(hex));
        return events;
    }

    // Layouts from two live captures (cube ids are dynamic varints: 41943 = d7 c7 02, 43627 = eb d4 02;
    // the player is 10678 = b6 53).
    [Fact]
    public void Cube_packets_decode()
    {
        var near = Assert.Single(Parse(Opcodes.CubeNear, "d7c702b65301").OfType<CubeEvent>());
        Assert.Equal(CubePhase.Near, near.Phase);
        Assert.Equal(41943u, near.CubeId);
        Assert.Equal(10678u, near.PlayerId);

        var spawn = Assert.Single(Parse(Opcodes.CubeSpawn, "000000d7c7020000000000000000").OfType<CubeEvent>());
        Assert.Equal(CubePhase.SpawnRecord, spawn.Phase);
        Assert.Equal(41943u, spawn.CubeId);

        var interact = Assert.Single(Parse(Opcodes.CubeInteract, "d7c702b6530100000023c59a25").OfType<CubeEvent>());
        Assert.Equal(CubePhase.Interact, interact.Phase);
        Assert.Equal(41943u, interact.CubeId);
        Assert.Equal(10678u, interact.PlayerId);

        Assert.Equal(CubePhase.Focus, Assert.Single(Parse(Opcodes.CubeFocus, "d7c702").OfType<CubeEvent>()).Phase);
        Assert.Equal(CubePhase.Gone, Assert.Single(Parse(Opcodes.CubeGone, "d7c702").OfType<CubeEvent>()).Phase);
        Assert.Equal(CubePhase.Collected, Assert.Single(Parse(Opcodes.CubeCollected, "d7c702040300").OfType<CubeEvent>()).Phase);
    }

    [Fact]
    public void Detects_a_cube_until_it_is_gone_then_fades()
    {
        var r = new CubeRadar { FadeAfterMs = 500 };
        Assert.False(r.Detected(0));

        r.Observe(new CubeEvent(1_000, CubePhase.Near, 41943, 10678));
        Assert.True(r.Detected(1_000));
        var cube = Assert.Single(r.Active(1_000));
        Assert.Equal(41943u, cube.CubeId);

        r.Observe(new CubeEvent(2_000, CubePhase.Focus, 41943));
        Assert.True(r.Detected(2_000));

        r.Observe(new CubeEvent(3_000, CubePhase.Gone, 41943));
        Assert.True(r.Detected(3_000));            // still shown during the fade
        Assert.True(r.Detected(3_499));
        Assert.False(r.Detected(3_500));           // 500 ms after the last one disappeared
        Assert.Empty(r.Active(3_500));
    }

    [Fact]
    public void Several_cubes_stay_until_each_is_gone()
    {
        var r = new CubeRadar { FadeAfterMs = 500 };
        r.Observe(new CubeEvent(1_000, CubePhase.Near, 41943));
        r.Observe(new CubeEvent(1_100, CubePhase.Near, 43627));
        Assert.Equal(2, r.Active(1_100).Count);

        r.Observe(new CubeEvent(2_000, CubePhase.Collected, 41943));
        Assert.True(r.Detected(2_000));            // one cube left
        Assert.Single(r.Active(2_000));

        r.Observe(new CubeEvent(2_500, CubePhase.Collected, 43627));
        Assert.True(r.Detected(2_500));
        Assert.False(r.Detected(3_000));           // fade counted from the last one
    }

    [Fact]
    public void Clears_on_zone_change()
    {
        var r = new CubeRadar { FadeAfterMs = 500 };
        r.Observe(new CubeEvent(1_000, CubePhase.Near, 41943));
        Assert.True(r.Detected(1_000));

        r.Observe(new ZoneChangedEvent(2_000, 1110, "Altgard", false));
        Assert.False(r.Detected(2_000));
        Assert.Empty(r.Active(2_000));
    }

    [Fact]
    public void Learns_self_from_the_cube_packet_and_identity()
    {
        var r = new CubeRadar();
        r.Observe(new CubeEvent(1_000, CubePhase.Near, 41943, 10678));
        Assert.Equal(10678u, r.Self);
        r.Observe(new SelfIdentifiedEvent(2_000, 7, "Me", 2305, GameClass.Elementalist));
        Assert.Equal(7u, r.Self);
    }
}
