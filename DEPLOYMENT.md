# Deployment Guide

## Prerequisites

- Kubernetes cluster (k3s or similar)
- kubectl configured for your cluster
- OpenSSL for key generation

## JWT Signing Key Generation

The API uses RS256 (RSA SHA256) for JWT signing. Generate a private key:

```bash
openssl genrsa -out jwt-private.pem 2048
openssl rsa -in jwt-private.pem -pubout -out jwt-public.pem
```

### Kubernetes Deployment

Store the private key as a secret:

```bash
kubectl create secret generic jwt-keys \
  --from-file=jwt-private.pem=/path/to/jwt-private.pem \
  -n bf42-stats
```

Set environment variables in your deployment:

```yaml
env:
  - name: Jwt__PrivateKeyPath
    value: /var/run/secrets/jwt/jwt-private.pem
  - name: Jwt__Issuer
    value: https://api.bfstats.io
  - name: Jwt__Audience
    value: https://bfstats.io
  - name: Jwt__AccessTokenMinutes
    value: "10080"  # 7 days

volumeMounts:
  - name: jwt-keys
    mountPath: /var/run/secrets/jwt
    readOnly: true

volumes:
  - name: jwt-keys
    secret:
      secretName: jwt-keys
```

## Refresh Token Secret Generation

Refresh tokens are hashed with HMACSHA256. Generate a strong secret (minimum 32 bytes, 64 bytes recommended):

```bash
# Using OpenSSL (recommended)
openssl rand -base64 64

# Or using /dev/urandom
head -c 64 /dev/urandom | base64
```

### Kubernetes Deployment

Store as a secret:

```bash
kubectl -n bf42-stats create secret generic bf42-stats-secrets \
  --from-literal=refresh-token-secret="$(openssl rand -base64 64)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

Set environment variables in your deployment:

```yaml
env:
  - name: RefreshToken__Secret
    valueFrom:
      secretKeyRef:
        name: bf42-stats-secrets
        key: refresh-token-secret
  - name: RefreshToken__CookieName
    value: rt
  - name: RefreshToken__CookiePath
    value: /stats
  - name: RefreshToken__Days
    value: "60"
  - name: RefreshToken__CookieDomain
    value: bfstats.io  # Adjust for your domain
```

## Configuration Overview

| Setting | Type | Required | Description |
|---------|------|----------|-------------|
| `Jwt:PrivateKeyPath` | string | Yes | Path to RSA private key (PEM format) |
| `Jwt:Issuer` | string | Yes | Token issuer claim (e.g., `https://api.bfstats.io`) |
| `Jwt:Audience` | string | Yes | Token audience claim (e.g., `https://bfstats.io`) |
| `Jwt:AccessTokenMinutes` | int | No | Access token lifetime in minutes (default: 10080) |
| `RefreshToken:Secret` | string | Yes | HMACSHA256 secret for token hashing |
| `RefreshToken:CookieName` | string | No | Cookie name (default: `rt`) |
| `RefreshToken:CookiePath` | string | No | Cookie path (default: `/stats`) |
| `RefreshToken:CookieDomain` | string | No | Cookie domain (optional) |
| `RefreshToken:Days` | int | No | Refresh token lifetime in days (default: 60) |

## Notes

- **Separate Keys:** JWT signing uses the RSA private key, while refresh tokens use a separate HMAC secret. Do not reuse keys.
- **Secure Storage:** Both secrets should be stored securely in Kubernetes Secrets and never committed to version control.
- **Cookie Security:** In development, cookies are issued with `Secure=false`. In production, they are `Secure=true` automatically.
- **Token Rotation:** Refresh tokens are automatically rotated on each refresh request. Expired or invalid tokens are revoked.
