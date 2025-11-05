## Folder Structure Convention

We use a **feature-first organization** pattern rather than type-based organization (no `/Services`, `/Controllers`, `/Models` at the root level).

### Pattern

**Feature folders** are organized by domain/feature with the following structure:
```
/FeatureName/
  ├── FeatureController.cs          (API endpoints, if applicable)
  ├── FeatureService.cs             (Business logic)
  ├── IFeatureService.cs            (Interface)
  ├── Models/
  │   ├── FeatureModel.cs
  │   └── ...
  └── [optional subfolders for cross-cutting concerns within the feature]
```

### Key Rules

1. **Feature folders are named by domain** (e.g., `Bflist`, `Gamification`, `PlayerStats`, `ServerStats`, `Caching`, `PlayerTracking`)
2. **Controllers go directly in the feature folder** - NOT in `/Controllers`
3. **Services and service implementations go directly in the feature folder** - NOT in `/Services`
4. **Models go in a `/Models` subfolder** within the feature
   - **Each DTO class should be in its own file** (e.g., `UserDto.cs`, `LoginRequest.cs`)
   - **Exception**: Small related value objects can be grouped if they form a cohesive domain concept
5. **Avoid root-level type-based folders** like `/Services`, `/Controllers`, `/Models`
6. **Shared/cross-cutting concerns** like `Telemetry`, `Caching`, `ClickHouse` can be in their own feature folders
7. **Migrations and build artifacts** stay in their special folders (`/Migrations`, `/bin`, `/obj`, etc.)

### Example Structure

```
junie-des-1942stats/
├── Bflist/                         # BFList API integration feature
│   ├── BfListApiService.cs
│   ├── ServerFilteringConfig.cs
│   ├── LiveServersController.cs
│   ├── PlayerInfo.cs
│   └── Models/
│       ├── Bf1942ServerInfo.cs
│       ├── BfvietnamServerInfo.cs
│       └── ...
├── Gamification/                   # Tournaments and achievements feature
│   ├── GamificationService.cs
│   ├── GamificationController.cs
│   ├── AdminTournamentController.cs
│   ├── Services/
│   │   ├── TeamRankingCalculator.cs
│   │   ├── TournamentMatchResultService.cs
│   │   └── ...
│   └── Models/
│       ├── Achievement.cs
│       ├── KillStreak.cs
│       ├── BadgeDefinition.cs
│       └── ...
├── PlayerStats/                    # Player statistics feature
│   ├── PlayerStatsService.cs
│   ├── PlayersController.cs
│   └── Models/
│       ├── PlayerBasicInfo.cs
│       ├── PlayerFilters.cs
│       ├── ServerInfo.cs
│       └── ...
├── Auth/                           # Authentication feature
│   ├── TokenService.cs
│   ├── RefreshTokenService.cs
│   ├── DiscordAuthService.cs
│   ├── AuthController.cs
│   └── Models/
│       └── ...
├── Telemetry/                      # Cross-cutting concern
├── Caching/                        # Cross-cutting concern
├── Migrations/                     # Database migrations (special folder)
└── ...
```

### Benefits

- **Fast navigation**: Feature name matches folder name
- **Cohesion**: Related code lives together
- **Discoverability**: Easy to find all code related to a feature
- **Scalability**: Features can grow independently with their own services, controllers, models
- **DTO Organization**: Individual DTO files improve file discoverability and keep file sizes manageable

---

## Conventions

- When we document our decisions or iterate on a design, we store the outcomes / tasklist / progress in a markdown file in `features/<feature-name>` where feature name is a brief descriptive name of the feature separated by hyphens
- Use the latest C# language features, e.g. use collection expressions Tiers = ["bronze"] instead of Tiers = new[] { "bronze" }, and other features like range expressions and pattern matching.