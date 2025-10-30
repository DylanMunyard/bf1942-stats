using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.PlayerStats.Models;
using NodaTime;

namespace junie_des_1942stats.PlayerTracking;

public class PlayerTrackerDbContext : DbContext
{
    public DbSet<Player> Players { get; set; }
    public DbSet<GameServer> Servers { get; set; }
    public DbSet<PlayerSession> PlayerSessions { get; set; }
    public DbSet<PlayerObservation> PlayerObservations { get; set; }
    public DbSet<Round> Rounds { get; set; }
    public DbSet<RoundObservation> RoundObservations { get; set; }
    public DbSet<ServerPlayerRanking> ServerPlayerRankings { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserPlayerName> UserPlayerNames { get; set; }
    public DbSet<UserFavoriteServer> UserFavoriteServers { get; set; }
    public DbSet<UserBuddy> UserBuddies { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Tournament> Tournaments { get; set; }
    public DbSet<TournamentTeam> TournamentTeams { get; set; }
    public DbSet<TournamentTeamPlayer> TournamentTeamPlayers { get; set; }
    public DbSet<TournamentMatch> TournamentMatches { get; set; }
    public DbSet<TournamentMatchMap> TournamentMatchMaps { get; set; }

    public PlayerTrackerDbContext(DbContextOptions<PlayerTrackerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Player entity
        modelBuilder.Entity<Player>()
            .HasKey(p => p.Name);

        // Configure GameServer entity
        modelBuilder.Entity<GameServer>()
            .HasKey(s => s.Guid);

        // Configure PlayerSession entity
        modelBuilder.Entity<PlayerSession>()
            .HasKey(ps => ps.SessionId);

        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.PlayerName, ps.ServerGuid, ps.IsActive });

        // Add composite index for efficient lastRounds query performance
        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.ServerGuid, ps.StartTime, ps.MapName });

        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.ServerGuid, ps.LastSeenTime });

        // Add index optimized for online players query (IsActive + LastSeenTime)
        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.IsActive, ps.LastSeenTime });

        // Indexes for Round filtering from PlayerSession
        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => ps.RoundId);

        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.RoundId, ps.PlayerName });

        // Configure PlayerObservation entity
        modelBuilder.Entity<PlayerObservation>()
            .HasKey(po => po.ObservationId);

        modelBuilder.Entity<PlayerObservation>()
            .HasIndex(po => po.SessionId);

        modelBuilder.Entity<PlayerObservation>()
            .HasIndex(po => po.Timestamp);

        // Composite index to optimize queries that filter by SessionId and Timestamp
        modelBuilder.Entity<PlayerObservation>()
            .HasIndex(po => new { po.SessionId, po.Timestamp });

        // Configure ServerPlayerRanking entity
        modelBuilder.Entity<ServerPlayerRanking>()
            .HasKey(r => r.Id);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasIndex(r => new { r.ServerGuid, r.PlayerName, r.Year, r.Month })
            .IsUnique();

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasIndex(r => new { r.ServerGuid, r.Rank });

        // Configure Round entity
        modelBuilder.Entity<Round>()
            .HasKey(r => r.RoundId);

        modelBuilder.Entity<Round>()
            .HasIndex(r => new { r.ServerGuid, r.EndTime });

        modelBuilder.Entity<Round>()
            .HasIndex(r => new { r.ServerGuid, r.StartTime });

        modelBuilder.Entity<Round>()
            .HasIndex(r => r.MapName);

        modelBuilder.Entity<Round>()
            .HasIndex(r => r.IsActive);

        // One active round per server (partial unique index)
        modelBuilder.Entity<Round>()
            .HasIndex(r => r.ServerGuid)
            .IsUnique()
            .HasFilter("IsActive = 1");

        // Check constraint to ensure EndTime >= StartTime when EndTime is not null
        modelBuilder.Entity<Round>()
            .ToTable(t => t.HasCheckConstraint("CK_Round_EndTime", "EndTime IS NULL OR EndTime >= StartTime"));

        // Configure relationship: Round -> GameServer
        modelBuilder.Entity<Round>()
            .HasOne(r => r.GameServer)
            .WithMany()
            .HasForeignKey(r => r.ServerGuid)
            .HasPrincipalKey(gs => gs.Guid);

        // Configure RoundObservation entity
        modelBuilder.Entity<RoundObservation>()
            .HasKey(ro => ro.Id);

        modelBuilder.Entity<RoundObservation>()
            .HasIndex(ro => ro.RoundId);

        modelBuilder.Entity<RoundObservation>()
            .HasIndex(ro => new { ro.RoundId, ro.Timestamp });

        // Configure relationships
        modelBuilder.Entity<PlayerSession>()
            .HasOne(ps => ps.Player)
            .WithMany(p => p.Sessions)
            .HasForeignKey(ps => ps.PlayerName);

        modelBuilder.Entity<PlayerSession>()
            .HasOne(ps => ps.Server)
            .WithMany(s => s.Sessions)
            .HasForeignKey(ps => ps.ServerGuid);

        // Relationship: PlayerSession → Round (optional FK)
        modelBuilder.Entity<PlayerSession>()
            .HasOne<Round>()
            .WithMany(r => r.Sessions)
            .HasForeignKey(ps => ps.RoundId)
            .HasPrincipalKey(r => r.RoundId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PlayerObservation>()
            .HasOne(po => po.Session)
            .WithMany(ps => ps.Observations)
            .HasForeignKey(po => po.SessionId);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasOne(sr => sr.Player)
            .WithMany(p => p.PlayerRankings)
            .HasForeignKey(sr => sr.PlayerName);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasOne(sr => sr.Server)
            .WithMany(s => s.PlayerRankings)
            .HasForeignKey(sr => sr.ServerGuid);

        // Configure User entity
        modelBuilder.Entity<User>()
            .HasKey(u => u.Id);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // Configure UserPlayerName entity
        modelBuilder.Entity<UserPlayerName>()
            .HasKey(upn => upn.Id);

        modelBuilder.Entity<UserPlayerName>()
            .HasIndex(upn => new { upn.UserId, upn.PlayerName })
            .IsUnique();

        // Configure UserFavoriteServer entity
        modelBuilder.Entity<UserFavoriteServer>()
            .HasKey(ufs => ufs.Id);

        modelBuilder.Entity<UserFavoriteServer>()
            .HasIndex(ufs => new { ufs.UserId, ufs.ServerGuid })
            .IsUnique();

        // Index for notification queries - find users by server
        modelBuilder.Entity<UserFavoriteServer>()
            .HasIndex(ufs => ufs.ServerGuid);

        // Configure UserBuddy entity
        modelBuilder.Entity<UserBuddy>()
            .HasKey(ub => ub.Id);

        modelBuilder.Entity<UserBuddy>()
            .HasIndex(ub => new { ub.UserId, ub.BuddyPlayerName })
            .IsUnique();

        // Index for notification queries - find users by buddy player name
        modelBuilder.Entity<UserBuddy>()
            .HasIndex(ub => ub.BuddyPlayerName);

        // Configure relationships for dashboard settings
        modelBuilder.Entity<UserPlayerName>()
            .HasOne(upn => upn.User)
            .WithMany(u => u.PlayerNames)
            .HasForeignKey(upn => upn.UserId);

        modelBuilder.Entity<UserPlayerName>()
            .HasOne(upn => upn.Player)
            .WithMany()
            .HasForeignKey(upn => upn.PlayerName);

        modelBuilder.Entity<UserFavoriteServer>()
            .HasOne(ufs => ufs.User)
            .WithMany(u => u.FavoriteServers)
            .HasForeignKey(ufs => ufs.UserId);

        modelBuilder.Entity<UserFavoriteServer>()
            .HasOne(ufs => ufs.Server)
            .WithMany()
            .HasForeignKey(ufs => ufs.ServerGuid);

        modelBuilder.Entity<UserBuddy>()
            .HasOne(ub => ub.User)
            .WithMany(u => u.Buddies)
            .HasForeignKey(ub => ub.UserId);

        modelBuilder.Entity<UserBuddy>()
            .HasOne(ub => ub.Player)
            .WithMany()
            .HasForeignKey(ub => ub.BuddyPlayerName);

        // Configure ServerBestScoreRaw as keyless entity (for query results only)
        modelBuilder.Entity<ServerBestScoreRaw>()
            .HasNoKey();

        // Configure RefreshToken entity
        modelBuilder.Entity<RefreshToken>()
            .HasKey(rt => rt.Id);

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.TokenHash)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.UserId);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(rt => rt.User)
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure Tournament entity
        modelBuilder.Entity<Tournament>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<Tournament>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<Tournament>()
            .HasIndex(t => t.Organizer);

        modelBuilder.Entity<Tournament>()
            .HasIndex(t => t.CreatedByUserEmail);

        modelBuilder.Entity<Tournament>()
            .HasIndex(t => t.Game);

        // Configure relationship: Tournament -> User (CreatedBy)
        modelBuilder.Entity<Tournament>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: Tournament -> Player (Organizer)
        modelBuilder.Entity<Tournament>()
            .HasOne(t => t.OrganizerPlayer)
            .WithMany()
            .HasForeignKey(t => t.Organizer)
            .HasPrincipalKey(p => p.Name)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: Tournament -> GameServer (optional)
        modelBuilder.Entity<Tournament>()
            .HasOne(t => t.Server)
            .WithMany()
            .HasForeignKey(t => t.ServerGuid)
            .HasPrincipalKey(gs => gs.Guid)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Configure NodaTime Instant conversions for Tournament
        modelBuilder.Entity<Tournament>()
            .Property(t => t.CreatedAt)
            .HasConversion(
                instant => instant.ToString(),
                str => NodaTime.Text.InstantPattern.ExtendedIso.Parse(str).Value);

        // Configure relationship: Tournament -> TournamentTeam
        modelBuilder.Entity<Tournament>()
            .HasMany(t => t.TournamentTeams)
            .WithOne(tt => tt.Tournament)
            .HasForeignKey(tt => tt.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: Tournament -> TournamentMatch
        modelBuilder.Entity<Tournament>()
            .HasMany(t => t.TournamentMatches)
            .WithOne(tm => tm.Tournament)
            .HasForeignKey(tm => tm.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);


        // Configure TournamentTeam entity
        modelBuilder.Entity<TournamentTeam>()
            .HasKey(tt => tt.Id);

        modelBuilder.Entity<TournamentTeam>()
            .HasIndex(tt => tt.TournamentId);

        modelBuilder.Entity<TournamentTeam>()
            .HasIndex(tt => tt.CreatedAt);

        // Configure NodaTime Instant conversion for TournamentTeam
        modelBuilder.Entity<TournamentTeam>()
            .Property(tt => tt.CreatedAt)
            .HasConversion(
                instant => instant.ToString(),
                str => NodaTime.Text.InstantPattern.ExtendedIso.Parse(str).Value);


        // Configure TournamentTeamPlayer entity
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasKey(ttp => ttp.Id);

        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasIndex(ttp => ttp.TournamentTeamId);

        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasIndex(ttp => ttp.PlayerName);

        // Unique constraint: a player can only be in a team once
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasIndex(ttp => new { ttp.TournamentTeamId, ttp.PlayerName })
            .IsUnique();

        // Configure relationship: TournamentTeamPlayer -> TournamentTeam
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasOne(ttp => ttp.TournamentTeam)
            .WithMany(tt => tt.TeamPlayers)
            .HasForeignKey(ttp => ttp.TournamentTeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentTeamPlayer -> Player
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasOne(ttp => ttp.Player)
            .WithMany()
            .HasForeignKey(ttp => ttp.PlayerName)
            .HasPrincipalKey(p => p.Name)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure TournamentMatch entity
        modelBuilder.Entity<TournamentMatch>()
            .HasKey(tm => tm.Id);

        modelBuilder.Entity<TournamentMatch>()
            .HasIndex(tm => tm.TournamentId);

        modelBuilder.Entity<TournamentMatch>()
            .HasIndex(tm => tm.ScheduledDate);

        modelBuilder.Entity<TournamentMatch>()
            .HasIndex(tm => tm.Team1Id);

        modelBuilder.Entity<TournamentMatch>()
            .HasIndex(tm => tm.Team2Id);

        modelBuilder.Entity<TournamentMatch>()
            .HasIndex(tm => tm.CreatedAt);

        // Configure NodaTime Instant conversions for TournamentMatch
        modelBuilder.Entity<TournamentMatch>()
            .Property(tm => tm.ScheduledDate)
            .HasConversion(
                instant => instant.ToString(),
                str => NodaTime.Text.InstantPattern.ExtendedIso.Parse(str).Value);

        modelBuilder.Entity<TournamentMatch>()
            .Property(tm => tm.CreatedAt)
            .HasConversion(
                instant => instant.ToString(),
                str => NodaTime.Text.InstantPattern.ExtendedIso.Parse(str).Value);


        // Configure relationship: TournamentMatch -> Team1
        modelBuilder.Entity<TournamentMatch>()
            .HasOne(tm => tm.Team1)
            .WithMany()
            .HasForeignKey(tm => tm.Team1Id)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: TournamentMatch -> Team2
        modelBuilder.Entity<TournamentMatch>()
            .HasOne(tm => tm.Team2)
            .WithMany()
            .HasForeignKey(tm => tm.Team2Id)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: TournamentMatch -> GameServer (optional)
        modelBuilder.Entity<TournamentMatch>()
            .HasOne(tm => tm.Server)
            .WithMany()
            .HasForeignKey(tm => tm.ServerGuid)
            .HasPrincipalKey(s => s.Guid)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Configure relationship: TournamentMatch -> TournamentMatchMaps
        modelBuilder.Entity<TournamentMatch>()
            .HasMany(tm => tm.Maps)
            .WithOne(tmm => tmm.Match)
            .HasForeignKey(tmm => tmm.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure TournamentMatchMap entity
        modelBuilder.Entity<TournamentMatchMap>()
            .HasKey(tmm => tmm.Id);

        modelBuilder.Entity<TournamentMatchMap>()
            .HasIndex(tmm => tmm.MatchId);

        modelBuilder.Entity<TournamentMatchMap>()
            .HasIndex(tmm => tmm.RoundId);

        // Index on MatchId + MapOrder (NOT unique to allow same map multiple times)
        modelBuilder.Entity<TournamentMatchMap>()
            .HasIndex(tmm => new { tmm.MatchId, tmm.MapOrder });

        // Configure relationship: TournamentMatchMap -> Round (optional)
        modelBuilder.Entity<TournamentMatchMap>()
            .HasOne(tmm => tmm.Round)
            .WithMany()
            .HasForeignKey(tmm => tmm.RoundId)
            .HasPrincipalKey(r => r.RoundId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}

public class Player
{
    public string Name { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public int TotalPlayTimeMinutes { get; set; }

    public bool AiBot { get; set; }

    // Navigation property
    public List<PlayerSession> Sessions { get; set; } = [];
    public List<ServerPlayerRanking> PlayerRankings { get; set; } = [];
}

public class GameServer
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public string GameId { get; set; } = "";
    public string Game { get; set; } = ""; // Standardized game type: bf1942, fh2, bfvietnam

    // Server info fields from bflist API
    public int? MaxPlayers { get; set; }
    public string? MapName { get; set; }
    public string? JoinLink { get; set; }

    // Current map being played (updated from active player sessions)
    public string? CurrentMap { get; set; }

    // Online status tracking
    public bool IsOnline { get; set; } = true;
    public DateTime LastSeenTime { get; set; } = DateTime.UtcNow;

    // GeoLocation fields (populated via ipinfo.io lookup when IP changes or no geolocation is stored)
    public string? Country { get; set; }
    public string? Region { get; set; }
    public string? City { get; set; }
    public string? Loc { get; set; } // latitude,longitude
    public string? Timezone { get; set; }
    public string? Org { get; set; } // ASN/Org info
    public string? Postal { get; set; }
    public DateTime? GeoLookupDate { get; set; } // When this lookup was performed

    // Community links
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }

    // Navigation property
    public List<PlayerSession> Sessions { get; set; } = [];
    public List<ServerPlayerRanking> PlayerRankings { get; set; } = [];
}

public class PlayerSession
{
    public int SessionId { get; set; } // Auto-incremented
    public string PlayerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime LastSeenTime { get; set; }
    public bool IsActive { get; set; } // True if session is ongoing
    public int ObservationCount { get; set; } // Number of times player was observed in this session
    public int TotalScore { get; set; } // Can track highest score or final score
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public string? RoundId { get; set; }

    // Current live state - updated with each observation for performance
    public int CurrentPing { get; set; } = 0;
    public int CurrentTeam { get; set; } = 1;
    public string CurrentTeamLabel { get; set; } = "";

    // Average ping for the session (calculated when session ends)
    public double? AveragePing { get; set; }

    // Navigation properties
    public Player Player { get; set; } = null!;
    public GameServer Server { get; set; } = null!;
    public List<PlayerObservation> Observations { get; set; } = new();
}

public class Round
{
    public string RoundId { get; set; } = ""; // PK (hash prefix)
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsActive { get; set; }
    public int? DurationMinutes { get; set; }
    public int? ParticipantCount { get; set; }
    public int? Tickets1 { get; set; }
    public int? Tickets2 { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
    public int? RoundTimeRemain { get; set; }

    // Navigation properties
    public List<PlayerSession> Sessions { get; set; } = new();
    public GameServer? GameServer { get; set; } // Navigation property to GameServer
}

public class RoundObservation
{
    public int Id { get; set; }
    public string RoundId { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int? Tickets1 { get; set; }
    public int? Tickets2 { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
    public int? RoundTimeRemain { get; set; }
}

public class PlayerObservation
{
    public int ObservationId { get; set; }
    public int SessionId { get; set; } // Foreign key to PlayerSession
    public DateTime Timestamp { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }

    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";

    // Navigation property
    public PlayerSession Session { get; set; } = null!;
}

public class ServerPlayerRanking
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Rank { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }

    // Navigation properties
    public GameServer Server { get; set; } = null!;
    public Player Player { get; set; } = null!;
}

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoggedIn { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties for dashboard settings
    public List<UserPlayerName> PlayerNames { get; set; } = [];
    public List<UserFavoriteServer> FavoriteServers { get; set; } = [];
    public List<UserBuddy> Buddies { get; set; } = [];
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public User User { get; set; } = null!;
}

public class UserPlayerName
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string PlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Player Player { get; set; } = null!;
}

public class UserFavoriteServer
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ServerGuid { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public GameServer Server { get; set; } = null!;
}

public class UserBuddy
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string BuddyPlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Player Player { get; set; } = null!;
}

public class Tournament
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = ""; // References Player.Name
    public string Game { get; set; } = ""; // bf1942, fh2, bfvietnam
    public Instant CreatedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByUserEmail { get; set; } = "";
    public int? AnticipatedRoundCount { get; set; }
    public byte[]? HeroImage { get; set; }
    public string? HeroImageContentType { get; set; }
    public string? ServerGuid { get; set; }

    // Community links
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;
    public Player OrganizerPlayer { get; set; } = null!;
    public GameServer? Server { get; set; }
    public List<TournamentTeam> TournamentTeams { get; set; } = [];
    public List<TournamentMatch> TournamentMatches { get; set; } = [];
}


public class TournamentTeam
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = ""; // Team name (usually clan tag)
    public Instant CreatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public List<TournamentTeamPlayer> TeamPlayers { get; set; } = [];
}

