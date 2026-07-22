using System.IO;
using System.Text.Json;
using VersionGraph.Models;

namespace VersionGraph.Services;

/// <summary>마지막으로 선택한 레포/로컬 경로를 config.json으로 저장·복원.</summary>
public static class AppConfigStore
{
    private static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VersionGraph");

    private static readonly string FilePath = Path.Combine(Directory, "config.json");

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (JsonException)
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
