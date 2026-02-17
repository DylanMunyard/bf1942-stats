// Migration: 001_InitialSchema
// Description: Create initial constraints and indexes for player relationships
// Date: 2026-02-17

// ============================================================
// Constraints (unique identifiers)
// ============================================================

CREATE CONSTRAINT player_name_unique IF NOT EXISTS
FOR (p:Player) REQUIRE p.name IS UNIQUE;

CREATE CONSTRAINT server_guid_unique IF NOT EXISTS
FOR (s:Server) REQUIRE s.guid IS UNIQUE;

// ============================================================
// Indexes for performance
// ============================================================

// Player lookups
CREATE INDEX player_name_index IF NOT EXISTS
FOR (p:Player) ON (p.name);

CREATE INDEX player_last_seen IF NOT EXISTS
FOR (p:Player) ON (p.lastSeen);

CREATE INDEX player_first_seen IF NOT EXISTS
FOR (p:Player) ON (p.firstSeen);

// Server lookups
CREATE INDEX server_game IF NOT EXISTS
FOR (s:Server) ON (s.game);

// Relationship indexes (critical for query performance!)
CREATE INDEX played_with_session_count IF NOT EXISTS
FOR ()-[r:PLAYED_WITH]-() ON (r.sessionCount);

CREATE INDEX played_with_last_played IF NOT EXISTS
FOR ()-[r:PLAYED_WITH]-() ON (r.lastPlayedTogether);

CREATE INDEX played_with_first_played IF NOT EXISTS
FOR ()-[r:PLAYED_WITH]-() ON (r.firstPlayedTogether);

CREATE INDEX plays_on_last_played IF NOT EXISTS
FOR ()-[r:PLAYS_ON]-() ON (r.lastPlayed);
