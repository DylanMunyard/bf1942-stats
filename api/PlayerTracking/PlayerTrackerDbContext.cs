using api.Data.Entities;
using api.ImageStorage.Models;
using api.Players.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using System.Globalization;

namespace api.PlayerTracking;

public class PlayerTrackerDbContext : DbContext
{
    public DbSet<Player> Players { get; set; }
    public DbSet<GameServer> Servers { get; set; }
    public DbSet<PlayerSession> PlayerSessions { get; set; }
    public DbSet<PlayerObservation> PlayerObservations { get; set; }
    public DbSet<Round> Rounds { get; set; }
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
    public DbSet<TournamentMatchResult> TournamentMatchResults { get; set; }
    public DbSet<TournamentTeamRanking> TournamentTeamRankings { get; set; }
    public DbSet<TournamentWeekDate> TournamentWeekDates { get; set; }
    public DbSet<TournamentFile> TournamentFiles { get; set; }
    public DbSet<TournamentMatchFile> TournamentMatchFiles { get; set; }
    public DbSet<TournamentMatchComment> TournamentMatchComments { get; set; }
    public DbSet<TournamentImageIndex> TournamentImageIndices { get; set; }

    // Pre-computed aggregate tables
    public DbSet<PlayerStatsMonthly> PlayerStatsMonthly { get; set; }
    public DbSet<PlayerServerStats> PlayerServerStats { get; set; }
    public DbSet<PlayerMapStats> PlayerMapStats { get; set; }
    public DbSet<PlayerBestScore> PlayerBestScores { get; set; }
    public DbSet<ServerOnlineCount> ServerOnlineCounts { get; set; }
    public DbSet<ServerHourlyPattern> ServerHourlyPatterns { get; set; }
    public DbSet<HourlyPlayerPrediction> HourlyPlayerPredictions { get; set; }
    public DbSet<HourlyActivityPattern> HourlyActivityPatterns { get; set; }
    public DbSet<MapGlobalAverage> MapGlobalAverages { get; set; }
    public DbSet<ServerMapStats> ServerMapStats { get; set; }
    public DbSet<MapServerHourlyPattern> MapServerHourlyPatterns { get; set; }
    public DbSet<PlayerAchievement> PlayerAchievements { get; set; }

    private static readonly InstantPattern InstantExtendedIsoPattern = InstantPattern.ExtendedIso;
    private static readonly LocalDateTimePattern LegacySqliteInstantPattern =
        LocalDateTimePattern.CreateWithInvariantCulture("yyyy-MM-dd HH:mm:ss.FFFFFFF");

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

