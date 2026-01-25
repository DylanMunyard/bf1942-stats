namespace api.AdminData.Models;

public record UndeleteRoundResponse(
    string RoundId,
    int SessionsRestored,
    int RoundRestored,
    int AffectedPlayers
);
