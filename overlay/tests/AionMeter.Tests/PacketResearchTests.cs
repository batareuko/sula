using AionMeter.Core.Events;
using AionMeter.Core.OneHp;
using AionMeter.Core.Protocol;

namespace AionMeter.Tests;

public class PacketResearchTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "cube-research-tests", Guid.NewGuid().ToString("N"));

    private static PacketResearch Research(string? dir = null) => new(dir ?? TempDir()) { Enabled = true };

    [Fact]
    public void Proposes_codes_seen_near_marks()
    {
        var r = Research();
        r.Observe(new NpcSeenEvent(0, 1, 2100001));   // background noise, away from the marks
        r.Observe(new NpcSeenEvent(500, 2, 2100001));
        r.Observe(new NpcSeenEvent(5000, 3, 2100099)); // near "appeared"
        r.Mark(CubeMark.Appeared, 5200);
        r.Observe(new NpcSeenEvent(7000, 4, 2100099)); // near "gone"
        r.Mark(CubeMark.Gone, 7200);

        var candidates = r.Candidates(8000);
        Assert.Contains(candidates, c => c.NpcCode == 2100099);
        Assert.DoesNotContain(candidates, c => c.NpcCode == 2100001);
    }

    [Fact]
    public void Move_marks_do_not_contribute_candidates()
    {
        var r = Research();
        r.Observe(new NpcSeenEvent(5000, 3, 2100099));
        r.Mark(CubeMark.Moved, 5200);
        Assert.Empty(r.Candidates(6000));
    }

    [Fact]
    public void Logs_unknown_opcodes_and_skips_known()
    {
        var dir = TempDir();
        var r = new PacketResearch(dir) { Enabled = true };
        r.OnPacket(0, 0x1234, new byte[] { 0x01, 0x02, 0x03 });
        r.OnPacket(1, 0x1234, new byte[] { 0x04 });
        r.OnPacket(2, Opcodes.Damage, new byte[] { 0x00 }); // known: not logged as unknown

        var file = Assert.Single(Directory.GetFiles(dir));
        var text = File.ReadAllText(file);
        Assert.Contains("UNKNOWN 1234", text);
        Assert.DoesNotContain($"UNKNOWN {Opcodes.Damage:X4}", text);
    }
}
