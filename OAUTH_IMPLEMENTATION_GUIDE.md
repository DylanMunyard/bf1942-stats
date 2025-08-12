# OAuth 2.0 Clean Implementation Guide

## Overview

This is a **clean, modern OAuth 2.0** implementation with **no legacy code**. It provides long-lived sessions (30 days) with automatic token refresh using Google's OAuth 2.0 Authorization Code Flow.

## Implementation Summary

### Database Entities
- `UserSession` - Encrypted session tokens and metadata  
- `UserRefreshToken` - Encrypted Google refresh tokens
- `User` - Basic user profile (email, timestamps)

### Services
- `GoogleAuthService` - OAuth 2.0 flows, token validation, rate limiting
- `UserSessionService` - Session management with AES-256 encryption

### Clean AuthController
- `POST /stats/auth/google-callback` - Exchange authorization code for session
- `POST /stats/auth/refresh` - Refresh expired sessions  
- `GET /stats/auth/profile` - Get basic user profile
- `POST /stats/auth/logout` - Revoke session and tokens

## Required Configuration

### 1. Google Cloud Console Setup

**OAuth 2.0 Client Configuration:**
- Client Type: **Web application** (not SPA/JavaScript)
- Authorized Redirect URIs:
  ```
  https://yourdomain.com/auth/callback
  http://localhost:3000/auth/callback  (for development)
  ```

**Required OAuth Scopes:**
- `openid` (for ID token)
- `email` (for email claim)  
- `profile` (for profile info)

### 2. Application Configuration (appsettings.json)

```json
{
  "GoogleOAuth": {
    "ClientId": "your-client-id.googleusercontent.com",
    "ClientSecret": "your-google-client-secret",
    "RedirectUri": "https://yourdomain.com/auth/callback"
  },
  "OAuth": {
    "EncryptionKey": "your-32-character-encryption-key-here"
  }
}
```

**Generate Encryption Key:**
```bash
openssl rand -base64 32
```

### 3. Database Migration

Run the migration to create the new OAuth tables:
```bash
dotnet ef database update
```

## Frontend Changes Required

### Replace Current Google Sign-In

**Old (Implicit Flow):**
```javascript
// Current Google Sign-In button implementation
```

**New (Authorization Code Flow with PKCE):**
```javascript
// 1. Generate PKCE code verifier and challenge
function generateCodeVerifier() {
    const array = new Uint32Array(56/4);
    window.crypto.getRandomValues(array);
    return Array.from(array, dec => ('0' + dec.toString(16)).substr(-2)).join('');
}

function generateCodeChallenge(verifier) {
    const encoder = new TextEncoder();
    const data = encoder.encode(verifier);
    return window.crypto.subtle.digest('SHA-256', data).then(digest => {
        return btoa(String.fromCharCode(...new Uint8Array(digest)))
            .replace(/=/g, '').replace(/\\+/g, '-').replace(/\\//g, '_');
    });
}

// 2. Redirect to Google for authorization
async function initiateGoogleLogin() {
    const codeVerifier = generateCodeVerifier();
    const codeChallenge = await generateCodeChallenge(codeVerifier);
    
    // Store code verifier for later use
    localStorage.setItem('oauth_code_verifier', codeVerifier);
    
    const authUrl = `https://accounts.google.com/o/oauth2/v2/auth?` +
        `client_id=${GOOGLE_CLIENT_ID}&` +
        `redirect_uri=${encodeURIComponent(REDIRECT_URI)}&` +
        `response_type=code&` +
        `scope=openid%20email%20profile&` +
        `code_challenge=${codeChallenge}&` +
        `code_challenge_method=S256&` +
        `access_type=offline&` +  // Critical: requests refresh token
        `prompt=consent`;         // Forces consent for refresh token
    
    window.location.href = authUrl;
}

// 3. Handle callback and exchange code for session
async function handleGoogleCallback() {
    const urlParams = new URLSearchParams(window.location.search);
    const code = urlParams.get('code');
    const codeVerifier = localStorage.getItem('oauth_code_verifier');
    
    if (!code || !codeVerifier) {
        throw new Error('Missing authorization code or verifier');
    }
    
    // Exchange code for session
    const response = await fetch('/stats/auth/google-callback', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            authorizationCode: code,
            codeVerifier: codeVerifier
        })
    });
    
    if (response.ok) {
        const result = await response.json();
        // Session cookie is automatically set by the server
        // Redirect to dashboard or main app
        window.location.href = '/dashboard';
    } else {
        throw new Error('Authentication failed');
    }
    
    // Clean up
    localStorage.removeItem('oauth_code_verifier');
}

// 4. Automatic session refresh (runs periodically)
async function refreshSession() {
    try {
        const response = await fetch('/stats/auth/refresh', {
            method: 'POST',
            credentials: 'include' // Include session cookie
        });
        
        if (response.ok) {
            const result = await response.json();
            console.log('Session refreshed until:', result.expiresAt);
            return true;
        }
        return false;
    } catch (error) {
        console.error('Session refresh failed:', error);
        return false;
    }
}

// 5. Logout function
async function logout() {
    await fetch('/stats/auth/logout', {
        method: 'POST',
        credentials: 'include'
    });
    window.location.href = '/login';
}
```

### Session Management

```javascript
// Check authentication status
async function isAuthenticated() {
    try {
        const response = await fetch('/stats/auth/profile', {
            credentials: 'include'
        });
        return response.ok;
    } catch {
        return false;
    }
}

// Set up automatic session refresh (every 30 minutes)
setInterval(async () => {
    if (await isAuthenticated()) {
        await refreshSession();
    }
}, 30 * 60 * 1000);
```

## Security Features

### Backend Security
- **Encrypted Token Storage**: All tokens encrypted at rest using AES-256
- **Rate Limiting**: 20 attempts per IP per hour
- **Session Management**: Secure HTTP-only cookies with proper SameSite settings
- **Token Validation**: Comprehensive Google JWT validation with age checks
- **Automatic Cleanup**: Expired sessions automatically deactivated

### Frontend Security  
- **PKCE Protection**: Prevents authorization code interception
- **Secure Cookies**: Session IDs only, no sensitive data in browser
- **Automatic Refresh**: Transparent token refresh without user interaction

## Migration Path

1. **Deploy Backend**: The old JWT authentication has been completely removed - only OAuth 2.0 is supported
2. **Update Frontend**: Replace Google Sign-In implementation with OAuth 2.0 flow  
3. **Test**: Verify refresh token functionality works correctly
4. **Update Database**: Run migrations to create new OAuth tables

## Benefits

- **Long Sessions**: 30-day sessions vs 1-hour ID tokens
- **Industry Standard**: Uses OAuth 2.0 as designed, like Microsoft Entra
- **Automatic Refresh**: No more sudden logouts
- **Better Security**: Encrypted storage, rate limiting, comprehensive validation
- **Scalability**: Works across multiple devices/browsers

## Troubleshooting

### Common Issues

1. **No Refresh Token**: Ensure `access_type=offline` and `prompt=consent` in auth URL
2. **Session Expires**: Check that session refresh is being called periodically
3. **CORS Issues**: Ensure redirect URI exactly matches Google Cloud Console configuration
4. **Rate Limiting**: Check IP-based rate limits if authentication is failing

### Configuration Validation

```bash
# Test OAuth configuration
curl -X POST https://yourdomain.com/stats/auth/google-callback \
  -H "Content-Type: application/json" \
  -d '{"authorizationCode":"test","codeVerifier":"test"}'

# Should return 401 with rate limiting info, not 500 server error
```