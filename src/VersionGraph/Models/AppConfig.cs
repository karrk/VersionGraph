namespace VersionGraph.Models;

/// <summary>
/// %APPDATA%\VersionGraph\config.json에 저장되는 마지막 선택 상태. 재실행 시 자동 복원용.
/// </summary>
public sealed class AppConfig
{
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? LocalPath { get; set; }

    /// <summary>"owner/name" -> 로컬 경로. 레포 선택 화면에서 폴더를 재지정하지 않도록 기억해둔다.</summary>
    public Dictionary<string, string> RepoLocalPaths { get; set; } = new();

    public string? GetRepoLocalPath(string owner, string name) =>
        RepoLocalPaths.TryGetValue(Key(owner, name), out var path) ? path : null;

    public void SetRepoLocalPath(string owner, string name, string path) =>
        RepoLocalPaths[Key(owner, name)] = path;

    public void RemoveRepoLocalPath(string owner, string name) =>
        RepoLocalPaths.Remove(Key(owner, name));

    private static string Key(string owner, string name) => $"{owner}/{name}";
}

/// <summary>레포 선택 화면에 표시할 최소 정보.</summary>
public sealed record RepoSummary(string Owner, string Name, string FullName, string CloneUrl, bool Private);
