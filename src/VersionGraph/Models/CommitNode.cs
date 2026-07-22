namespace VersionGraph.Models;

/// <summary>
/// 커밋 그래프의 노드 하나를 표현. LibGit2Sharp.Commit에서 UI에 필요한 값만 추출한 불변 스냅샷.
/// </summary>
public sealed class CommitNode
{
    public required string Sha { get; init; }
    public required string ShortSha { get; init; }
    public required string Message { get; init; }
    public required string AuthorName { get; init; }
    public required DateTimeOffset When { get; init; }
    public required IReadOnlyList<string> ParentShas { get; init; }
    public required IReadOnlyList<string> RefLabels { get; init; }

    // 레이아웃 엔진이 채우는 값 (열 위치, 색상 인덱스, 부모로 그릴 엣지의 레인)
    public int Lane { get; set; }
    public int ColorIndex { get; set; }
    public IReadOnlyList<CommitEdge> Edges { get; set; } = [];
}

/// <summary>
/// 커밋 노드에서 부모 노드로 이어지는 엣지 한 개. 부모가 같은 레인이면 직선, 아니면 레인 이동 곡선으로 그림.
/// </summary>
public sealed record CommitEdge(string ParentSha, int FromLane, int ToLane, int ColorIndex);
