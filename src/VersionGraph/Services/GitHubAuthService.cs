using Octokit;
using VersionGraph.Models;

namespace VersionGraph.Services;

/// <summary>토큰(PAT/OAuth) 검증, Device Flow 로그인, 계정 소유 레포 목록 조회. 그래프 구성에는 관여하지 않음(로컬 clone + fetch가 담당).</summary>
public sealed class GitHubAuthService
{
    private const string ProductHeader = "VersionGraph";

    /// <summary>토큰이 유효하면 로그인한 사용자 이름을 반환, 아니면 null.</summary>
    public async Task<string?> ValidateAsync(string token)
    {
        var client = new GitHubClient(new ProductHeaderValue(ProductHeader))
        {
            Credentials = new Credentials(token)
        };

        try
        {
            var user = await client.User.Current();
            return user.Login;
        }
        catch (AuthorizationException)
        {
            return null;
        }
    }

    /// <summary>Device Flow 시작. 반환된 UserCode/VerificationUri를 사용자에게 보여줘야 함.</summary>
    public async Task<OauthDeviceFlowResponse> InitiateDeviceFlowAsync()
    {
        var client = new GitHubClient(new ProductHeaderValue(ProductHeader));
        var request = new OauthDeviceFlowRequest(GitHubOAuth.ClientId) { Scopes = { GitHubOAuth.Scopes[0] } };
        return await client.Oauth.InitiateDeviceFlow(request);
    }

    /// <summary>
    /// 사용자가 브라우저에서 코드를 입력할 때까지 GitHub 권장 interval로 내부 폴링 후 액세스 토큰 반환.
    /// 사용자가 인증을 완료하지 않고 만료(ExpiresIn)되면 예외 발생.
    /// </summary>
    public async Task<string> CompleteDeviceFlowAsync(OauthDeviceFlowResponse deviceFlow, CancellationToken ct = default)
    {
        var client = new GitHubClient(new ProductHeaderValue(ProductHeader));
        var token = await client.Oauth.CreateAccessTokenForDeviceFlow(GitHubOAuth.ClientId, deviceFlow, ct);
        return token.AccessToken;
    }

    public async Task<IReadOnlyList<RepoSummary>> ListReposAsync(string token)
    {
        var client = new GitHubClient(new ProductHeaderValue(ProductHeader))
        {
            Credentials = new Credentials(token)
        };

        var request = new RepositoryRequest
        {
            Sort = RepositorySort.Updated,
            Direction = SortDirection.Descending
        };

        var repos = await client.Repository.GetAllForCurrent(request);
        return repos
            .Select(r => new RepoSummary(r.Owner.Login, r.Name, r.FullName, r.CloneUrl, r.Private))
            .ToList();
    }
}
