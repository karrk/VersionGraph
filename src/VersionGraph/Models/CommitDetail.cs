namespace VersionGraph.Models;

/// <summary>
/// 커밋 하나의 상세 정보 스냅샷. 전체화면 상세 패널에서 표시할 목적으로
/// LibGit2Sharp 커밋 + 첫 부모 기준 diff를 UI에 필요한 형태로 추출한 것.
/// </summary>
public sealed class CommitDetail
{
    public required string FullSha { get; init; }
    public required string ShortSha { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorEmail { get; init; }
    public required DateTimeOffset When { get; init; }
    public required string FullMessage { get; init; }
    public required IReadOnlyList<string> RefLabels { get; init; }
    public required IReadOnlyList<string> ParentShas { get; init; }
    public required IReadOnlyList<CommitFileChange> Files { get; init; }

    // XAML 바인딩 편의를 위한 표시용 문자열
    public string RefLabelsText => string.Join(", ", RefLabels);
    public string ParentShasText => string.Join(Environment.NewLine, ParentShas);
    public bool HasRefLabels => RefLabels.Count > 0;
}

/// <summary>커밋에서 변경된 파일 한 개의 상태·라인 통계·패치 본문(라인 단위 분류 완료 상태).</summary>
public sealed record CommitFileChange(
    string Path,
    string Status,
    int LinesAdded,
    int LinesDeleted,
    IReadOnlyList<DiffLine> Lines)
{
    public string StatsText => $"{Status}  +{LinesAdded} -{LinesDeleted}";
}

/// <summary>diff 패치의 한 줄. Kind에 따라 UI에서 배경/전경 색이 달라진다.</summary>
public sealed record DiffLine(string Text, DiffLineKind Kind);

public enum DiffLineKind
{
    Header,
    Added,
    Removed,
    Context
}
