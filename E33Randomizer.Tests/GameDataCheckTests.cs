using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

/// <summary>
/// Compares the game files in Data with the installed game. Runs only where the game is installed (Steam), or where
/// E33_GAME_DIR points at it; elsewhere (e.g. the release build) there is nothing to compare with.
/// </summary>
[Collection("GameData")]
public class GameDataCheckTests(GameDataFixture fixture)
{
    private static string GameDirectory =>
        Environment.GetEnvironmentVariable("E33_GAME_DIR") is { Length: > 0 } dir ? dir : GameInstallation.FindSteamGameDirectory();

    private List<(string gamePath, string sourcePath)> AllDataAssets() =>
        Directory.GetFiles(fixture.DataDirectory, "*.uasset", SearchOption.AllDirectories)
            .Select(path => (gamePath: GamePath(path), sourcePath: path))
            .Where(asset => asset.gamePath != null)
            .ToList();

    // The game path of a Data copy, taken from the asset itself
    private static string GamePath(string path)
    {
        try
        {
            var asset = new UAssetAPI.UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
            return asset.FolderName.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [Fact]
    public void DataMatchesTheInstalledGame()
    {
        var game = GameDirectory;
        if (game == null) return;

        var outdated = GameDataCheck.FindOutdatedFiles(game, AllDataAssets());
        Assert.True(outdated.Count == 0,
            "These files in Data differ from the installed game; update them from the game:\n" + string.Join("\n", outdated));
    }

    [Fact]
    public void AChangedCopyIsReportedAsOutdated()
    {
        var game = GameDirectory;
        if (game == null) return;

        var copyDirectory = Path.Combine(fixture.WorkDirectory, "outdated_copy");
        Directory.CreateDirectory(copyDirectory);
        var source = Path.Combine(fixture.DataDirectory, "ItemData", "MerchantsData", "DT_Merchant_WM_3_GustaveSuit");
        var copy = Path.Combine(copyDirectory, "DT_Merchant_WM_3_GustaveSuit");
        File.Copy(source + ".uasset", copy + ".uasset", true);
        var uexp = File.ReadAllBytes(source + ".uexp");
        uexp[^5] ^= 0xFF;
        File.WriteAllBytes(copy + ".uexp", uexp);

        var gamePath = GamePath(source + ".uasset");
        var outdated = GameDataCheck.FindOutdatedFiles(game, [(gamePath, copy + ".uasset"), (gamePath, source + ".uasset")]);
        Assert.Equal([gamePath], outdated);
        Assert.Empty(GameDataCheck.FindOutdatedFiles(game, [(gamePath, source + ".uasset")]));
    }
}
