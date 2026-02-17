# Neo4j Migrations

## Overview

Neo4j schema changes are managed through **migration files** similar to EF Core migrations. Migrations run automatically on application startup and are tracked in the Neo4j database.

## How It Works

1. **Migration Files**: Cypher scripts in `api/PlayerRelationships/Migrations/`
2. **Embedded Resources**: Files are compiled into the assembly
3. **Automatic Execution**: Migrations run on app startup
4. **Tracking**: Applied migrations stored in `:MigrationTracker` node
5. **Idempotent**: Safe to run multiple times (already-applied migrations are skipped)

## Migration File Format

**Naming Convention:**
```
{Number}_{Description}.cypher
```

Examples:
- `001_InitialSchema.cypher`
- `002_AddPlayerIndexes.cypher`
- `003_AddCommunityNodes.cypher`

**File Structure:**
```cypher
// Migration: {Number}_{Description}
// Description: What this migration does
// Date: YYYY-MM-DD

// Your Cypher statements here
CREATE INDEX index_name IF NOT EXISTS
FOR (n:NodeLabel) ON (n.property);

CREATE CONSTRAINT constraint_name IF NOT EXISTS
FOR (n:NodeLabel) REQUIRE n.property IS UNIQUE;

// Multiple statements separated by semicolons
MERGE (n:NewNode {id: 'test'})
RETURN n;
```

**Important:**
- Use `IF NOT EXISTS` for indexes and constraints
- Each statement must end with semicolon (`;`)
- Comments use `//` syntax
- Blank lines are ignored

## Creating a New Migration

### 1. Create Migration File

```bash
# Create new migration file
touch api/PlayerRelationships/Migrations/002_AddCommunityNodes.cypher
```

### 2. Write Cypher Statements

```cypher
// Migration: 002_AddCommunityNodes
// Description: Add Community nodes for clan/group tracking
// Date: 2026-02-18

// Create Community node
CREATE CONSTRAINT community_id_unique IF NOT EXISTS
FOR (c:Community) REQUIRE c.id IS UNIQUE;

CREATE INDEX community_name IF NOT EXISTS
FOR (c:Community) ON (c.name);

// Create relationship index
CREATE INDEX member_of_joined_at IF NOT EXISTS
FOR ()-[r:MEMBER_OF]-() ON (r.joinedAt);
```

### 3. Build and Run

Migrations are automatically embedded as resources when you build:

```bash
cd api
dotnet build
dotnet run
```

On startup, you'll see:
```
Checking for pending Neo4j migrations...
Found 1 previously applied migrations
Found 2 total migration files
Found 1 pending migrations. Applying...
Applying migration: 002_AddCommunityNodes.cypher
Successfully applied migration 002_AddCommunityNodes.cypher in 45ms
All migrations applied successfully
```

## Checking Migration Status

### Via API

```bash
# Get migration status
curl -X GET http://localhost:5555/stats/admin/data/neo4j/migrations \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Response:
{
  "appliedCount": 2,
  "pendingCount": 0,
  "isUpToDate": true,
  "appliedMigrations": [
    "001_InitialSchema.cypher",
    "002_AddCommunityNodes.cypher"
  ],
  "pendingMigrations": []
}
```

### Via Neo4j Browser

```cypher
// Check migration tracker
MATCH (tracker:MigrationTracker {id: 'singleton'})
RETURN tracker.migrations AS appliedMigrations,
       tracker.lastMigration AS lastMigration,
       tracker.lastMigrationAt AS lastMigrationAt;
```

## Manual Migration Execution

If migrations fail on startup or you need to re-run:

### Via API

```bash
curl -X POST http://localhost:5555/stats/admin/data/neo4j/migrations/run \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq
```

### Via Code

```csharp
var migrationService = serviceProvider.GetRequiredService<Neo4jMigrationService>();
await migrationService.MigrateAsync();
```

## Migration Tracking

Migrations are tracked in a singleton node:

```cypher
(:MigrationTracker {
  id: 'singleton',
  createdAt: datetime(),
  migrations: ['001_InitialSchema.cypher', '002_...'],
  lastMigration: '002_AddCommunityNodes.cypher',
  lastMigrationAt: datetime()
})
```

## Best Practices

### ✅ DO

- Use `IF NOT EXISTS` for indexes and constraints
- Include descriptive comments in migration files
- Test migrations in development before deploying
- Keep migrations small and focused
- Use semantic numbering (001, 002, 003...)
- Make migrations idempotent when possible

### ❌ DON'T

- Modify existing migration files after they're deployed
- Delete migration files (breaks tracking)
- Use database-specific syntax (stick to standard Cypher)
- Create circular dependencies between migrations
- Forget semicolons at the end of statements

## Rollback Strategy

**Neo4j doesn't support automatic rollback.** If a migration fails:

1. **Fix the migration file** and re-run
2. **Manually undo changes** via Neo4j Browser
3. **Reset tracking** if needed (advanced):

