using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.PlayerTracking;

public class PlayerTrackerDbContext : DbContext
{
    public DbSet<Player> Players { get; set; }
    public DbSet<GameServer> Servers { get; set; }
    public DbSet<PlayerSession> PlayerSessions { get; set; }
    public DbSet<PlayerObservation> PlayerObservations { get; set; }
    public DbSet<ServerPlayerRanking> ServerPlayerRankings { get; set; }

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
            
        // Configure PlayerObservation entity
        modelBuilder.Entity<PlayerObservation>()
            .HasKey(po => po.ObservationId);

        modelBuilder.Entity<PlayerObservation>()
            .HasIndex(po => po.SessionId);

        // Configure ServerPlayerRanking entity
        modelBuilder.Entity<ServerPlayerRanking>()
            .HasKey(r => r.Id);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasIndex(r => new { r.ServerGuid, r.PlayerName })
            .IsUnique();

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasIndex(r => new { r.ServerGuid, r.Rank });
            
        // Configure relationships
        modelBuilder.Entity<PlayerSession>()
            .HasOne(ps => ps.Player)
            .WithMany(p => p.Sessions)
            .HasForeignKey(ps => ps.PlayerName);
            
        modelBuilder.Entity<PlayerSession>()
            .HasOne(ps => ps.Server)
            .WithMany(s => s.Sessions)
            .HasForeignKey(ps => ps.ServerGuid);
            
        modelBuilder.Entity<PlayerObservation>()
            .HasOne(po => po.Session)
            .WithMany(ps => ps.Observations)
            .HasForeignKey(po => po.SessionId);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasOne<Player>()
            .WithMany()
            .HasForeignKey(r => r.PlayerName);

        modelBuilder.Entity<ServerPlayerRanking>()
            .HasOne<GameServer>()
            .WithMany()
            .HasForeignKey(r => r.ServerGuid);
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
    public List<PlayerSession> Sessions { get; set; } = new();
}

public class GameServer
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public string GameId { get; set; } = "";

    // Navigation property
    public List<PlayerSession> Sessions { get; set; } = new();
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
    
    // Navigation properties
    public Player Player { get; set; } = null!;
    public GameServer Server { get; set; } = null!;
    public List<PlayerObservation> Observations { get; set; } = new();
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
    public int HighestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastUpdated { get; set; }
    
    // Navigation properties
    public GameServer Server { get; set; } = null!;
    public Player Player { get; set; } = null!;
}