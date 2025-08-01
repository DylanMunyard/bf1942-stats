## Development Guidelines

- Any time you add a new achievement (e.g. add a new constant in @Gamification/Services/BadgeDefinitionsService.cs) please update @achievements.md 

## Security Guidelines

- ClickHouse querying should preference ADO.NET instead of Http queries sent using raw HttpClient with escaped parmeters. This will prevent the application from SQL injection vulnerabilities.