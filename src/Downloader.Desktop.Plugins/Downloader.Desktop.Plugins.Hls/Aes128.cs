using System.Security.Cryptography;

namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// AES-128-CBC decryption for HLS segments (RFC 8216 §5.2). The whole segment is a single CBC-encrypted
/// unit with PKCS7 padding; each segment is decrypted independently with its key + IV.
/// </summary>
public static class Aes128
{
    /// <summary>Decrypt one AES-128-CBC encrypted segment. <paramref name="key"/> and <paramref name="iv"/> must be 16 bytes.</summary>
    public static byte[] DecryptCbc(byte[] cipher, byte[] key, byte[] iv)
    {
        if (key is null || key.Length != 16) throw new ArgumentException("AES-128 key must be 16 bytes.", nameof(key));
        if (iv is null || iv.Length != 16) throw new ArgumentException("AES-128 IV must be 16 bytes.", nameof(iv));

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    /// <summary>Encrypt helper — used only by tests for round-trip verification.</summary>
    public static byte[] EncryptCbc(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }
}
