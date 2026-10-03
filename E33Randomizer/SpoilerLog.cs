using System.IO;
using System.Text;
using E33Randomizer.ItemSources;

namespace E33Randomizer;

/// <summary>
/// Builds a human-readable spoiler log of the current randomization: what each changed encounter, check and
/// skill node contained originally and what it contains now. Unchanged entries are left out.
/// encounters.txt and checks.txt stay the machine-readable formats that can be loaded back.
/// </summary>
public static class SpoilerLog
{
    public static void Write(string path)
    {
        File.WriteAllText(path, Build(), Encoding.UTF8);
    }

    public static string Build()
    {
        var log = new StringBuilder();
        log.AppendLine("Clair Obscur: Expedition 33 Randomizer - spoiler log");
        log.AppendLine($"Seed: {RandomizerLogic.usedSeed}");
        if (RandomizerLogic.PresetName.Length > 0) log.AppendLine($"Preset: {RandomizerLogic.PresetName}");
        log.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        log.AppendLine();

        AppendSettings(log);
        if (RandomizerLogic.Settings.RandomizeEnemies) AppendEnemies(log);
        if (RandomizerLogic.Settings.RandomizeItems) AppendKeyItems(log);
        if (RandomizerLogic.Settings.RandomizeItems) AppendItems(log);
        if (RandomizerLogic.Settings.RandomizeSkills) AppendSkills(log);
        return log.ToString();
    }

    private static void AppendHeader(StringBuilder log, string title)
    {
        log.AppendLine(new string('=', 60));
        log.AppendLine(title);
        log.AppendLine(new string('=', 60));
    }

    private static void AppendSettings(StringBuilder log)
    {
        AppendHeader(log, "SETTINGS");
        foreach (var property in typeof(SettingsViewModel).GetProperties().OrderBy(p => p.Name))
        {
            // A local path, not a randomization setting
            if (property.Name == nameof(SettingsViewModel.GameDirectory)) continue;
            log.AppendLine($"  {property.Name}: {property.GetValue(RandomizerLogic.Settings)}");
        }
        log.AppendLine();
    }

    private static string EnemyName(string codeName)
    {
        var enemy = Controllers.EnemiesController.GetObject(codeName);
        return string.IsNullOrEmpty(enemy.CustomName) ? codeName : enemy.CustomName;
    }

    private static void AppendEnemies(StringBuilder log)
    {
        var controller = Controllers.EnemiesController;
        controller.ApplyViewModel();

        // Some encounters are listed under several locations; count them once
        var changedEncounters = new HashSet<string>();
        var body = new StringBuilder();
        foreach (var location in controller.EncounterIndexesByLocation)
        {
            var locationLines = new StringBuilder();
            foreach (var index in location.Value.Where(i => i >= 0 && i < controller.Encounters.Count))
            {
                var encounter = controller.Encounters[index];
                var newEnemies = encounter.Enemies.Select(e => e.CodeName).ToList();
                if (newEnemies.SequenceEqual(encounter.OriginalEnemyCodeNames)) continue;

                changedEncounters.Add(encounter.Name);
                // Encounters without a level override are fought at the level of their map
                var levelNote = encounter.LevelOverride > 0 ? $"  [fought at level {encounter.LevelOverride}]" : "  [fought at the area's level]";
                locationLines.AppendLine($"  {encounter.Name}{levelNote}");
                locationLines.AppendLine($"      was: {string.Join(", ", encounter.OriginalEnemyCodeNames.Select(EnemyName))}");
                locationLines.AppendLine($"      now: {string.Join(", ", newEnemies.Select(EnemyName))}");
            }

            if (locationLines.Length == 0) continue;
            body.AppendLine($"[{location.Key}]");
            body.Append(locationLines);
            body.AppendLine();
        }

        AppendHeader(log, $"ENEMIES ({changedEncounters.Count} encounters changed)");
        log.Append(body);
        log.AppendLine();
    }

    private static string ItemDescription(ItemSourceParticle particle)
    {
        var description = particle.Item.CustomName;
        if (particle.Item.HasQuantities && particle.Quantity > 1) description += $" x{particle.Quantity}";
        if (particle.LootDropChance < 100) description += $" ({particle.LootDropChance:0.##}%)";
        if (particle.MerchantInventoryLocked) description += " [locked]";
        return description;
    }

