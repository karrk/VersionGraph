namespace VersionGraph.Models;

/// <summary>
/// 레이아웃까지 끝난 커밋 그래프 스냅샷. GraphCanvasControl이 그대로 렌더링에 사용.
/// </summary>
public sealed class GraphModel
{
    public required IReadOnlyList<CommitNode> Commits { get; init; }
    public required int LaneCount { get; init; }

    public static readonly GraphModel Empty = new() { Commits = [], LaneCount = 0 };
}
