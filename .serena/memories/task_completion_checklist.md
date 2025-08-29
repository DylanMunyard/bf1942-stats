# Task Completion Checklist

When completing coding tasks in this project:

## Code Quality
1. **Build**: Run `dotnet build` to ensure compilation
2. **Format**: Run `dotnet format` to apply consistent formatting
3. **Review**: Check for proper error handling and logging
4. **Async**: Ensure I/O operations use async/await patterns

## Testing
- No specific test framework found in the project
- Manual testing through Swagger UI available in development
- API endpoints can be tested via HTTP requests

## Configuration
- Environment variables properly configured
- Database connection strings set
- ClickHouse read/write URLs configured
- Redis connection working

## Logging and Monitoring
- Ensure appropriate log levels used
- Add structured logging for new features
- Consider OpenTelemetry tracing for performance monitoring

## Security
- Never commit secrets or keys
- Use JWT authentication where required
- Validate user inputs properly

## Documentation
- Update API documentation if endpoints change
- Consider updating README.md for significant changes (only if requested)