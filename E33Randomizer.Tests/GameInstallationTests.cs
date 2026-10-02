using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

public class GameInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "e33rando_install_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string MakeGame(string relative)
    {
        var game = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.Combine(game, "Sandfall", "Content", "Paks"));
        return game;
    }

    private string MakeExport(string content)
    {
        var export = Path.Combine(_root, "rand_1");
        Directory.CreateDirectory(export);
        foreach (var extension in GameInstallation.ModFileExtensions)
            File.WriteAllText(Path.Combine(export, "randomizer_P" + extension), content);
        return export;
    }

    [Fact]
    public void AnyFolderInsideTheGameIsNormalizedToTheGameFolder()
    {
        var game = MakeGame("Expedition 33");
        var paks = Path.Combine(game, "Sandfall", "Content", "Paks");

        Assert.Equal(game, GameInstallation.NormalizeGameDirectory(game));
        Assert.Equal(game, GameInstallation.NormalizeGameDirectory(paks));
        Directory.CreateDirectory(Path.Combine(paks, "~mods"));
        Assert.Equal(game, GameInstallation.NormalizeGameDirectory(Path.Combine(paks, "~mods")));
        Assert.Null(GameInstallation.NormalizeGameDirectory(_root));
    }

    [Fact]
    public void GamePassFolderWithContentSubfolderIsAccepted()
    {
        var content = MakeGame(Path.Combine("Clair Obscur- Expedition 33", "Content"));
        Assert.Equal(content, GameInstallation.NormalizeGameDirectory(Path.GetDirectoryName(content)));
    }

    [Fact]
    public void InstallCopiesAllModFilesIntoModsAndReplacesTheOldOnes()
    {
        var game = MakeGame("Expedition 33");
        var mods = GameInstallation.InstallMod(MakeExport("first"), game);
        GameInstallation.InstallMod(MakeExport("second"), game);

        Assert.Equal(GameInstallation.GetModsDirectory(game), mods);
        foreach (var extension in GameInstallation.ModFileExtensions)
            Assert.Equal("second", File.ReadAllText(Path.Combine(mods, "randomizer_P" + extension)));
    }

    [Fact]
    public void InstallRejectsWrongFolderAndIncompleteMod()
    {
        var export = MakeExport("x");
        Assert.Throws<DirectoryNotFoundException>(() => GameInstallation.InstallMod(export, _root));

        File.Delete(Path.Combine(export, "randomizer_P.ucas"));
        var error = Assert.Throws<FileNotFoundException>(() => GameInstallation.InstallMod(export, MakeGame("Expedition 33")));
        Assert.Contains("randomizer_P.ucas", error.Message);
    }
}
