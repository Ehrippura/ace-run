using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ace_run.Models;

namespace ace_run.Services;

public static class DataService
{
    private static readonly string s_filePath;

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
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

        // Try loading as new AppData format first
        try
        {
            var data = JsonSerializer.Deserialize<AppData>(json, s_options);
            if (data is not null)
                return data;
        }
        catch { }

        // Backward compat: try loading as flat List<AppItem> (old format)
        try
        {
            var items = JsonSerializer.Deserialize<List<AppItem>>(json);
            if (items is not null)
            {
                var data = new AppData();
                data.Items.AddRange(items);
                return data;
            }
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
