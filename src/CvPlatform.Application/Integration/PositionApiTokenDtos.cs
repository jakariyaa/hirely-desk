namespace CvPlatform.Application.Integration;

/// <summary>Token metadata for the position form token manager. Never carries the secret.</summary>
public sealed record PositionApiTokenDto(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}

/// <summary>Result of a token generation: the plaintext token is returned exactly once.</summary>
public sealed record PositionApiTokenCreatedDto(
    Guid Id,
    string Name,
    string Token,
    DateTime CreatedAt);
