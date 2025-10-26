# Discord OAuth Backend Setup Guide

This guide explains how to configure the backend to support Discord OAuth authentication alongside Google OAuth.

## What Was Implemented

The backend now supports both Google and Discord OAuth authentication:

1. **Discord Auth Service** (`Services/OAuth/DiscordAuthService.cs`)
   - Exchanges Discord authorization codes for access tokens
   - Fetches user profile from Discord API
   - Implements rate limiting (20 attempts per IP per hour)
   - Validates email verification

2. **Updated Auth Controller** (`Controllers/AuthController.cs`)
   - Login endpoint now accepts both `googleIdToken` or `discordCode`+`redirectUri`
   - Creates/updates users based on Discord email
   - Issues the same JWT tokens regardless of OAuth provider

3. **Request Format**

   For Discord authentication:
   ```json
   POST /stats/auth/login
   {
     "discordCode": "authorization_code_from_discord",
     "redirectUri": "http://localhost:5173/auth/discord/callback"
   }
   ```

   For Google authentication (still supported):
   ```json
   POST /stats/auth/login
   {
     "googleIdToken": "google_id_token"
   }
   ```

4. **Response Format** (same for both providers):
   ```json
   {
     "user": {
       "id": 123,
       "email": "user@example.com",
       "name": "Username"
     },
     "accessToken": "jwt_access_token",
     "expiresAt": "2024-01-01T00:00:00Z"
   }
   ```

## Configuration

### Local Development

Update `Properties/launchSettings.json` with your Discord OAuth credentials:

```json
"environmentVariables": {
  "DiscordOAuth__ClientId": "YOUR_DISCORD_CLIENT_ID",
  "DiscordOAuth__ClientSecret": "YOUR_DISCORD_CLIENT_SECRET"
}
```

**Important:** Replace `YOUR_DISCORD_CLIENT_ID` and `YOUR_DISCORD_CLIENT_SECRET` with your actual Discord application credentials from the [Discord Developer Portal](https://discord.com/developers/applications).

### Production Deployment

For production, Discord OAuth credentials are stored in Kubernetes secrets.

#### Create or Update the Kubernetes Secret

Add the Discord OAuth credentials to your `bf42-stats-secrets` secret:

```bash
kubectl create secret generic bf42-stats-secrets \
  --namespace=bf42-stats \
  --from-literal=discord-client-id='YOUR_DISCORD_CLIENT_ID' \
  --from-literal=discord-client-secret='YOUR_DISCORD_CLIENT_SECRET' \
  --from-literal=jwt-private-key='YOUR_JWT_PRIVATE_KEY' \
  --from-literal=refresh-token-secret='YOUR_REFRESH_TOKEN_SECRET' \
  --dry-run=client -o yaml | kubectl apply -f -
```

Or if the secret already exists, update it:

```bash
# Update Discord Client ID
kubectl patch secret bf42-stats-secrets \
  --namespace=bf42-stats \
  --type='json' \
  -p='[{"op": "add", "path": "/data/discord-client-id", "value": "'$(echo -n 'YOUR_DISCORD_CLIENT_ID' | base64)'"}]'

# Update Discord Client Secret
kubectl patch secret bf42-stats-secrets \
  --namespace=bf42-stats \
  --type='json' \
  -p='[{"op": "add", "path": "/data/discord-client-secret", "value": "'$(echo -n 'YOUR_DISCORD_CLIENT_SECRET' | base64)'"}]'
```

The deployment configuration (`deploy/app/deployment.yaml`) has been updated to reference these secrets:

```yaml
- name: DiscordOAuth__ClientId
  valueFrom:
    secretKeyRef:
      name: bf42-stats-secrets
      key: discord-client-id
- name: DiscordOAuth__ClientSecret
  valueFrom:
    secretKeyRef:
      name: bf42-stats-secrets
      key: discord-client-secret
```

## Discord Application Setup

1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Create a new application or select an existing one
3. Go to OAuth2 settings
4. Add redirect URIs:
   - Development: `http://localhost:5173/auth/discord/callback`
   - Production: `https://1942.munyard.dev/auth/discord/callback`
5. Copy the **Client ID** and **Client Secret**
6. Configure the backend with these credentials as described above

## Discord OAuth Scopes

The implementation requests the following scopes from Discord:
- `identify`: Access to user's ID, username, and avatar
- `email`: Access to user's email address (required for authentication)

## Security Features

1. **Rate Limiting**: Maximum 20 authentication attempts per IP per hour
2. **Email Verification**: Only users with verified Discord emails can authenticate
3. **Token Exchange**: Authorization codes are exchanged server-side, keeping the client secret secure
4. **IP Address Logging**: Failed authentication attempts are logged with IP addresses for security monitoring

## Testing

### Local Testing

1. Start the backend: `dotnet run --project junie-des-1942stats`
2. The API will be available at `http://localhost:9222`
3. Use the frontend or a tool like Postman to test the `/stats/auth/login` endpoint

### Example Request with curl

```bash
curl -X POST http://localhost:9222/stats/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "discordCode": "authorization_code_from_discord",
    "redirectUri": "http://localhost:5173/auth/discord/callback"
  }'
```

## Troubleshooting

### "DiscordOAuth:ClientId not configured" Error

- **Cause**: The backend couldn't find the Discord client ID in configuration
- **Solution**: Ensure `DiscordOAuth__ClientId` is set in environment variables or launchSettings.json

### "DiscordOAuth:ClientSecret not configured" Error

- **Cause**: The backend couldn't find the Discord client secret in configuration
- **Solution**: Ensure `DiscordOAuth__ClientSecret` is set in environment variables or secrets

### "Failed to exchange Discord authorization code" Error

- **Cause**: The authorization code or redirect URI is invalid
- **Solution**: 
  - Ensure the redirect URI in the request matches the one registered in Discord Developer Portal
  - Authorization codes are single-use and expire quickly, ensure you're using a fresh code
  - Check that the client ID and secret are correct

### "Discord account does not have a verified email" Error

- **Cause**: The Discord account doesn't have a verified email address
- **Solution**: Ask the user to verify their email in Discord settings

### Rate Limiting Errors

- **Cause**: Too many failed authentication attempts from the same IP
- **Solution**: Wait an hour or clear the Redis cache for the affected IP

## Google OAuth Still Supported

Google OAuth remains fully functional. The backend supports both authentication methods simultaneously. Users can choose which provider to use when logging in.

## Migration Notes

- Existing users authenticated via Google OAuth will continue to work
- Users can log in with Discord if their Discord email matches their Google account email
- The system matches users by email address, regardless of OAuth provider

