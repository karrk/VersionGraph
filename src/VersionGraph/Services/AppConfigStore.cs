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

    /// <summary>Load → 수정 → Save를 한 번에 해서, 다른 필드(예: 레포별 경로 기록)를 덮어쓰지 않게 한다.</summary>
    public static void Update(Action<AppConfig> mutate)
    {
        var config = Load();
        mutate(config);
        Save(config);
    }

    /// <summary>추적 중지 시 "마지막 접속 레포" 정보만 비운다. 레포별 경로 기록(RepoLocalPaths)은 유지.</summary>
    public static void ClearActiveRepo() =>
        Update(config =>
        {
            config.RepoOwner = null;
            config.RepoName = null;
            config.LocalPath = null;
        });
}
