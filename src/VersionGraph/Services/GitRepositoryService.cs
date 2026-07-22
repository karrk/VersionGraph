using LibGit2Sharp;
using VersionGraph.Graph;
using VersionGraph.Models;

namespace VersionGraph.Services;

/// <summary>
/// 로컬 clone의 object DB를 LibGit2Sharp로 직접 읽어 커밋 그래프를 만든다.
/// 로컬 ref와 remote-tracking ref(refs/remotes/origin/*)를 모두 tip으로 사용하므로
/// fetch만 해두면 원격에서 들어온 새 커밋도 같은 그래프에 자연히 포함된다.
/// </summary>
public sealed class GitRepositoryService : IDisposable
{
    private const int MaxCommits = 2000;

    private readonly string _localPath;
    private readonly string _token;
    private Repository? _repo;

    public GitRepositoryService(string localPath, string token)
    {
        _localPath = localPath;
        _token = token;
    }

    private Repository Repo => _repo ??= new Repository(_localPath);

    public static void Clone(string cloneUrl, string localPath, string token)
    {
        var options = new CloneOptions
        {
            FetchOptions = { CredentialsProvider = (_, _, _) => BuildCredentials(token) }
        };
        Repository.Clone(cloneUrl, localPath, options);
    }

    /// <summary>로컬 clone의 origin remote URL이 선택한 레포와 같은 레포를 가리키는지 확인.</summary>
    public static bool MatchesRemote(string localPath, string expectedCloneUrl)
    {
        using var repo = new Repository(localPath);
        var origin = repo.Network.Remotes["origin"];
        if (origin is null)
            return false;

        return NormalizeUrl(origin.Url) == NormalizeUrl(expectedCloneUrl);
    }

    private static string NormalizeUrl(string url) =>
        url.TrimEnd('/').Replace(".git", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    public void Fetch()
    {
        var origin = Repo.Network.Remotes["origin"];
        var refSpecs = origin.FetchRefSpecs.Select(r => r.Specification);
        var options = new FetchOptions { CredentialsProvider = (_, _, _) => BuildCredentials(_token) };
        Commands.Fetch(Repo, origin.Name, refSpecs, options, null);
    }

    private static UsernamePasswordCredentials BuildCredentials(string token) => new()
    {
        Username = token,
        Password = string.Empty
    };

    public GraphModel BuildGraph()
    {
        var tips = Repo.Branches.Select(b => b.Tip).Where(c => c is not null).Distinct();

        var filter = new CommitFilter
        {
            IncludeReachableFrom = tips,
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time
        };

        var refLabelsBySha = BuildRefLabels();

        var commits = Repo.Commits.QueryBy(filter)
            .Take(MaxCommits)
            .Select(c => new CommitNode
            {
                Sha = c.Sha,
                ShortSha = c.Sha[..7],
                Message = c.MessageShort,
                AuthorName = c.Author.Name,
                When = c.Author.When,
                ParentShas = c.Parents.Select(p => p.Sha).ToList(),
                RefLabels = refLabelsBySha.TryGetValue(c.Sha, out var labels) ? labels : []
            })
            .ToList();

        var laneCount = GraphLayoutEngine.Assign(commits, FindPrimaryTipSha());
        return new GraphModel { Commits = commits, LaneCount = laneCount };
    }

    // 트렁크 색상 고정의 기준이 되는 브랜치. 로컬 master/main이 없으면 origin의 것을 쓴다.
    private string? FindPrimaryTipSha()
    {
        string[] candidates = ["master", "main", "origin/master", "origin/main"];
        foreach (var name in candidates)
        {
            var branch = Repo.Branches[name];
            if (branch?.Tip is not null)
                return branch.Tip.Sha;
        }

        return null;
    }

    /// <summary>커밋 하나의 상세 정보(메타데이터 + 첫 부모 기준 diff)를 만든다. diff 계산이 무거울 수 있어 백그라운드 호출 권장.</summary>
    public CommitDetail GetCommitDetail(string sha)
    {
        var commit = Repo.Lookup<Commit>(sha) ?? throw new ArgumentException($"커밋을 찾을 수 없음: {sha}");
        var refLabelsBySha = BuildRefLabels();

        // 머지 커밋도 git show 기본 동작처럼 첫 부모 기준으로 비교. 루트 커밋은 빈 트리(null)와 비교
        var parentTree = commit.Parents.FirstOrDefault()?.Tree;
        var patch = Repo.Diff.Compare<Patch>(parentTree, commit.Tree);

        var files = patch
            .Select(p => new CommitFileChange(
                p.Path,
                p.Status.ToString(),
                p.LinesAdded,
                p.LinesDeleted,
                ParseDiffLines(p.Patch)))
            .ToList();

        return new CommitDetail
        {
            FullSha = commit.Sha,
            ShortSha = commit.Sha[..7],
            AuthorName = commit.Author.Name,
            AuthorEmail = commit.Author.Email,
            When = commit.Author.When,
            FullMessage = commit.Message.TrimEnd(),
            RefLabels = refLabelsBySha.TryGetValue(commit.Sha, out var labels) ? labels : [],
            ParentShas = commit.Parents.Select(p => p.Sha).ToList(),
            Files = files
        };
    }

    // 패치 텍스트를 줄 단위로 분해해 접두사로 종류를 분류.
    // "+++"/"---"는 파일 경로 헤더라서 "+"/"-"보다 먼저 검사해야 추가/삭제로 오분류되지 않음
    private static List<DiffLine> ParseDiffLines(string patch)
    {
        var lines = new List<DiffLine>();
        foreach (var line in patch.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            var kind =
                text.StartsWith("+++") || text.StartsWith("---") || text.StartsWith("@@")
                    || text.StartsWith("diff ") || text.StartsWith("index ") ? DiffLineKind.Header
                : text.StartsWith('+') ? DiffLineKind.Added
                : text.StartsWith('-') ? DiffLineKind.Removed
                : DiffLineKind.Context;
            lines.Add(new DiffLine(text, kind));
        }
        return lines;
    }

    private Dictionary<string, List<string>> BuildRefLabels()
    {
        var map = new Dictionary<string, List<string>>();

        void Add(string sha, string label)
        {
            if (!map.TryGetValue(sha, out var list))
                map[sha] = list = [];
            list.Add(label);
        }

        foreach (var branch in Repo.Branches)
        {
            if (branch.Tip is not null)
                Add(branch.Tip.Sha, branch.FriendlyName);
        }

        foreach (var tag in Repo.Tags)
        {
            if (tag.Target is Commit c)
                Add(c.Sha, $"tag: {tag.FriendlyName}");
        }

        return map;
    }

    public void Dispose()
    {
        _repo?.Dispose();
    }
}
