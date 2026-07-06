using Xunit;

namespace Heimdall.Tests;

/// <summary>
/// The 16 behavioral checks the Python harness was validated with (harness_test.py in the
/// slicespike experiments): feedback fires precisely on scope creep and frozen contracts,
/// stays silent on clean work, and the drift report shows wandering sessions undiluted.
/// </summary>
public class BehavioralScenarios
{
    // 1
    [Fact]
    public void CleanSession_EditsAndReadsOwnSlice_AllHooksSilent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Kvad/Internal/KvadService.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/SharedKernel/Primitives.cs")));
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Rune/Contract/IRuneService.cs")));
    }

    // 2
    [Fact]
    public void CleanSession_TelemetryStillLogged()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Kvad/Internal/KvadService.cs"));
        var lines = repo.Telemetry.TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"event\": \"edit\"", lines[0]);
        Assert.Contains("\"event\": \"read\"", lines[1]);
    }

    // 3
    [Fact]
    public void Wanderer_ForeignInternalRead_LoggedNotWarned()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Rune/Internal/RuneEngine.cs"));
        Assert.Equal(0, code);           // reads NEVER interrupt — pure observation
        Assert.Equal("", stderr);
        Assert.Contains("\"kind\": \"slice:Rune\"", repo.Telemetry);
    }

    // 4
    [Fact]
    public void ScopeCreep_SecondSliceEdit_WarnsWithBothSliceNames()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Rune/Internal/RuneEngine.cs"));
        Assert.Equal(2, code);
        Assert.Contains("you are now editing slice 'Rune' after editing ['Kvad']", stderr);
        Assert.Contains("cross-slice changes should go through contracts", stderr);
    }

    // 5
    [Fact]
    public void ScopeCreep_WarnsExactlyOnce_ThirdEditSameSliceSilent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        Assert.Equal(2, repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Rune/Internal/RuneEngine.cs")).Code);
        // the slice is now part of the session's working set — repeating the edit is not a new crossing
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Rune/Internal/RuneService.cs")));
    }

    // 6
    [Fact]
    public void ScopeCreep_SeparateSessions_NoWarning()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s2", "Edit", "samples/Slices/Rune/Internal/RuneEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
    }

    // 7
    [Fact]
    public void FrozenContract_HighFanIn_Warns()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap(withFrozenCore: true); // Core has fan_in 12 >= 10
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Core/Contract/ICoreService.cs"));
        Assert.Equal(2, code);
        Assert.Contains("'Core' Contract has fan-in 12 — treat as frozen", stderr);
        Assert.Contains("expand-contract", stderr);
    }

    // 8
    [Fact]
    public void LowFanIn_ContractEdit_Silent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap(); // Rune fan_in 1 < 10
        Assert.Equal((0, ""), repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Rune/Contract/IRuneService.cs")));
    }

    // 9
    [Fact]
    public void Read_KernelFile_ClassifiedKernel()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/SharedKernel/Primitives.cs"));
        Assert.Contains("\"kind\": \"kernel\"", repo.Telemetry);
    }

    // 10
    [Fact]
    public void Read_ContractFile_ClassifiedContractWithSlice()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Rune/Contract/IRuneService.cs"));
        Assert.Contains("\"kind\": \"contract:Rune\"", repo.Telemetry);
    }

    // 11
    [Fact]
    public void Read_OutsideSlicesDir_ClassifiedOutside()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "docs/rules/EIT001.md"));
        Assert.Contains("\"kind\": \"outside\"", repo.Telemetry);
    }

    // 12
    [Fact]
    public void Read_NonCodeFile_ProducesNoFinding()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Kvad/appsettings.json"));
        Assert.Equal("", repo.Telemetry);
    }

    // 13
    [Fact]
    public void Hook_IrrelevantTool_NoFindingNoTelemetry()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        Assert.Equal((0, ""), repo.Hook("{\"session_id\":\"s1\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}}"));
        Assert.Equal("", repo.Telemetry);
    }

    // 14
    [Fact]
    public void Hook_GarbageStdin_Exit0()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var r = repo.Run("{not json", "hook");
        Assert.Equal(0, r.Code);
        Assert.Equal("", r.Stderr);
    }

    // 15
    [Fact]
    public void Hook_NoMap_StaysSilentEvenOnCrossSliceEdits()
    {
        using var repo = new TempRepo();
        repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Rune/Internal/RuneEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.Equal("", repo.Telemetry);
    }

    // 16
    [Fact]
    public void Drift_PerSession_WanderingSessionShows100PercentUndiluted()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        // clean session: 3 reads, all in bounds
        repo.Hook(TempRepo.Ev("clean", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/Slices/Kvad/Internal/KvadService.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/SharedKernel/Primitives.cs"));
        repo.Hook(TempRepo.Ev("clean", "Read", "samples/Slices/Rune/Contract/IRuneService.cs"));
        // wandering session: edits Kvad, then reads ONLY foreign internals
        repo.Hook(TempRepo.Ev("wander", "Edit", "samples/Slices/Kvad/Internal/KvadEngine.cs"));
        repo.Hook(TempRepo.Ev("wander", "Read", "samples/Slices/Rune/Internal/RuneEngine.cs"));
        repo.Hook(TempRepo.Ev("wander", "Read", "samples/Slices/Rune/Internal/RuneService.cs"));

        var stdout = repo.Run("", "drift").Stdout.Replace("\r\n", "\n");
        var lines = stdout.Split('\n');
        // per-session table: the wanderer shows 100%, NOT averaged with the clean session
        Assert.Equal("clean     Kvad                            3     0      0%", lines[1]);
        Assert.Equal("wander    Kvad                            2     2    100%", lines[2]);
        // the aggregate DOES dilute (5 reads, 2 oob -> 40%) — which is exactly why
        // the per-session table exists and prints first
        Assert.Contains("Kvad                        5              2     40%", stdout);
    }
}
