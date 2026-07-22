using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VersionGraph.Services;

/// <summary>
/// GitHub PAT를 DPAPI(CurrentUser)로 암호화해 로컬에 저장. 다른 계정/PC로는 복호화 불가.
/// </summary>
public static class TokenStore
{
    private static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VersionGraph");

    private static readonly string FilePath = Path.Combine(Directory, "token.dat");

    public static void Save(string token)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var plain = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static string? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // 다른 사용자 계정에서 저장된 토큰이거나 손상됨 - 재로그인 유도
            return null;
        }
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
