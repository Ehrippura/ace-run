using System;
using System.IO;
using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

public static class DataService
{
    private static readonly string s_filePath;

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    static DataService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AceRun");
        Directory.CreateDirectory(folder);
        s_filePath = Path.Combine(folder, "apps.json");
    }

    public static AppData Load()
    {
        if (!File.Exists(s_filePath))
            return new AppData();

        var json = File.ReadAllText(s_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new AppData();

        try
        {
            var data = JsonSerializer.Deserialize<AppData>(json, s_options);
            if (data is not null)
                return data;
        }
        catch { }

        return new AppData();
    }

    public static void Save(AppData data)
    {
        var json = JsonSerializer.Serialize(data, s_options);
        File.WriteAllText(s_filePath, json);
    }
}
