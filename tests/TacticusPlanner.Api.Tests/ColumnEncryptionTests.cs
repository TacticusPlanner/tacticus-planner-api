using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Tests;

public sealed class ColumnEncryptionTests
{
    private const string Column = "profiles.tacticus_user_id";

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    private static ColumnEncryptionOptions Config(string? currentKeyVersion = "v1", string? key = null) => new()
    {
        CurrentKeyVersion = currentKeyVersion,
        Keys = { ["v1"] = key ?? Convert.ToBase64String(Key) },
    };

    private static AesGcmColumnEncryptionService Encryption() =>
        new(Options.Create(Config()));

    private static HmacColumnHashService Hashing() =>
        new(Options.Create(Config()));

    [Fact]
    public void NewEnvelopeRoundTripsAndIsVersioned()
    {
        var envelope = Encryption().Encrypt("user-123", Column);

        Assert.StartsWith($"{AesGcmColumnEncryptionService.CurrentEnvelopePrefix}v1:", envelope, StringComparison.Ordinal);
        Assert.Equal("user-123", Encryption().Decrypt(envelope, Column));
    }

    [Fact]
    public void EnvelopeWithoutTheVersionIsRefused()
    {
        var unversioned = Encryption().Encrypt("user-123", Column)!.Replace("tpenc:2:", "tpenc:", StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => Encryption().Decrypt(unversioned, Column));
    }

    [Fact]
    public void CiphertextMovedToAnotherColumnFailsToDecrypt()
    {
        var envelope = Encryption().Encrypt("user-123", Column);

        Assert.ThrowsAny<CryptographicException>(() => Encryption().Decrypt(envelope, "guild_members.tacticus_user_id"));
    }

    [Fact]
    public void PlaintextThatLooksLikeAnEnvelopeIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Encryption().Encrypt("tpenc:anything", Column));
    }

    [Fact]
    public void HashUsesADerivedSubkeyNotTheEncryptionKey()
    {
        var hash = Hashing().ComputeHash("user-123");

        Assert.Equal(hash, Hashing().ComputeHash("user-123"));
        Assert.NotEqual(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes("user-123")), hash);
        Assert.NotEqual(hash, Hashing().ComputeHash("user-124"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("v2", null)]
    [InlineData("v1", "AAAA")]
    [InlineData("v1", "not base64!")]
    public void InvalidOptionsAreRejected(string? currentKeyVersion, string? key)
    {
        Assert.False(Config(currentKeyVersion, key).IsValid());
    }

    [Fact]
    public void ValidOptionsAreAccepted()
    {
        Assert.True(Config().IsValid());
    }

    [Fact]
    public void UnknownCurrentKeyVersionFailsAtStartup()
    {
        using var ownerFactory = new GameCatalogApiFactory();
        using var factory = ownerFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ColumnEncryption:CurrentKeyVersion"] = "missing",
                })));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ColumnEncryption:CurrentKeyVersion", exception.Message, StringComparison.Ordinal);
    }
}
