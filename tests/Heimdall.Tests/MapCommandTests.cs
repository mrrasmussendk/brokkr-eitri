using Xunit;

namespace Heimdall.Tests;

public class MapCommandTests
{
    private static void WriteSampleTree(TempRepo repo)
    {
        repo.WriteFile("samples/Slices/Kvad/Kvad.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\SharedKernel\\SharedKernel.csproj\" />\n" +
            "    <ProjectReference Include=\"..\\Rune\\Rune.csproj\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        repo.WriteFile("samples/Slices/Rune/Rune.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
            "    <ProjectReference Include=\"..\\..\\SharedKernel\\SharedKernel.csproj\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        repo.WriteFile("samples/Slices/Kvad/AGENTS.md", "# Kvad\nhand-written notes stay.\n");
    }

    [Fact]
    public void Map_EmitsPythonCompatibleMapJson()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        var r = repo.Run("", "map", "--root", "samples", "--budget", "15000");
        Assert.Equal(0, r.Code);
        Assert.Contains("heimdall map: 2 slices -> .heimdall/map.json; AGENTS.md deps regenerated", r.Stdout);
        var sep = Path.DirectorySeparatorChar == '\\' ? "\\\\" : "/";
        Assert.Equal(
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples" + sep + "Slices\",\n  \"slices\": {\n" +
            "    \"Kvad\": {\n      \"path\": \"samples" + sep + "Slices" + sep + "Kvad\",\n      \"depends_on\": [\n        \"Rune\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Rune\": {\n      \"path\": \"samples" + sep + "Slices" + sep + "Rune\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }\n" +
            "  }\n}",
            repo.ReadFile(".heimdall/map.json"));
    }

    [Fact]
    public void Map_InjectsAndRefreshesAgentsMdMarkers_Idempotently()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        repo.Run("", "map", "--root", "samples");
        Assert.Equal("# Kvad\nhand-written notes stay.\n<!--heimdall:deps-->depends on: Rune + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Kvad/AGENTS.md"));
        // Rune had no AGENTS.md -> created from scratch
        Assert.Equal("# Rune\n<!--heimdall:deps-->depends on: (none) + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Rune/AGENTS.md"));
        // rerun: markers replaced in place, no duplication
        repo.Run("", "map", "--root", "samples");
        Assert.Equal("# Kvad\nhand-written notes stay.\n<!--heimdall:deps-->depends on: Rune + SharedKernel<!--/heimdall:deps-->\n",
            repo.ReadFile("samples/Slices/Kvad/AGENTS.md"));
    }

    [Fact]
    public void Map_HonorsBudgetAndKernelFlags()
    {
        using var repo = new TempRepo();
        WriteSampleTree(repo);
        repo.Run("", "map", "--root", "samples", "--budget", "9000", "--kernel", "Rune");
        var map = repo.ReadFile(".heimdall/map.json");
        Assert.Contains("\"budget\": 9000", map);
        Assert.Contains("\"kernel\": \"Rune\"", map);
        // Rune is now the kernel -> dropped from Kvad's deps. SharedKernel appears
        // instead: the (Python-faithful) unanchored regex matches the second "..\" hop of
        // "..\..\SharedKernel\...", and only the --kernel subtraction normally removes it.
        Assert.DoesNotContain("\"Rune\"\n", map.Substring(map.IndexOf("\"slices\"")));
        Assert.Contains("\"depends_on\": [\n        \"SharedKernel\"\n      ]", map);
    }

    [Fact]
    public void Map_NoSlicesDir_Exit1WithPythonMessage()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "map", "--root", "nowhere");
        Assert.Equal(1, r.Code);
        Assert.Contains("no Slices/ under nowhere", r.Stderr);
    }
}
