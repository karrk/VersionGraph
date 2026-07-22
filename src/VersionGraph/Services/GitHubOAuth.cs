namespace VersionGraph.Services;

/// <summary>
/// Device Flow용 OAuth App 설정. client_id는 비밀값이 아니라(공개 클라이언트) 소스에 상수로 둬도 안전.
/// GitHub Developer settings > OAuth Apps에서 "Enable Device Flow"를 켠 앱의 Client ID를 넣어야 함.
/// </summary>
public static class GitHubOAuth
{
    public const string ClientId = "REPLACE_WITH_OAUTH_APP_CLIENT_ID";

    // repo 범위: private 레포 목록 조회 및 clone/fetch 인증에 사용.
    public static readonly string[] Scopes = ["repo"];
}
