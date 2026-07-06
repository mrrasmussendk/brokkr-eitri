using Xunit;

namespace Heimdall.Tests;

public class DriftCommandTests
{
    private static TempRepo SmokeScenario()
    {
        var repo = new TempRepo();
        repo.WriteSampleMap();
        // session s1: edits Domme, reads own internal + declared contract; then creeps into Retskilder
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Contract/IRetskilderService.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")); // OOB at read time...
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")); // ...but session later edits Retskilder
        // session s2: edits Domme only, one clean read + one OOB read
        repo.Hook(TempRepo.Ev("s2", "Edit", "samples/Slices/Domme/Internal/DommeService.cs"));
        repo.Hook(TempRepo.Ev("s2", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        repo.Hook(TempRepo.Ev("s2", "Read", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs")); // OOB
        return repo;
    }

    [Fact]
    public void Drift_PrintsPerSessionTableFirst_ExactColumns()
    {
        using var repo = SmokeScenario();
        var r = repo.Run("", "drift");
        Assert.Equal(0, r.Code);
        var lines = r.Stdout.Replace("\r\n", "\n").Split('\n');
        Assert.Equal("session   slices edited               reads   oob   oob %", lines[0]);
        // s1 edited both slices; 3 reads; the Retskilder Internal read happened BEFORE the
        // s1 Retskilder edit, but session sets are computed over the whole session -> in-bounds
        Assert.Equal("s1        Domme,Retskilder                3     0      0%", lines[1]);
        // s2: 2 reads, 1 OOB -> 50%
        Assert.Equal("s2        Domme                           2     1     50%", lines[2]);
    }

    [Fact]
    public void Drift_PerSliceAggregateSecond_AttributesToFirstEditedSlice()
    {
        using var repo = SmokeScenario();
        var r = repo.Run("", "drift");
        var text = r.Stdout.Replace("\r\n", "\n");
        var lines = text.Split('\n');
        Assert.Equal("", lines[3]);
        Assert.Equal("slice                   reads  out-of-bounds   oob %", lines[4]);
        // both sessions' target is "Domme" (first sorted edited slice): 3+2 reads, 0+1 oob -> 20%
        Assert.Equal("Domme                       5              1     20%", lines[5]);
        Assert.Contains("rule of thumb: sustained oob% > 20 on a slice = re-cut that seam", text);
    }

    [Fact]
    public void Drift_MissingInputs_Exit1()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "drift");
        Assert.Equal(1, r.Code);
        Assert.Contains("need telemetry + map", r.Stderr);
    }
}
