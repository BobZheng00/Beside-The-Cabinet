using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace InsertCoin.Mind;

/// <summary>
/// The user's API key, encrypted for their Windows account (DPAPI): unreadable from another account or machine,
/// never in settings.json, never logged.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class KeyStore(string path)
{
    private static readonly byte[] Entropy = "InsertCoin.Mind.Key"u8.ToArray();

    public bool Has => File.Exists(path);

    public string? Load()
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;            // written by another Windows account: as good as no key
        }
    }

    /// <summary>Keeps the key, or forgets it when given nothing.</summary>
    public void Save(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser));
    }
}
