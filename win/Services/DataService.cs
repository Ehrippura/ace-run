using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

public static class DataService
{
    private static readonly string s_filePath;

    static DataService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AceRun");
        Directory.CreateDirectory(folder);
        s_filePath = Path.Combine(folder, "apps.json");
    }

    public static List<AppItem> Load()
    {
        if (!File.Exists(s_filePath))
            return new List<AppItem>();

        var json = File.ReadAllText(s_filePath);
        return JsonSerializer.Deserialize<List<AppItem>>(json) ?? new List<AppItem>();
    }

    public static void Save(IEnumerable<AppItem> items)
    {
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(s_filePath, json);
    }
}
