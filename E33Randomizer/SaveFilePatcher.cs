using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace E33Randomizer;

public static class SaveFilePatcher
{
    private const string JUMP_COUNTER_NID = "8e3263a7-493b-f6fd-f260-549af74ea0db";
    private const string GRADIENT_COUNTER_NID = "30e2946e-432c-b0e8-363e-d29811577e30";
    private const string LUMIERE_CURTAIN_NID = "aa6633b1-4fed-a61d-092e-ed80cd949751";

    private const string NamedIDsStatesProperty = "NamedIDsStates_0";

    // Type of the NamedIDsStates map (Guid -> bool), in both uesave JSON formats
    private const string NamedIDsStatesType =
        "{\"data\": {\"Map\": {\"key_type\": {\"Struct\": {\"struct_type\": \"Guid\", \"id\": \"00000000-0000-0000-0000-000000000000\"}},\"value_type\": {\"Other\": \"BoolProperty\"}}}}";

    /// <summary>
    /// Sets named ID flags in a save converted to JSON by uesave. Supports both JSON formats: uesave 0.7+
    /// (types in "schemas", map entries as {"key": guid, "value": bool}) and older versions (type in "tag",
    /// entries as {"key": {"Struct": {"Guid": guid}}, "value": {"Bool": bool}}).
    /// </summary>
    public static string PatchJson(string json, Dictionary<string, bool> flags)
    {
        var save = JObject.Parse(json);
        var properties = (JObject)save["root"]!["properties"]!;
        var schemas = save["schemas"]?["schemas"] as JObject;
        var newFormat = save["schemas"] != null;

        if (properties[NamedIDsStatesProperty] == null)
        {
            if (newFormat)
            {
                properties[NamedIDsStatesProperty] = new JArray();
                if (schemas != null && schemas["NamedIDsStates"] == null)
                {
                    schemas["NamedIDsStates"] = JObject.Parse(NamedIDsStatesType);
                }
            }
            else
            {
                properties[NamedIDsStatesProperty] = new JObject
                {
                    ["tag"] = JObject.Parse(NamedIDsStatesType),
                    ["Map"] = new JArray(),
                };
            }
        }

        var entries = properties[NamedIDsStatesProperty] is JArray array ? array : (JArray)properties[NamedIDsStatesProperty]!["Map"]!;
        var isOldEntry = properties[NamedIDsStatesProperty] is JObject;

        foreach (var (guid, value) in flags)
        {
            var entry = entries.FirstOrDefault(e =>
                (isOldEntry ? e["key"]?["Struct"]?["Guid"] : e["key"])?.ToString() == guid);
            if (entry == null)
            {
                entries.Add(isOldEntry
                    ? new JObject { ["key"] = new JObject { ["Struct"] = new JObject { ["Guid"] = guid } }, ["value"] = new JObject { ["Bool"] = value } }
                    : new JObject { ["key"] = guid, ["value"] = value });
            }
            else if (isOldEntry)
            {
                entry["value"]!["Bool"] = value;
            }
            else
            {
                entry["value"] = value;
            }
        }

        return save.ToString(Formatting.Indented);
    }

    private static void RunUesave(string arguments)
    {
        var startInfo = new ProcessStartInfo("uesave.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo);
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"uesave.exe failed with exit code {process.ExitCode}: {errors.Result}".Trim());
        }
    }

    /// <summary>
    /// Sets the flags in the save file. The original save is kept next to it as .bak.
    /// </summary>
    public static void Patch(string saveFilePath, Dictionary<string, bool> flags)
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), $"e33rando_save_{Guid.NewGuid():N}.json");
        try
        {
            RunUesave($"to-json -i \"{saveFilePath}\" -o \"{jsonPath}\"");
            File.WriteAllText(jsonPath, PatchJson(File.ReadAllText(jsonPath), flags));

            File.Copy(saveFilePath, saveFilePath + ".bak", true);
            RunUesave($"from-json -i \"{jsonPath}\" -o \"{saveFilePath}\"");
        }
        finally
        {
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
        }
    }

    public static void AddCounters(string saveFilePath)
    {
        var flags = new Dictionary<string, bool>()
        {
            {JUMP_COUNTER_NID, true},
            {GRADIENT_COUNTER_NID, true}
        };
        Patch(saveFilePath, flags);
    }

    public static void FixCurtain(string saveFilePath)
    {
        var flags = new Dictionary<string, bool>()
        {
            {LUMIERE_CURTAIN_NID, false}
        };
        Patch(saveFilePath, flags);
    }
}
