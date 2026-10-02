using E33Randomizer;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace E33Randomizer.Tests;

/// <summary>
/// Loads the real game data from E33Randomizer/Data once for all integration tests.
/// Generated assets are written into a temporary working directory.
/// </summary>
public class GameDataFixture : IDisposable
{
    public string DataDirectory { get; }
    public string WorkDirectory { get; }

    public GameDataFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "E33Randomizer.sln")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("Repository root not found");

        DataDirectory = Path.Combine(dir.FullName, "E33Randomizer", "Data");
        WorkDirectory = Path.Combine(Path.GetTempPath(), "e33rando_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkDirectory);
        Directory.SetCurrentDirectory(WorkDirectory);

        RandomizerLogic.DataDirectory = DataDirectory;
        RandomizerLogic.Init();
    }

    public static void ResetSettings(int seed)
    {
        RandomizerLogic.Settings = new SettingsViewModel { Seed = seed };
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(Path.GetTempPath());
        try { Directory.Delete(WorkDirectory, true); } catch (IOException) { }
    }
}

[CollectionDefinition("GameData")]
public class GameDataCollection : ICollectionFixture<GameDataFixture>
{
}
