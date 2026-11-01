using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.StagingTransfer;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence.Encryption;

namespace TacticusPlanner.Api.Tests;

public sealed class StagingTransferCodeServiceTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AesGcmColumnEncryptionService Encryption(byte seed) => new(Options.Create(new ColumnEncryptionOptions
    {
        CurrentKeyVersion = "v1",
        Keys = { ["v1"] = Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()) },
    }));

    private readonly ManualTime time = new();

    [Fact]
    public void CodeRoundTripsToTheProfileUntilItExpires()
    {
        var service = new StagingTransferCodeService(Encryption(1), time);
        var profileId = ProfileId.From(Guid.CreateVersion7());

        var (code, expiresAt) = service.Mint(profileId);

        Assert.StartsWith("TP1-", code, StringComparison.Ordinal);
        Assert.Equal(time.Now + StagingTransferCodeService.Lifetime, expiresAt);
        Assert.Equal(profileId, service.Read($"  {code} "));

        time.Now = expiresAt.AddSeconds(-1);
        Assert.Equal(profileId, service.Read(code));

        time.Now = expiresAt;
        Assert.Null(service.Read(code));
    }

    [Fact]
    public void CodeSealedWithAnotherKeyIsRefused()
    {
        var (code, _) = new StagingTransferCodeService(Encryption(1), time).Mint(ProfileId.From(Guid.CreateVersion7()));

        Assert.Null(new StagingTransferCodeService(Encryption(2), time).Read(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TP1-")]
    [InlineData("TP1-v1:a:b:c")]
    [InlineData("TP1-v9:AAAAAAAAAAAAAAAA:AAAAAAAAAAAAAAAAAAAAAA:AAAA")]
    [InlineData("not a code")]
    public void MalformedCodeIsRefused(string? code)
    {
        Assert.Null(new StagingTransferCodeService(Encryption(1), time).Read(code));
    }

    [Fact]
    public void StoredColumnCiphertextIsNotACode()
    {
        var encryption = Encryption(1);
        var stored = encryption.Encrypt(
            $"1|{Guid.CreateVersion7():N}|{time.Now.AddDays(1).ToUnixTimeSeconds()}",
            "tacticus_integrations.tacticus_api_key")!;
        var forged = "TP1-" + stored[AesGcmColumnEncryptionService.CurrentEnvelopePrefix.Length..];

        Assert.Null(new StagingTransferCodeService(encryption, time).Read(forged));
    }
}
