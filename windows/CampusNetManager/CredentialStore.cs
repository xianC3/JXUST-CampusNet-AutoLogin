using System.Security.Cryptography;
using System.Text;

namespace CampusNetManager;

public static class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CampusNetManager.v1");

    public static bool Exists
    {
        get
        {
            SettingsStore.EnsureDataDirectory();
            return File.Exists(SettingsStore.CredentialPath);
        }
    }

    public static void Save(string password)
    {
        SettingsStore.EnsureDataDirectory();
        var plain = Encoding.UTF8.GetBytes(password);
        var protectedBytes = ProtectedData.Protect(
            plain,
            Entropy,
            DataProtectionScope.CurrentUser
        );
        File.WriteAllBytes(SettingsStore.CredentialPath, protectedBytes);
        CryptographicOperations.ZeroMemory(plain);
    }

    public static string? Load()
    {
        try
        {
            SettingsStore.EnsureDataDirectory();
            if (!File.Exists(SettingsStore.CredentialPath))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(SettingsStore.CredentialPath);
            var plain = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser
            );
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(SettingsStore.CredentialPath))
        {
            File.Delete(SettingsStore.CredentialPath);
        }
    }
}
