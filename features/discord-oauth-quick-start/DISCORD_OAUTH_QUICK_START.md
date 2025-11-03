# Discord OAuth - Quick Start

## What's Been Implemented ✅

The backend now supports Discord OAuth authentication alongside Google OAuth. Both authentication methods work seamlessly.

## Quick Setup for Local Development

### 1. Get Your Discord Credentials

1. Go to https://discord.com/developers/applications
2. Create a new application
3. Go to OAuth2 → General
4. Copy your **Client ID**
5. Generate and copy your **Client Secret**
6. Add redirect URL: `http://localhost:5173/auth/discord/callback`

### 2. Update Local Configuration

Edit `junie-des-1942stats/Properties/launchSettings.json`:

```json
"DiscordOAuth__ClientId": "YOUR_ACTUAL_CLIENT_ID",
"DiscordOAuth__ClientSecret": "YOUR_ACTUAL_CLIENT_SECRET"
```

Replace the placeholder values with your actual Discord credentials.

### 3. Run the Backend

```bash
cd junie-des-1942stats
dotnet run
```

The backend will be available at `http://localhost:9222`

## API Endpoint

### POST `/stats/auth/login`

**Discord Login:**
```json
{
  "discordCode": "authorization_code_from_discord",
  "redirectUri": "http://localhost:5173/auth/discord/callback"
}
```

**Google Login (still works):**
```json
{
  "googleIdToken": "google_id_token"
}
```

**Response (both providers):**
```json
{
  "user": {
    "id": 123,
    "email": "user@example.com",
    "name": "Username"
  },
  "accessToken": "jwt_token",
  "expiresAt": "2024-01-01T00:00:00Z"
}
```

## Production Setup

For production deployment, add the Discord credentials to your Kubernetes secrets:

```bash
kubectl patch secret bf42-stats-secrets \
  --namespace=bf42-stats \
  --type='json' \
  -p='[
    {"op": "add", "path": "/data/discord-client-id", "value": "'$(echo -n 'YOUR_CLIENT_ID' | base64)'"},
    {"op": "add", "path": "/data/discord-client-secret", "value": "'$(echo -n 'YOUR_CLIENT_SECRET' | base64)'"}
  ]'
```

And update your Discord redirect URI to: `https://1942.munyard.dev/auth/discord/callback`

## Files Changed

1. ✅ Created `Services/OAuth/DiscordAuthService.cs` - Handles Discord OAuth flow
2. ✅ Updated `Controllers/AuthController.cs` - Supports both Discord and Google auth
3. ✅ Updated `Program.cs` - Registered Discord auth service
4. ✅ Updated `Properties/launchSettings.json` - Added Discord config placeholders
5. ✅ Updated `deploy/app/deployment.yaml` - Added Kubernetes secret references

## Testing

The frontend should now work with Discord sign-in! The flow is:
1. User clicks "Sign in with Discord" on frontend
2. Discord authorization page opens
3. User authorizes
4. Frontend receives authorization code
5. Frontend sends code to backend `/stats/auth/login`
6. Backend exchanges code for access token
7. Backend fetches user info from Discord
8. Backend creates/updates user in database
9. Backend returns JWT token to frontend
10. User is logged in!

## Troubleshooting

**Error: "DiscordOAuth:ClientId not configured"**
- Make sure you've replaced the placeholder values in launchSettings.json

**Error: "Failed to exchange Discord authorization code"**
- Check that your redirect URI exactly matches what's configured in Discord Developer Portal
- Ensure your client ID and secret are correct

**Error: "Discord account does not have a verified email"**
- User needs to verify their email in Discord settings

## Both Providers Work!

- Users can sign in with either Discord or Google
- Existing Google users continue to work
- If a user has the same email on both Discord and Google, they can use either to log in

See `DISCORD_OAUTH_BACKEND_SETUP.md` for detailed documentation.

