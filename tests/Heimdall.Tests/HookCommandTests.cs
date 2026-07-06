using System.Text.RegularExpressions;
using Xunit;

namespace Heimdall.Tests;

public class HookCommandTests
{
    [Fact]
    public void Hook_ReadInSlice_LogsPythonCompatibleTelemetryLine()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Read", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.Matches(
            new Regex("^\\{\"event\": \"read\", \"path\": \"samples/Slices/Domme/Internal/DommeEngine\\.cs\", " +
                      "\"kind\": \"slice:Domme\", \"sensor\": \"boundary_reads\", \"ts\": \\d+\\.\\d+, \"session\": \"s1\"\\}\n$"),
            repo.Telemetry);
    }

    [Fact]
    public void Hook_SecondSliceEdit_Exit2WithFeedbackAndSessionStateFile()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var first = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, first.Code);
        Assert.True(repo.HasFile(".heimdall/session-s1.json"));
        var second = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(2, second.Code);
        Assert.Contains("Heimdall: you are now editing slice 'Retskilder' after editing ['Domme']", second.Stderr);
        Assert.Contains("cross-slice", second.Stderr);
    }

    [Fact]
    public void Hook_InvalidJson_Exit0Silent()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        var r = repo.Run("this is not json", "hook");
        Assert.Equal(0, r.Code);
        Assert.Equal("", r.Stderr);
        Assert.Equal("", repo.Telemetry);
    }

    [Fact]
    public void Hook_NoMap_Exit0NoTelemetry()
    {
        using var repo = new TempRepo();
        var (code, stderr) = repo.Hook(TempRepo.Ev("s1", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        Assert.False(repo.HasFile(".heimdall/telemetry.jsonl"));
    }

    [Fact]
    public void SessionStore_IsolatesSessions_AndSanitizesIds()
    {
        using var repo = new TempRepo();
        repo.WriteSampleMap();
        repo.Hook(TempRepo.Ev("a/b:c", "Edit", "samples/Slices/Domme/Internal/DommeEngine.cs"));
        Assert.True(repo.HasFile(".heimdall/session-a_b_c.json"));
        Assert.Contains("\"edited_slices\":[\"Domme\"]", repo.ReadFile(".heimdall/session-a_b_c.json").Replace(" ", ""));
        // a different session editing another slice gets no cross-slice warning
        var other = repo.Hook(TempRepo.Ev("other", "Edit", "samples/Slices/Retskilder/Internal/RetskilderEngine.cs"));
        Assert.Equal(0, other.Code);
    }
}
