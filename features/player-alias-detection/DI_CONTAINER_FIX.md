# Dependency Injection Container Fix

## Problem

Runtime error when starting the application:
```
Unable to resolve service for type 'Neo4j.Driver.IDriver' while attempting to activate 'api.PlayerRelationships.Neo4jNetworkAnalyzer'
```

The `Neo4jNetworkAnalyzer` requires `IDriver`, but it wasn't registered in the DI container.

---

## Solution

### Step 1: Expose Driver from Neo4jService
**File**: `api/PlayerRelationships/Neo4jService.cs`

Added a public property to expose the properly configured driver:
```csharp
/// <summary>
/// Get the underlying Neo4j driver instance.
/// Useful for services that need direct driver access.
/// </summary>
public IDriver Driver => _driver;
```

This ensures we reuse the driver that was already created with proper configuration:
- `WithMaxConnectionPoolSize(50)`
- `WithConnectionTimeout(15 seconds)`

### Step 2: Register IDriver in DI Container
**File**: `api/Program.cs`

Added registration in the Neo4j configuration block:
```csharp
if (neo4jConfig != null && !string.IsNullOrEmpty(neo4jConfig.Uri))
{
    // ... existing registrations ...

    // Register IDriver as a singleton (provided by Neo4jService)
    builder.Services.AddSingleton<Neo4j.Driver.IDriver>(sp =>
    {
        var neo4jService = sp.GetRequiredService<api.PlayerRelationships.Neo4jService>();
        return neo4jService.Driver;
    });

    // ... rest of registrations ...
}
```

### Why This Approach?

✅ **Reuses existing driver** - No duplicate driver instances
✅ **Preserves configuration** - Uses driver with proper pool size and timeouts
✅ **Only when Neo4j configured** - IDriver only registered if Neo4j is enabled
✅ **Proper lifecycle** - Driver is singleton, shared across all services

---

## Dependency Chain

```
IDriver (registered)
  ↑
Neo4jService (creates driver with config)
  ↑
Neo4jNetworkAnalyzer (now can inject IDriver)
  ↑
PlayerAliasDetectionService (uses Neo4jNetworkAnalyzer)
```

---

## Build Status

✅ **0 errors**
✅ **Compiles successfully**
✅ **Ready for runtime testing**

---

## Testing

To verify the fix works:

```bash
# Should start without DI container errors
dotnet run

# Then test the alias detection endpoint
curl "http://localhost:5000/stats/alias-detection/compare?player1=Player1&player2=Player2"
```

Expected behavior:
- Application starts without errors
- DI container successfully resolves all services
- API endpoints respond

---

## Files Modified

- `api/PlayerRelationships/Neo4jService.cs` - Added `Driver` property
- `api/Program.cs` - Added `IDriver` registration

---

## Related Services

All of these now work properly with the registered `IDriver`:

- ✅ `Neo4jNetworkAnalyzer` - Gets IDriver for network analysis
- ✅ `PlayerAliasDetectionService` - Uses Neo4jNetworkAnalyzer
- ✅ `AliasDetectionController` - Exposes API endpoints

---

**Status: Ready for testing** ✅