    private static string ItemsDescription(IEnumerable<ItemSourceParticle> particles)
    {
        var descriptions = particles.Select(ItemDescription).ToList();
        return descriptions.Count == 0 ? "(nothing)" : string.Join(", ", descriptions);
    }

    private static void AppendItems(StringBuilder log)
    {
        var controller = Controllers.ItemsController;
        controller.ApplyViewModel();

        var changedCount = 0;
        var body = new StringBuilder();
        foreach (var checkType in controller.CheckTypes)
        {
            var typeLines = new StringBuilder();
            foreach (var check in checkType.Value.OrderBy(c => c.CustomName))
            {
                var source = check.ItemSource;
                var current = source is MerchantInventoryItemSource merchant
                    ? merchant.GetMergedInventory()
                    : source.SourceSections[check.Key];
                var original = controller.GetOriginalSection(source, check.Key);

                var currentDescription = ItemsDescription(current);
                var originalDescription = ItemsDescription(original);
                if (currentDescription == originalDescription) continue;

                changedCount++;
                typeLines.AppendLine($"  {check.CustomName}");
                typeLines.AppendLine($"      was: {originalDescription}");
                typeLines.AppendLine($"      now: {currentDescription}");
            }

            if (typeLines.Length == 0) continue;
            body.AppendLine($"[{checkType.Key}]");
            body.Append(typeLines);
            body.AppendLine();
        }

        AppendHeader(log, $"ITEMS ({changedCount} checks changed)");
        if (controller.StartingEquipment.Count > 0)
        {
            body.Insert(0, "[Starting equipment]\n" +
                           string.Concat(controller.StartingEquipment.Select(e => $"  {e}\n")) + "\n");
        }
        log.Append(body);
        log.AppendLine();
    }

    private static void AppendKeyItems(StringBuilder log)
    {
        var controller = Controllers.ItemsController;
        controller.ApplyViewModel();
        var checkNames = controller.CheckTypes.Values.SelectMany(c => c)
            .GroupBy(c => ProgressionLogicData.GetCheckId(c.ItemSource.FileName, c.Key))
            .ToDictionary(g => g.Key, g => g.First().CustomName);

        var unreachable = ProgressionLogic.FindUnreachableItems();
        AppendHeader(log, unreachable.Count == 0
            ? "KEY ITEMS (all key items can be obtained)"
            : $"KEY ITEMS (WARNING: not guaranteed to be obtainable: {string.Join(", ", unreachable.Select(i => controller.GetObject(i).CustomName))})");

        foreach (var (item, slot) in ProgressionLogic.FindPlacedItems().OrderBy(p => p.slot.Check?.Act ?? 99))
        {
            var check = slot.Check;
            var checkId = check?.CheckId ?? ProgressionLogicData.GetCheckId(slot.Source.FileName, slot.Key);
            var where = checkNames.GetValueOrDefault(checkId, checkId);
            var region = check?.Region != null ? $"{check.Region}, act {check.Act}" : "no fixed location";
            var requires = check?.Requires.Count > 0
                ? $"; needs {string.Join(", ", check.Requires.Select(r => controller.GetObject(r).CustomName))}"
                : "";
            log.AppendLine($"  {controller.GetObject(item).CustomName}");
            log.AppendLine($"      at: {where} ({region}{requires})");
        }
        log.AppendLine();
    }

    private static void AppendSkills(StringBuilder log)
    {
        var controller = Controllers.SkillsController;
        controller.ApplyViewModel();

        AppendHeader(log, "SKILLS");
        foreach (var graph in controller.SkillGraphs)
        {
            log.AppendLine($"[{graph.CharacterName}]");
            foreach (var node in graph.Nodes)
            {
                var original = controller.GetObject(node.OriginalSkillCodeName).CustomName;
                var marker = node.SkillData.CodeName == node.OriginalSkillCodeName ? "  " : "* ";
                log.AppendLine($"  {marker}{original} -> {node.SkillData.CustomName} (cost {node.UnlockCost})");
            }
            log.AppendLine();
        }
    }
}
