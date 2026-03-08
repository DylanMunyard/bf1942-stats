namespace api.PlayerRelationships.Models;

/// <summary>
/// A player with a similar ping to a given player on a specific server.
/// Small PingDiff suggests they are geographically close.
/// </summary>
public record NearbyPlayer(
    string PlayerName,
    double PlayerPing,
    double OtherPing,
    double PingDiff,
    int SessionCount);
