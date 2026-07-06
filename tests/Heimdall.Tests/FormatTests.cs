using Heimdall.Cli;
using Heimdall.Sensors;
using Xunit;

namespace Heimdall.Tests;

public class FormatTests
{
    [Fact]
    public void PyJson_Str_EscapesLikeCPython()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", PyJson.Str("a\"b\\c"));
        Assert.Equal("\"tab\\there\"", PyJson.Str("tab\there"));
        Assert.Equal("\"\\u2014\"", PyJson.Str("—"));           // em dash -> ensure_ascii
        Assert.Equal("\"\\u0001\"", PyJson.Str(((char)1).ToString())); // control char -> \u-escape
        Assert.Equal("\"\\ud83d\\ude00\"", PyJson.Str("\U0001F600")); // surrogate pair, two escapes
    }

    [Fact]
    public void PyJson_Float_MatchesPythonRepr()
    {
        Assert.Equal("1751812345.6789093", PyJson.Float(1751812345.6789093));
        Assert.Equal("1751812345.0", PyJson.Float(1751812345.0));
    }

    [Fact]
    public void PyJson_Line_UsesPythonSeparatorsAndKeyOrder()
    {
        var f = new Finding { Event = "read", Path = "a/b.cs", Kind = "slice:Domme", Sensor = "boundary_reads", Ts = 1751812345.5, Session = "s1" };
        Assert.Equal(
            "{\"event\": \"read\", \"path\": \"a/b.cs\", \"kind\": \"slice:Domme\", \"sensor\": \"boundary_reads\", \"ts\": 1751812345.5, \"session\": \"s1\"}",
            PyJson.Line(f));
        var err = new Finding { Sensor = "heimdall", Error = "boom", Ts = 2.0 };
        Assert.Equal("{\"sensor\": \"heimdall\", \"error\": \"boom\", \"ts\": 2.0}", PyJson.Line(err));
    }

    [Fact]
    public void PyJson_MapDocument_MatchesPythonIndent2()
    {
        var doc = PyJson.MapDocument("SharedKernel", "samples\\Slices", new[]
        {
            new MapSlice("Domme", "samples\\Slices\\Domme", new[] { "Retskilder" }, 15000, 0),
            new MapSlice("Retskilder", "samples\\Slices\\Retskilder", Array.Empty<string>(), 15000, 1),
        });
        Assert.Equal(
            "{\n  \"kernel\": \"SharedKernel\",\n  \"slices_dir\": \"samples\\\\Slices\",\n  \"slices\": {\n" +
            "    \"Domme\": {\n      \"path\": \"samples\\\\Slices\\\\Domme\",\n      \"depends_on\": [\n        \"Retskilder\"\n      ],\n      \"budget\": 15000,\n      \"fan_in\": 0\n    },\n" +
            "    \"Retskilder\": {\n      \"path\": \"samples\\\\Slices\\\\Retskilder\",\n      \"depends_on\": [],\n      \"budget\": 15000,\n      \"fan_in\": 1\n    }\n" +
            "  }\n}",
            doc);
    }

    [Fact]
    public void Estimate_File_MatchesLinkedEstimator()
    {
        using var repo = new TempRepo();
        const string src = "namespace X;\npublic sealed class Thing { public int Answer() => 42; }\n";
        repo.WriteFile("samples/Thing.cs", src);
        var r = repo.Run("", "estimate", "samples/Thing.cs");
        Assert.Equal(0, r.Code);
        Assert.Equal(Brokkr.Tokenization.TokenEstimator.Estimate(src).ToString(), r.Stdout.Trim());
    }

    [Fact]
    public void Estimate_Directory_SumsAllCsFiles()
    {
        using var repo = new TempRepo();
        repo.WriteFile("tree/A.cs", "class A { }");
        repo.WriteFile("tree/sub/B.cs", "class B { int x = 123; }");
        repo.WriteFile("tree/ignored.txt", "not counted");
        var expected = Brokkr.Tokenization.TokenEstimator.Estimate("class A { }")
                     + Brokkr.Tokenization.TokenEstimator.Estimate("class B { int x = 123; }");
        var r = repo.Run("", "estimate", "tree");
        Assert.Equal(0, r.Code);
        Assert.Equal(expected.ToString(), r.Stdout.Trim());
    }

    [Fact]
    public void Estimate_MissingPath_Exit1()
    {
        using var repo = new TempRepo();
        var r = repo.Run("", "estimate", "nope.cs");
        Assert.Equal(1, r.Code);
        Assert.Contains("no such file", r.Stderr);
    }
}