        // Optimized index for player search EXISTS subquery
        // Used by GetAllPlayersWithPaging when filtering active players
        modelBuilder.Entity<PlayerSession>()
            .HasIndex(ps => new { ps.PlayerName, ps.IsActive })
            .HasDatabaseName("IX_PlayerSessions_PlayerName_IsActive");

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
                instant => FormatInstant(instant),
                str => ParseInstant(str));

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
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentTeam -> User (Leader)
        modelBuilder.Entity<TournamentTeam>()
            .HasOne(tt => tt.Leader)
            .WithMany()
            .HasForeignKey(tt => tt.LeaderUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);


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

        // Index on UserId for efficient user lookup
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasIndex(ttp => ttp.UserId);

        // Configure NodaTime Instant conversions for TournamentTeamPlayer
        modelBuilder.Entity<TournamentTeamPlayer>()
            .Property(ttp => ttp.RulesAcknowledgedAt)
            .HasConversion(
                instant => instant.HasValue ? FormatInstant(instant.Value) : null,
                str => str != null ? ParseInstant(str) : null);

        modelBuilder.Entity<TournamentTeamPlayer>()
            .Property(ttp => ttp.JoinedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentTeamPlayer -> User (optional)
        modelBuilder.Entity<TournamentTeamPlayer>()
            .HasOne(ttp => ttp.User)
            .WithMany()
            .HasForeignKey(ttp => ttp.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

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
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<TournamentMatch>()
            .Property(tm => tm.CreatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));


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

        // Index on MatchId + MapOrder (NOT unique to allow same map multiple times)
        modelBuilder.Entity<TournamentMatchMap>()
            .HasIndex(tmm => new { tmm.MatchId, tmm.MapOrder });

        // Configure TournamentMatchResult entity
        modelBuilder.Entity<TournamentMatchResult>()
            .HasKey(tmr => tmr.Id);

        modelBuilder.Entity<TournamentMatchResult>()
            .HasIndex(tmr => tmr.TournamentId);

        modelBuilder.Entity<TournamentMatchResult>()
            .HasIndex(tmr => new { tmr.TournamentId, tmr.Week });

        modelBuilder.Entity<TournamentMatchResult>()
            .HasIndex(tmr => tmr.MatchId);

        modelBuilder.Entity<TournamentMatchResult>()
            .HasIndex(tmr => tmr.RoundId);

        // Configure NodaTime Instant conversions for TournamentMatchResult
        modelBuilder.Entity<TournamentMatchResult>()
            .Property(tmr => tmr.CreatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<TournamentMatchResult>()
            .Property(tmr => tmr.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentMatchResult -> Tournament
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Tournament)
            .WithMany()
            .HasForeignKey(tmr => tmr.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentMatchResult -> TournamentMatch
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Match)
            .WithMany()
            .HasForeignKey(tmr => tmr.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentMatchResult -> TournamentMatchMap
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Map)
            .WithMany(tmm => tmm.MatchResults)
            .HasForeignKey(tmr => tmr.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentMatchResult -> Round (optional)
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Round)
            .WithMany()
            .HasForeignKey(tmr => tmr.RoundId)
            .HasPrincipalKey(r => r.RoundId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Configure relationship: TournamentMatchResult -> Team1
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Team1)
            .WithMany()
            .HasForeignKey(tmr => tmr.Team1Id)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: TournamentMatchResult -> Team2
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.Team2)
            .WithMany()
            .HasForeignKey(tmr => tmr.Team2Id)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship: TournamentMatchResult -> WinningTeam
        modelBuilder.Entity<TournamentMatchResult>()
            .HasOne(tmr => tmr.WinningTeam)
            .WithMany()
            .HasForeignKey(tmr => tmr.WinningTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure TournamentTeamRanking entity
        modelBuilder.Entity<TournamentTeamRanking>()
            .HasKey(ttr => ttr.Id);

        modelBuilder.Entity<TournamentTeamRanking>()
            .HasIndex(ttr => ttr.TournamentId);

        modelBuilder.Entity<TournamentTeamRanking>()
            .HasIndex(ttr => new { ttr.TournamentId, ttr.Week });

        // Composite unique index on (TournamentId, TeamId, Week)
        modelBuilder.Entity<TournamentTeamRanking>()
            .HasIndex(ttr => new { ttr.TournamentId, ttr.TeamId, ttr.Week })
            .IsUnique();

        // Configure NodaTime Instant conversion for TournamentTeamRanking
        modelBuilder.Entity<TournamentTeamRanking>()
            .Property(ttr => ttr.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentTeamRanking -> Tournament
        modelBuilder.Entity<TournamentTeamRanking>()
            .HasOne(ttr => ttr.Tournament)
            .WithMany()
            .HasForeignKey(ttr => ttr.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentTeamRanking -> TournamentTeam
        modelBuilder.Entity<TournamentTeamRanking>()
            .HasOne(ttr => ttr.Team)
            .WithMany()
            .HasForeignKey(ttr => ttr.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure NodaTime Instant conversions for TournamentFile
        modelBuilder.Entity<TournamentFile>()
            .Property(tf => tf.UploadedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentFile -> Tournament
        modelBuilder.Entity<TournamentFile>()
            .HasOne(tf => tf.Tournament)
            .WithMany(t => t.Files)
            .HasForeignKey(tf => tf.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure NodaTime LocalDate conversions for TournamentWeekDate
        modelBuilder.Entity<TournamentWeekDate>()
            .Property(twd => twd.StartDate)
            .HasConversion(
                date => NodaTime.Text.LocalDatePattern.Iso.Format(date),
                str => NodaTime.Text.LocalDatePattern.Iso.Parse(str).Value);

        modelBuilder.Entity<TournamentWeekDate>()
            .Property(twd => twd.EndDate)
            .HasConversion(
                date => NodaTime.Text.LocalDatePattern.Iso.Format(date),
                str => NodaTime.Text.LocalDatePattern.Iso.Parse(str).Value);

        // Configure relationship: TournamentWeekDate -> Tournament
        modelBuilder.Entity<TournamentWeekDate>()
            .HasOne(twd => twd.Tournament)
            .WithMany(t => t.WeekDates)
            .HasForeignKey(twd => twd.TournamentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure TournamentImageIndex entity
        modelBuilder.Entity<TournamentImageIndex>()
            .HasKey(tii => tii.Id);

        modelBuilder.Entity<TournamentImageIndex>()
            .HasIndex(tii => tii.FolderPath);

        modelBuilder.Entity<TournamentImageIndex>()
            .HasIndex(tii => new { tii.FolderPath, tii.FileName })
            .IsUnique();

        // Configure NodaTime Instant conversions for TournamentImageIndex
        modelBuilder.Entity<TournamentImageIndex>()
            .Property(tii => tii.IndexedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<TournamentImageIndex>()
            .Property(tii => tii.FileLastModified)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure NodaTime Instant conversion for TournamentMatchFile
        modelBuilder.Entity<TournamentMatchFile>()
            .Property(tmf => tmf.UploadedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentMatchFile -> TournamentMatch
        modelBuilder.Entity<TournamentMatchFile>()
            .HasOne(tmf => tmf.Match)
            .WithMany(tm => tm.Files)
            .HasForeignKey(tmf => tmf.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure NodaTime Instant conversions for TournamentMatchComment
        modelBuilder.Entity<TournamentMatchComment>()
            .Property(tmc => tmc.CreatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<TournamentMatchComment>()
            .Property(tmc => tmc.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure relationship: TournamentMatchComment -> TournamentMatch
        modelBuilder.Entity<TournamentMatchComment>()
            .HasOne(tmc => tmc.Match)
            .WithMany(tm => tm.Comments)
            .HasForeignKey(tmc => tmc.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure relationship: TournamentMatchComment -> User
        modelBuilder.Entity<TournamentMatchComment>()
            .HasOne(tmc => tmc.CreatedByUser)
            .WithMany()
            .HasForeignKey(tmc => tmc.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ============================================================
        // Pre-computed aggregate tables
        // ============================================================

        // Configure PlayerStatsMonthly entity (period-based aggregation)
        modelBuilder.Entity<PlayerStatsMonthly>()
            .HasKey(psm => new { psm.PlayerName, psm.Year, psm.Month });

        modelBuilder.Entity<PlayerStatsMonthly>()
            .HasIndex(psm => new { psm.Year, psm.Month });

        modelBuilder.Entity<PlayerStatsMonthly>()
            .Property(psm => psm.FirstRoundTime)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<PlayerStatsMonthly>()
            .Property(psm => psm.LastRoundTime)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<PlayerStatsMonthly>()
            .Property(psm => psm.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure PlayerServerStats entity (weekly aggregation for leaderboards)
        modelBuilder.Entity<PlayerServerStats>()
            .HasKey(pss => new { pss.PlayerName, pss.ServerGuid, pss.Year, pss.Week });

        modelBuilder.Entity<PlayerServerStats>()
            .HasIndex(pss => pss.ServerGuid);

        modelBuilder.Entity<PlayerServerStats>()
            .HasIndex(pss => new { pss.Year, pss.Week });

        modelBuilder.Entity<PlayerServerStats>()
            .HasIndex(pss => new { pss.ServerGuid, pss.Year, pss.Week });

        modelBuilder.Entity<PlayerServerStats>()
            .Property(pss => pss.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure PlayerMapStats entity (period-based aggregation)
        modelBuilder.Entity<PlayerMapStats>()
            .HasKey(pms => new { pms.PlayerName, pms.MapName, pms.ServerGuid, pms.Year, pms.Month });

        modelBuilder.Entity<PlayerMapStats>()
            .HasIndex(pms => pms.MapName);

        modelBuilder.Entity<PlayerMapStats>()
            .HasIndex(pms => new { pms.Year, pms.Month });

        // Optimizes queries that filter by ServerGuid and MapName (e.g., top players per map)
        modelBuilder.Entity<PlayerMapStats>()
            .HasIndex(pms => new { pms.ServerGuid, pms.MapName });

        modelBuilder.Entity<PlayerMapStats>()
            .Property(pms => pms.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure PlayerBestScore entity
        modelBuilder.Entity<PlayerBestScore>()
            .HasKey(pbs => new { pbs.PlayerName, pbs.Period, pbs.Rank });

        modelBuilder.Entity<PlayerBestScore>()
            .Property(pbs => pbs.RoundEndTime)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure ServerOnlineCount entity
        modelBuilder.Entity<ServerOnlineCount>()
            .HasKey(soc => new { soc.ServerGuid, soc.HourTimestamp });

        modelBuilder.Entity<ServerOnlineCount>()
            .HasIndex(soc => soc.HourTimestamp);

        modelBuilder.Entity<ServerOnlineCount>()
            .HasIndex(soc => new { soc.Game, soc.HourTimestamp });

        modelBuilder.Entity<ServerOnlineCount>()
            .Property(soc => soc.HourTimestamp)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure ServerHourlyPattern entity
        modelBuilder.Entity<ServerHourlyPattern>()
            .HasKey(shp => new { shp.ServerGuid, shp.DayOfWeek, shp.HourOfDay });

        modelBuilder.Entity<ServerHourlyPattern>()
            .Property(shp => shp.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure HourlyPlayerPrediction entity
        modelBuilder.Entity<HourlyPlayerPrediction>()
            .HasKey(hpp => new { hpp.Game, hpp.DayOfWeek, hpp.HourOfDay });

        modelBuilder.Entity<HourlyPlayerPrediction>()
            .Property(hpp => hpp.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure HourlyActivityPattern entity
        modelBuilder.Entity<HourlyActivityPattern>()
            .HasKey(hap => new { hap.Game, hap.DayOfWeek, hap.HourOfDay });

        modelBuilder.Entity<HourlyActivityPattern>()
            .Property(hap => hap.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure MapGlobalAverage entity
        modelBuilder.Entity<MapGlobalAverage>()
            .HasKey(mga => new { mga.MapName, mga.ServerGuid });

        modelBuilder.Entity<MapGlobalAverage>()
            .Property(mga => mga.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure ServerMapStats entity (monthly server map aggregation)
        modelBuilder.Entity<ServerMapStats>()
            .HasKey(sms => new { sms.ServerGuid, sms.MapName, sms.Year, sms.Month });

        modelBuilder.Entity<ServerMapStats>()
            .HasIndex(sms => sms.ServerGuid);

        modelBuilder.Entity<ServerMapStats>()
            .HasIndex(sms => new { sms.ServerGuid, sms.Year, sms.Month });

        modelBuilder.Entity<ServerMapStats>()
            .Property(sms => sms.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure MapServerHourlyPattern entity (map activity heatmap data per server)
        modelBuilder.Entity<MapServerHourlyPattern>()
            .HasKey(mhp => new { mhp.ServerGuid, mhp.MapName, mhp.Game, mhp.DayOfWeek, mhp.HourOfDay });

        modelBuilder.Entity<MapServerHourlyPattern>()
            .HasIndex(mhp => new { mhp.MapName, mhp.Game });

        modelBuilder.Entity<MapServerHourlyPattern>()
            .Property(mhp => mhp.UpdatedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        // Configure PlayerAchievement entity
        modelBuilder.Entity<PlayerAchievement>()
            .HasKey(pa => pa.Id);

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => new { pa.PlayerName, pa.AchievementId, pa.AchievedAt })
            .IsUnique();

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => new { pa.PlayerName, pa.AchievedAt });

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => pa.AchievementType);

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => pa.AchievementId);

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => pa.ServerGuid);

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => pa.MapName);

        modelBuilder.Entity<PlayerAchievement>()
            .HasIndex(pa => pa.AchievedAt);

        modelBuilder.Entity<PlayerAchievement>()
            .Property(pa => pa.AchievedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<PlayerAchievement>()
            .Property(pa => pa.ProcessedAt)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));

        modelBuilder.Entity<PlayerAchievement>()
            .Property(pa => pa.Version)
            .HasConversion(
                instant => FormatInstant(instant),
                str => ParseInstant(str));
    }

    private static string FormatInstant(Instant instant) => InstantExtendedIsoPattern.Format(instant);

    private static Instant ParseInstant(string value)
    {
        // Handle empty strings from database corruption - treat as epoch (1970-01-01)
        if (string.IsNullOrWhiteSpace(value))
        {
            return Instant.FromUnixTimeSeconds(0);
        }

        var extended = InstantExtendedIsoPattern.Parse(value);
        if (extended.Success)
        {
            return extended.Value;
        }

        var legacy = LegacySqliteInstantPattern.Parse(value);
        if (legacy.Success)
        {
            return legacy.Value.InZoneLeniently(DateTimeZone.Utc).ToInstant();
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTime))
        {
            return Instant.FromDateTimeUtc(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        throw new FormatException($"Unable to parse Instant value '{value}'");
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
    public int CurrentNumPlayers { get; set; } = 0; // Current number of players online
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

public class TournamentTheme
{
    public int Id { get; set; }
    public string? BackgroundColour { get; set; } // Hex color (e.g., #RRGGBB or #RRGGBBAA)
    public string? TextColour { get; set; } // Hex color
    public string? AccentColour { get; set; } // Hex color

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
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
    public byte[]? CommunityLogo { get; set; }
    public string? CommunityLogoContentType { get; set; }
    public string? Rules { get; set; } // Markdown content for tournament rules
    public string? RegistrationRules { get; set; } // Markdown content for tournament registration rules
    public string? ServerGuid { get; set; }

    // Tournament status and configuration
    public string Status { get; set; } = "draft"; // draft, registration, open, closed
    public string? GameMode { get; set; } // Conquest, CTF, TDM, Coop, etc.

    // Theme customisation
    public int? ThemeId { get; set; }

    // Community links
    public string? DiscordUrl { get; set; }
    public string? ForumUrl { get; set; }
    public string? YouTubeUrl { get; set; }
    public string? TwitchUrl { get; set; }

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;
    public Player OrganizerPlayer { get; set; } = null!;
    public GameServer? Server { get; set; }
    public TournamentTheme? Theme { get; set; }
    public List<TournamentTeam> TournamentTeams { get; set; } = [];
    public List<TournamentMatch> TournamentMatches { get; set; } = [];
    public List<TournamentWeekDate> WeekDates { get; set; } = [];
    public List<TournamentFile> Files { get; set; } = [];
}


public class TournamentTeam
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = ""; // Team name (usually clan tag)
    public string? Tag { get; set; } // Short clan tag e.g. "[GGE]"
    public int? LeaderUserId { get; set; } // User who created/leads team
    public Instant CreatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public User? Leader { get; set; }
    public List<TournamentTeamPlayer> TeamPlayers { get; set; } = [];
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

    // Optional week/campaign identifier for ranking purposes
    public string? Week { get; set; }

    public Instant CreatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public TournamentTeam Team1 { get; set; } = null!;
    public TournamentTeam Team2 { get; set; } = null!;
    public GameServer? Server { get; set; }
    public List<TournamentMatchMap> Maps { get; set; } = [];
    public List<TournamentMatchFile> Files { get; set; } = [];
    public List<TournamentMatchComment> Comments { get; set; } = [];
}

public class TournamentMatchMap
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string MapName { get; set; } = "";
    public int MapOrder { get; set; } // Sequence order for maps in the match (0-based). Note: MapName is NOT unique - same map can appear multiple times with different MapOrder values

    // Optional: Team that chose/assigned this map
    public int? TeamId { get; set; }

    // Optional: Reference to a tournament image for this map, e.g., "golden-gun/map1.png"
    public string? ImagePath { get; set; }

    // Navigation properties
    public TournamentMatch Match { get; set; } = null!;
    public TournamentTeam? Team { get; set; }
    public ICollection<TournamentMatchResult> MatchResults { get; set; } = new List<TournamentMatchResult>();
}

public class TournamentTeamPlayer
{
    public int Id { get; set; }
    public int TournamentTeamId { get; set; }
    public string PlayerName { get; set; } = ""; // References Player.Name

    // Self-service registration fields
    public int? UserId { get; set; } // Link to User if they joined themselves
    public bool IsTeamLeader { get; set; } = false;
    public bool RulesAcknowledged { get; set; } = false;
    public Instant? RulesAcknowledgedAt { get; set; }
    public Instant JoinedAt { get; set; }

    // Navigation properties
    public TournamentTeam TournamentTeam { get; set; } = null!;
    public Player Player { get; set; } = null!;
    public User? User { get; set; }
}

public class TournamentMatchResult
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int MatchId { get; set; }
    public int MapId { get; set; }
    public string? RoundId { get; set; } // Now nullable - round is optional for manual entries
    public string? Week { get; set; } // Denormalized from TournamentMatch.Week

    // Tournament teams mapped to this result (can come from round or manual entry)
    public int? Team1Id { get; set; }
    public int? Team2Id { get; set; }
    public int? WinningTeamId { get; set; }

    // Ticket information (from Round or manually entered)
    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }

    // Timestamps
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public TournamentMatch Match { get; set; } = null!;
    public TournamentMatchMap Map { get; set; } = null!;
    public Round? Round { get; set; } // Now nullable - can exist without a linked round
    public TournamentTeam? Team1 { get; set; }
    public TournamentTeam? Team2 { get; set; }
    public TournamentTeam? WinningTeam { get; set; }
}

public class TournamentTeamRanking
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public int TeamId { get; set; }
    public string? Week { get; set; } // NULL = cumulative across all weeks

    // Aggregated statistics
    public int RoundsWon { get; set; }
    public int RoundsTied { get; set; }
    public int RoundsLost { get; set; }
    public int TicketDifferential { get; set; }

    // Match-level statistics
    public int MatchesPlayed { get; set; }
    public int Victories { get; set; }
    public int Ties { get; set; }
    public int Losses { get; set; }

    // Ticket statistics
    public int TicketsFor { get; set; }
    public int TicketsAgainst { get; set; }

    // Points (primary ranking metric = RoundsWon)
    public int Points { get; set; }

    // Calculated position in standings
    public int Rank { get; set; }

    // Timestamp
    public Instant UpdatedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
    public TournamentTeam Team { get; set; } = null!;
}

public class TournamentWeekDate
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string? Week { get; set; } // "Week 1", "Week 2", or null for unweekly
    public LocalDate StartDate { get; set; }
    public LocalDate EndDate { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
}

public class TournamentFile
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = ""; // Display name (e.g., "Map Pack v1.0")
    public string Url { get; set; } = ""; // External URL to file
    public string? Category { get; set; } // Optional: 'map', 'mod', 'program', etc.
    public Instant UploadedAt { get; set; }

    // Navigation properties
    public Tournament Tournament { get; set; } = null!;
}

public class TournamentMatchFile
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string Name { get; set; } = ""; // Display name (e.g., "Map Pack v1.0")
    public string Url { get; set; } = ""; // External URL to file
    public string? Tags { get; set; } // Comma-separated tags (e.g., "recording,gameplay")
    public Instant UploadedAt { get; set; }

    // Navigation properties
    public TournamentMatch Match { get; set; } = null!;
}

public class TournamentMatchComment
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string Content { get; set; } = ""; // Comment text
    public int CreatedByUserId { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    // Navigation properties
    public TournamentMatch Match { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
