using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VersionGraph.Services;

/// <summary>
/// 이 PC에 이미 로그인되어 있는 GitHub 자격 증명을 탐색해 토큰을 가져온다.
/// 여기서 반환하는 값은 신뢰하지 않고, 호출부(GitHubAuthService.ValidateAsync)에서 항상 재검증한다.
/// </summary>
public static class LocalGitCredentialService
{
    private const int CredTypeGeneric = 1;

    public static async Task<string?> TryDetectTokenAsync()
    {
        return await TryGetGhCliTokenAsync() ?? TryGetWindowsCredential();
    }

    /// <summary>GitHub CLI(gh)가 설치되고 로그인되어 있으면 토큰 반환. 비대화형 호출이라 팝업이 뜨지 않음.</summary>
    private static async Task<string?> TryGetGhCliTokenAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var token = output.Trim();
            return process.ExitCode == 0 && !string.IsNullOrEmpty(token) ? token : null;
        }
        catch (Win32Exception)
        {
            // gh 미설치 - 다음 소스로 폴백
            return null;
        }
    }

    /// <summary>
    /// Windows 자격 증명 관리자에서 Git Credential Manager가 저장한 github.com 항목을 조회.
    /// CredRead 대신 CredEnumerate를 쓰는 이유: 타깃 이름이 GCM 버전마다 조금씩 달라 와일드카드 필터가 필요함.
    /// 둘 다 읽기 전용 API라 자격 증명이 없어도 대화형 로그인 팝업을 띄우지 않는다.
    /// </summary>
    private static string? TryGetWindowsCredential()
    {
        if (!CredEnumerate("git:*", 0, out var count, out var pCredentials))
            return null;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var pCredential = Marshal.ReadIntPtr(pCredentials, i * IntPtr.Size);
                var credential = Marshal.PtrToStructure<CREDENTIAL>(pCredential);

                if (credential.Type != CredTypeGeneric || credential.TargetName is null)
                    continue;

                if (!credential.TargetName.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                    continue;

                var token = ReadTokenBlob(credential);
                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }

            return null;
        }
        finally
        {
            CredFree(pCredentials);
        }
    }

    // GCM은 비밀번호(토큰)를 UTF-16LE(Unicode) 바이트로 저장함.
    private static string? ReadTokenBlob(CREDENTIAL credential)
    {
        if (credential.CredentialBlobSize == 0)
            return null;

        var blob = new byte[credential.CredentialBlobSize];
        Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
        return Encoding.Unicode.GetString(blob).Trim('\0');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string filter, int flag, out int count, out IntPtr pCredentials);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
