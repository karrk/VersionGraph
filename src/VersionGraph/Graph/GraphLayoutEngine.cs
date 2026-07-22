using VersionGraph.Models;

namespace VersionGraph.Graph;

/// <summary>
/// git log --graph 스타일의 레인(열) 배정. 최신→과거 순으로 순회하며
/// 각 레인이 "다음에 나와야 할 커밋 SHA"를 기다리는 방식으로 동작한다.
/// </summary>
public static class GraphLayoutEngine
{
    // 마스터(트렁크) 계보 전용 색상 인덱스. 다른 브랜치가 새로 생기거나 커밋 순서가
    // 바뀌어도 이 값만은 절대 재배정하지 않아 트렁크가 항상 같은 색을 유지한다.
    private const int TrunkColorIndex = 0;

    /// <summary>
    /// commits는 반드시 최신 → 과거(부모가 자식 뒤에 오는) 순서여야 한다.
    /// primaryTipSha를 주면 그 커밋의 전체 조상(마스터 계보)은 레인 배정과 무관하게
    /// 항상 TrunkColorIndex로 고정된다.
    /// </summary>
    /// <returns>이번 레이아웃에서 사용된 최대 레인 수.</returns>
    public static int Assign(IReadOnlyList<CommitNode> commits, string? primaryTipSha = null)
    {
        var trunkShas = BuildTrunkShas(commits, primaryTipSha);

        // 각 레인이 기다리는 커밋 SHA. null = 빈 레인(재사용 가능).
        var laneWaitingSha = new List<string?>();
        var laneColor = new List<int>();
        // 트렁크가 있으면 0번 색은 트렁크 전용으로 예약해두고, 나머지 브랜치는 1번부터 받는다.
        var nextColor = trunkShas.Count > 0 ? TrunkColorIndex + 1 : TrunkColorIndex;

        foreach (var commit in commits)
        {
            var isTrunk = trunkShas.Contains(commit.Sha);

            var lane = laneWaitingSha.IndexOf(commit.Sha);
            if (lane == -1)
            {
                // 아무 레인도 기다리지 않는 커밋 = 새 브랜치의 시작점(다른 tip에서 처음 도달).
                lane = AllocateLane(laneWaitingSha, laneColor, ref nextColor);
            }
            laneWaitingSha[lane] = null; // 배치 완료, 자식 결정 전까지 임시로 비움
            commit.Lane = lane;
            commit.ColorIndex = isTrunk ? TrunkColorIndex : laneColor[lane];

            var edges = new List<CommitEdge>();

            for (var i = 0; i < commit.ParentShas.Count; i++)
            {
                var parentSha = commit.ParentShas[i];
                var existingLane = laneWaitingSha.IndexOf(parentSha);
                int targetLane;

                if (i == 0 && existingLane == -1)
                {
                    // 첫 부모이고 아무도 기다리지 않으면 같은 레인을 이어받음 (직선 경로 유지)
                    laneWaitingSha[lane] = parentSha;
                    targetLane = lane;
                }
                else if (existingLane != -1)
                {
                    // 다른 레인이 이미 같은 부모를 기다림 (분기점으로 합류) - 그 레인 쪽으로 꺾어 그림
                    targetLane = existingLane;
                }
                else
                {
                    targetLane = AllocateLane(laneWaitingSha, laneColor, ref nextColor);
                    laneWaitingSha[targetLane] = parentSha;
                }

                // 선 색은 도착지(부모)가 아니라 출발지(자기 자신) 커밋 색을 따라간다.
                // 그래야 트렁크에 합류하는 지점까지 브랜치 고유 색이 줄기 전체에 유지된다.
                edges.Add(new CommitEdge(parentSha, lane, targetLane, commit.ColorIndex));
            }

            commit.Edges = edges;
        }

        return laneWaitingSha.Count;
    }

    // primaryTipSha에서 부모를 계속 따라가며 만나는 모든 커밋(=마스터가 담고 있는 이력 전체)을 모은다.
    private static HashSet<string> BuildTrunkShas(IReadOnlyList<CommitNode> commits, string? primaryTipSha)
    {
        var trunkShas = new HashSet<string>();
        if (primaryTipSha is null)
            return trunkShas;

        var bySha = commits.ToDictionary(c => c.Sha);
        if (!bySha.ContainsKey(primaryTipSha))
            return trunkShas;

        var stack = new Stack<string>();
        stack.Push(primaryTipSha);
        while (stack.Count > 0)
        {
            var sha = stack.Pop();
            if (!trunkShas.Add(sha))
                continue; // 이미 방문함

            if (!bySha.TryGetValue(sha, out var node))
                continue; // 히스토리 경계(shallow) 밖

            foreach (var parentSha in node.ParentShas)
                stack.Push(parentSha);
        }

        return trunkShas;
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
