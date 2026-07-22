namespace VersionGraph.Models;

/// <summary>
/// %APPDATA%\VersionGraph\config.json에 저장되는 마지막 선택 상태. 재실행 시 자동 복원용.
/// </summary>
public sealed class AppConfig
{
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public string? LocalPath { get; set; }
}

/// <summary>레포 선택 화면에 표시할 최소 정보.</summary>
public sealed record RepoSummary(string Owner, string Name, string FullName, string CloneUrl, bool Private);
