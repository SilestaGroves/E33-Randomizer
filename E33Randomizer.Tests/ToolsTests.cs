using System.Diagnostics;
using E33Randomizer;
using Newtonsoft.Json.Linq;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class ToolsTests(GameDataFixture fixture)
{
    private static (int exitCode, string output) Run(string tool, string arguments)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, tool), arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + errors.Result);
    }

    [Fact]
    public void ToolsAreCopiedNextToTheExe()
    {
        foreach (var tool in new[] { "retoc.exe", "uesave.exe", "repak.exe" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, tool)), $"{tool} is missing");
    }

    [Fact]
    public void GenerationPacksAValidModAndCopiesItIntoTheGame()
    {
        var game = Path.Combine(fixture.WorkDirectory, "Game", "Expedition 33");
        Directory.CreateDirectory(Path.Combine(game, "Sandfall", "Content", "Paks"));
        GameDataFixture.ResetSettings(77);
        RandomizerLogic.Settings.GameDirectory = game;
        RandomizerLogic.PresetName = "";

        RandomizerLogic.Randomize(saveData: true);

        var export = Path.Combine(fixture.WorkDirectory, "rand_77");
        var utoc = Path.Combine(export, "randomizer_P.utoc");
        Assert.True(File.Exists(Path.Combine(export, "spoiler_log.txt")));

        var (verifyCode, verifyOutput) = Run("retoc.exe", $"verify \"{utoc}\"");
        Assert.True(verifyCode == 0, verifyOutput);
        var (listCode, listing) = Run("retoc.exe", $"list --path \"{utoc}\"");
        Assert.True(listCode == 0, listing);
        Assert.Contains("DT_jRPG_Encounters", listing);
        Assert.Contains("DT_ChestsContent", listing);

        var mods = GameInstallation.GetModsDirectory(game);
        Assert.Equal(mods, RandomizerLogic.LastInstalledModsDirectory);
        foreach (var extension in GameInstallation.ModFileExtensions)
        {
            var packed = File.ReadAllBytes(Path.Combine(export, "randomizer_P" + extension));
            Assert.NotEmpty(packed);
            Assert.Equal(packed, File.ReadAllBytes(Path.Combine(mods, "randomizer_P" + extension)));
        }
    }
}

public class SaveFilePatcherTests
{
    private const string Jump = "8e3263a7-493b-f6fd-f260-549af74ea0db";
    private const string Curtain = "aa6633b1-4fed-a61d-092e-ed80cd949751";

    [Fact]
    public void PatchesTheUesave07Format()
    {
        var json = """
            {"header": {}, "schemas": {"schemas": {"NamedIDsStates": {"data": {}}}},
             "root": {"properties": {"NamedIDsStates_0": [{"key": "JUMP", "value": false}]}}, "extra": []}
            """.Replace("JUMP", Jump);

        var patched = JObject.Parse(SaveFilePatcher.PatchJson(json, new() { { Jump, true }, { Curtain, false } }));

        var entries = (JArray)patched["root"]!["properties"]!["NamedIDsStates_0"]!;
        Assert.Equal(2, entries.Count);
        Assert.True((bool)entries.Single(e => (string)e["key"]! == Jump)["value"]!);
        Assert.False((bool)entries.Single(e => (string)e["key"]! == Curtain)["value"]!);
    }

    [Fact]
    public void AddsTheMissingMapAndItsSchemaInTheUesave07Format()
    {
        var json = """{"header": {}, "schemas": {"schemas": {}}, "root": {"properties": {}}, "extra": []}""";

        var patched = JObject.Parse(SaveFilePatcher.PatchJson(json, new() { { Jump, true } }));

        Assert.NotNull(patched["schemas"]!["schemas"]!["NamedIDsStates"]!["data"]!["Map"]);
        Assert.Equal(Jump, (string)patched["root"]!["properties"]!["NamedIDsStates_0"]![0]!["key"]!);
    }

    [Fact]
    public void PatchesTheOlderUesaveFormat()
    {
        var json = """
            {"root": {"properties": {"NamedIDsStates_0": {"tag": {}, "Map": [
              {"key": {"Struct": {"Guid": "JUMP"}}, "value": {"Bool": false}}]}}}}
            """.Replace("JUMP", Jump);

        var patched = JObject.Parse(SaveFilePatcher.PatchJson(json, new() { { Jump, true }, { Curtain, false } }));

        var entries = (JArray)patched["root"]!["properties"]!["NamedIDsStates_0"]!["Map"]!;
        Assert.True((bool)entries.Single(e => (string)e["key"]!["Struct"]!["Guid"]! == Jump)["value"]!["Bool"]!);
        Assert.False((bool)entries.Single(e => (string)e["key"]!["Struct"]!["Guid"]! == Curtain)["value"]!["Bool"]!);
    }
}