public class TournamentTeamPlayer
{
    public int Id { get; set; }
    public int TournamentTeamId { get; set; }
    public string PlayerName { get; set; } = ""; // References Player.Name

    // Navigation properties
    public TournamentTeam TournamentTeam { get; set; } = null!;
    public Player Player { get; set; } = null!;
}

public class TournamentMatchMap
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string MapName { get; set; } = "";
    public int MapOrder { get; set; } // Sequence order for maps in the match (0-based). Note: MapName is NOT unique - same map can appear multiple times with different MapOrder values

    // Link to completed round - set after the round completes
    public string? RoundId { get; set; }

    // Optional: Team that chose/assigned this map
    public int? TeamId { get; set; }

    // Navigation properties
    public TournamentMatch Match { get; set; } = null!;
    public Round? Round { get; set; }
    public TournamentTeam? Team { get; set; }
}

public class TournamentMatch
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public Instant ScheduledDate { get; set; }
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }

    // Optional server reference - may not exist until tournament starts
    public string? ServerGuid { get; set; }
    public string? ServerName { get; set; } // Fallback if ServerGuid is null

    public Instant CreatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public TournamentTeam Team1 { get; set; } = null!;
    public TournamentTeam Team2 { get; set; } = null!;
    public GameServer? Server { get; set; }
    public List<TournamentMatchMap> Maps { get; set; } = [];
}
