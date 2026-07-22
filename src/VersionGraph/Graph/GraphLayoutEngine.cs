using VersionGraph.Models;

namespace VersionGraph.Graph;

/// <summary>
/// git log --graph 스타일의 레인(열) 배정. 최신→과거 순으로 순회하며
/// 각 레인이 "다음에 나와야 할 커밋 SHA"를 기다리는 방식으로 동작한다.
/// </summary>
public static class GraphLayoutEngine
{
    /// <summary>commits는 반드시 최신 → 과거(부모가 자식 뒤에 오는) 순서여야 한다.</summary>
    /// <returns>이번 레이아웃에서 사용된 최대 레인 수.</returns>
    public static int Assign(IReadOnlyList<CommitNode> commits)
    {
        // 각 레인이 기다리는 커밋 SHA. null = 빈 레인(재사용 가능).
        var laneWaitingSha = new List<string?>();
        var laneColor = new List<int>();
        var nextColor = 0;

        foreach (var commit in commits)
        {
            var lane = laneWaitingSha.IndexOf(commit.Sha);
            if (lane == -1)
            {
                // 아무 레인도 기다리지 않는 커밋 = 새 브랜치의 시작점(다른 tip에서 처음 도달).
                lane = AllocateLane(laneWaitingSha, laneColor, ref nextColor);
            }
            laneWaitingSha[lane] = null; // 배치 완료, 자식 결정 전까지 임시로 비움
            commit.Lane = lane;
            commit.ColorIndex = laneColor[lane];

            var edges = new List<CommitEdge>();

            if (commit.ParentShas.Count == 0)
            {
                // 루트 커밋: 이 레인은 여기서 끝 (freed 상태 유지)
            }
            else
            {
                // 첫 부모는 같은 레인을 이어받음 (직선 경로 유지가 목적)
                laneWaitingSha[lane] = commit.ParentShas[0];
                edges.Add(new CommitEdge(commit.ParentShas[0], lane, lane, laneColor[lane]));

                for (var i = 1; i < commit.ParentShas.Count; i++)
                {
                    var parentSha = commit.ParentShas[i];
                    var existingLane = laneWaitingSha.IndexOf(parentSha);
                    if (existingLane != -1)
                    {
                        // 다른 레인이 이미 같은 부모를 기다림 (히스토리 합류) - 새 레인 만들지 않고 합류
                        edges.Add(new CommitEdge(parentSha, lane, existingLane, laneColor[existingLane]));
                    }
                    else
                    {
                        var mergeLane = AllocateLane(laneWaitingSha, laneColor, ref nextColor);
                        laneWaitingSha[mergeLane] = parentSha;
                        edges.Add(new CommitEdge(parentSha, lane, mergeLane, laneColor[mergeLane]));
                    }
                }
            }

            commit.Edges = edges;
        }

        return laneWaitingSha.Count;
    }

    private static int AllocateLane(List<string?> laneWaitingSha, List<int> laneColor, ref int nextColor)
    {
        var freeIndex = laneWaitingSha.IndexOf(null);
        if (freeIndex != -1)
        {
            laneColor[freeIndex] = nextColor++;
            return freeIndex;
        }

        laneWaitingSha.Add(null);
        laneColor.Add(nextColor++);
        return laneWaitingSha.Count - 1;
    }
}