```cypher
// Remove failed migration from tracker
MATCH (tracker:MigrationTracker {id: 'singleton'})
SET tracker.migrations = [m IN tracker.migrations WHERE m <> '002_BadMigration.cypher']
RETURN tracker;
```

4. **Drop and recreate** (nuclear option):
```cypher
// WARNING: Deletes all data!
MATCH (n) DETACH DELETE n;

// Remove migration tracker
MATCH (tracker:MigrationTracker) DELETE tracker;

// Restart app to re-run all migrations
```

## Migration Examples

### Add New Index

```cypher
// Migration: 003_AddPlayerActivityIndex
// Description: Index for player activity queries
// Date: 2026-02-18

CREATE INDEX player_total_sessions IF NOT EXISTS
FOR (p:Player) ON (p.totalSessions);
```

### Add New Node Type

```cypher
// Migration: 004_AddTeamNodes
// Description: Add Team nodes for clan/team tracking
// Date: 2026-02-19

CREATE CONSTRAINT team_id_unique IF NOT EXISTS
FOR (t:Team) REQUIRE t.id IS UNIQUE;

CREATE INDEX team_name IF NOT EXISTS
FOR (t:Team) ON (t.name);

CREATE INDEX team_created_at IF NOT EXISTS
FOR (t:Team) ON (t.createdAt);
```

### Add Relationship Type

```cypher
// Migration: 005_AddCaptainRelationship
// Description: Add CAPTAIN_OF relationship for team leadership
// Date: 2026-02-20

// Relationship indexes
CREATE INDEX captain_of_since IF NOT EXISTS
FOR ()-[r:CAPTAIN_OF]-() ON (r.since);

// No need to create the relationship type explicitly
// It's created when first used in a MERGE/CREATE statement
```

### Data Migration

```cypher
// Migration: 006_PopulatePlayerScores
// Description: Add totalScore property to existing players
// Date: 2026-02-21

// Update existing players (careful with large datasets!)
MATCH (p:Player)
WHERE NOT EXISTS(p.totalScore)
SET p.totalScore = 0;
```

## Troubleshooting

### Migration fails on startup

**Symptoms:** App crashes or logs "Failed to run Neo4j migrations"

**Solutions:**
1. Check Neo4j is running: `docker ps | grep neo4j`
2. Check connection settings in `appsettings.json`
3. Check migration file syntax (valid Cypher)
4. Run migrations manually via admin endpoint
5. Check Neo4j logs: `docker logs bf1942-neo4j`

### "Migration already applied" but schema is wrong

**Cause:** Migration file was modified after being applied

**Solution:**
```cypher
// Remove from tracking
MATCH (tracker:MigrationTracker {id: 'singleton'})
SET tracker.migrations = [m IN tracker.migrations WHERE m <> 'bad_migration.cypher']

// Fix the migration file
// Restart app to re-run
```

### Migrations run but indexes don't appear

**Check index status:**
```cypher
SHOW INDEXES;
```

If status is "FAILED", there may be a constraint violation or syntax error.

### Can't connect to Neo4j

**Ensure Neo4j is enabled in config:**
```json
{
  "Neo4j": {
    "Uri": "bolt://localhost:7687"
  }
}
```

If `Uri` is empty, migrations are skipped.

## Production Deployment

### Pre-deployment Checklist

- [ ] All migrations tested in dev/staging
- [ ] Migration files embedded in build
- [ ] Neo4j instance accessible from API
- [ ] Credentials configured (secrets, not plaintext)
- [ ] Backup taken before deployment

### Deployment Process

1. **Deploy API** (with new migration files)
2. **Migrations run automatically** on first pod startup
3. **Verify migration status** via admin endpoint
4. **Check logs** for "All migrations applied successfully"
5. **Test queries** to verify schema changes

### Zero-Downtime Migrations

For large schema changes:
1. **Add new fields/indexes** in migration (old code still works)
2. **Deploy API** with new code using new fields
3. **Remove old fields** in later migration after rollout complete

## CI/CD Integration

### GitHub Actions Example

```yaml
- name: Build API
  run: |
    cd api
    dotnet build --configuration Release
    
- name: Run Tests
  run: |
    cd api
    dotnet test
    
- name: Check Migrations Embedded
  run: |
    # Verify migration files are in the assembly
    dotnet build
    strings bin/Release/net10.0/api.dll | grep -q "001_InitialSchema.cypher"
```

### Kubernetes Deployment

Migrations run automatically when pods start. No special init containers needed!

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: api
        image: your-api:latest
        env:
        - name: Neo4j__Uri
          value: "bolt://neo4j-service:7687"
        # Migrations run automatically on startup
```

## Migration History

| Migration | Description | Date |
|-----------|-------------|------|
| 001_InitialSchema | Initial indexes and constraints | 2026-02-17 |

(Add new migrations to this table as they're created)

## Further Reading

- [Neo4j Constraints](https://neo4j.com/docs/cypher-manual/current/constraints/)
- [Neo4j Indexes](https://neo4j.com/docs/cypher-manual/current/indexes/)
- [Cypher Syntax](https://neo4j.com/docs/cypher-manual/current/syntax/)
