using System.Text;
using Downloader.Desktop.Plugins.Hls;
using Xunit;

namespace Downloader.Desktop.Tests.Plugins.Hls;

public class Aes128Tests
{
    private static readonly byte[] Key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
    private static readonly byte[] Iv = Convert.FromHexString("0f0e0d0c0b0a09080706050403020100");

    [Fact]
    public void Decrypt_round_trips_known_plaintext()
    {
        var plain = Encoding.UTF8.GetBytes("the quick brown fox jumps over a segment of MPEG-TS bytes 0123456789");
        var cipher = Aes128.EncryptCbc(plain, Key, Iv);

        Assert.NotEqual(plain, cipher);
        var decrypted = Aes128.DecryptCbc(cipher, Key, Iv);

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Decrypt_with_implicit_sequence_iv_round_trips()
    {
        var plain = Encoding.UTF8.GetBytes("segment whose IV is derived from its media sequence number");
        var iv = M3u8Parser.SequenceIv(7);
        var cipher = Aes128.EncryptCbc(plain, Key, iv);

        var decrypted = Aes128.DecryptCbc(cipher, Key, M3u8Parser.SequenceIv(7));

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void Wrong_key_size_throws()
    {
        Assert.Throws<ArgumentException>(() => Aes128.DecryptCbc(new byte[16], new byte[8], Iv));
        Assert.Throws<ArgumentException>(() => Aes128.DecryptCbc(new byte[16], Key, new byte[4]));
    }
}
