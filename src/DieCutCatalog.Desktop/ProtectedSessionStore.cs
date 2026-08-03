using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DieCutCatalog.Desktop;

internal sealed record StoredClientSession(
    string ClientVersion,
    string ServerAddress,
    string AccessToken,
    DateTimeOffset ExpiresAt);

internal static class ProtectedSessionStore
{
    private static readonly byte[] Entropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("DieCutCatalog.ClientSession.v1"));
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DieCutCatalog");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "session.dat");

    public static void Save(StoredClientSession session)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.SerializeToUtf8Bytes(session);
        var protectedData = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedData);
        File.Move(temporaryPath, FilePath, true);
    }

    public static StoredClientSession? Load(string clientVersion)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var protectedData = File.ReadAllBytes(FilePath);
            var json = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<StoredClientSession>(json);
            if (session is null
                || !string.Equals(session.ClientVersion, clientVersion, StringComparison.OrdinalIgnoreCase)
                || session.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Clear();
                return null;
            }
            return session;
        }
        catch (CryptographicException)
        {
            Clear();
            return null;
        }
        catch (JsonException)
        {
            Clear();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            var temporaryPath = FilePath + ".tmp";
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}