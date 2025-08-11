# BF1942 Stats

Battlefield 1942 player and server statistics tracking application.

## Redis TCP Ingress Configuration

Since Redis requires raw TCP connections (not HTTP), special Traefik configuration is needed for external access.

### Required Traefik Configuration

For Redis to work through Traefik ingress, you need to configure a TCP entrypoint. Add these to your Traefik configuration:

#### Via Helm Values (Recommended for k3s)

Add this to your k3s `traefik.yaml` manifest in the `valuesContent` section:

```yaml
apiVersion: helm.cattle.io/v1
kind: HelmChart
metadata:
  name: traefik
  namespace: kube-system
spec:
  chart: https://%{KUBERNETES_API}%/static/charts/traefik-27.0.201+up27.0.2.tgz
  set:
    global.systemDefaultRegistry: ""
  valuesContent: |-
    # ... existing config ...
    
    # Add Redis TCP entrypoint
    entryPoints:
      redis:
        address: ":6379/tcp"
    
    # Expose Redis port in service
    ports:
      redis:
        port: 6379
        expose: true
        exposedPort: 6379
        protocol: TCP
```

#### Via Manual Patching (Temporary)
If you need to patch an existing Traefik deployment:

**For production Redis (port 6379):**
```bash
kubectl patch deployment traefik -n kube-system --type='json' -p='[
  {
    "op": "add",
    "path": "/spec/template/spec/containers/0/args/-",
    "value": "--entrypoints.redis.address=:6379/tcp"
  },
  {
    "op": "add",
    "path": "/spec/template/spec/containers/0/ports/-",
    "value": {
      "containerPort": 6379,
      "name": "redis",
      "protocol": "TCP"
    }
  }
]'
```

**For dev Redis (port 6380):**
```bash
kubectl patch deployment traefik -n kube-system --type='json' -p='[
  {
    "op": "add",
    "path": "/spec/template/spec/containers/0/args/-",
    "value": "--entrypoints.redis-dev.address=:6380/tcp"
  },
  {
    "op": "add",
    "path": "/spec/template/spec/containers/0/ports/-",
    "value": {
      "containerPort": 6380,
      "name": "redis-dev",
      "protocol": "TCP"
    }
  }
]'
```

Then deploy regular ClusterIP services for your Redis instances.

**Add LoadBalancer Service Ports:**
```bash
# Add port 6380 to Traefik LoadBalancer service
kubectl patch svc traefik -n kube-system --type='json' -p='[{"op": "add", "path": "/spec/ports/-", "value": {"name": "redis-dev", "port": 6380, "protocol": "TCP", "targetPort": "redis-dev"}}]'
```

### Redis Ingress Configuration

The Redis ingress must use `IngressRouteTCP` with wildcard HostSNI for raw TCP:

```yaml
apiVersion: traefik.containo.us/v1alpha1
kind: IngressRouteTCP
metadata:
  name: redis
  namespace: bf42-stats
spec:
  entryPoints:
    - redis
  routes:
  - match: HostSNI(`*`)
    services:
    - name: redis-service
      port: 6379
```

**Note**: Use `HostSNI('*')` instead of a specific hostname because Redis doesn't use TLS/SNI.

### Connection Configuration

- **Local Development**: `42redis.home.net:6379`
- **In-Cluster**: `redis-service.bf42-stats:6379`

The application automatically uses the appropriate connection string based on the `REDIS_CONNECTION_STRING` environment variable.

## JWT Signing Keys (Access Token) Configuration

The API issues short‑lived access tokens (JWT) and long‑lived refresh tokens. Signing uses RS256 only. Provide an RSA private key via configuration (works the same in dev and Kubernetes).

Required settings:
- `Jwt:Issuer` – e.g. `https://api.example.com`
- `Jwt:Audience` – e.g. `https://app.example.com`
- `Jwt:PrivateKey` (inline PEM) or `Jwt:PrivateKeyPath` (absolute file path to PEM)

ASP.NET Core environment variables use `__` instead of `:` (e.g., `Jwt__PrivateKeyPath`).

### Generate keys

RS256 (PEM):

```bash
openssl genrsa -out jwt-private.pem 2048
openssl rsa -in jwt-private.pem -pubout -out jwt-public.pem
```

### Local development examples

- RS256 via file path:

```bash
export Jwt__PrivateKeyPath=/home/you/secrets/jwt-private.pem
export Jwt__Issuer=https://localhost:5001
export Jwt__Audience=http://localhost:5173
```

You can also use `dotnet user-secrets` in the project directory:

```bash
dotnet user-secrets set "Jwt:PrivateKeyPath" "/home/you/secrets/jwt-private.pem"
dotnet user-secrets set "Jwt:Issuer" "https://localhost:5001"
dotnet user-secrets set "Jwt:Audience" "http://localhost:5173"
```

### Kubernetes example (RS256 via Secret file mount)

```bash
kubectl create secret generic jwt-keys \
  --from-file=jwt-private.pem=/path/to/jwt-private.pem \
  -n bf42-stats
```

Deployment excerpt:

```yaml
env:
  - name: Jwt__PrivateKeyPath
    value: /var/run/secrets/jwt/jwt-private.pem
  - name: Jwt__Issuer
    value: https://api.example.com
  - name: Jwt__Audience
    value: https://app.example.com
volumeMounts:
  - name: jwt-keys
    mountPath: /var/run/secrets/jwt
    readOnly: true
volumes:
  - name: jwt-keys
    secret:
      secretName: jwt-keys
```

If a key is not provided, the application will fail fast at startup with a clear error message indicating which configuration keys are accepted.

## Refresh Token Configuration

Long‑lived refresh tokens are stored hashed with HMAC‑SHA256. Configure the HMAC secret and cookie behavior via the following keys:

Required:
- `RefreshToken:Secret` – high‑entropy server‑side secret used as HMAC key to hash/verify refresh tokens. Not related to JWT signing and must be different from the JWT private key.

Optional (defaults shown):
- `RefreshToken:CookieName` – defaults to `rt`
- `RefreshToken:CookiePath` – defaults to `/stats`
- `RefreshToken:Days` – defaults to `60`
- `RefreshToken:CookieDomain` – no default; set in production (e.g., `1942.munyard.dev`) if you need the cookie scoped to a domain

Notes:
- In Development, cookies are issued with `Secure=false`; in non‑Development they are `Secure=true` automatically.

### Generate a strong RefreshToken secret

Use at least 32 bytes of randomness (256‑bit). 64 bytes is recommended.

Examples (64 random bytes, Base64‑encoded):

```bash
# OpenSSL (recommended)
openssl rand -base64 64

# /dev/urandom (portable)
head -c 64 /dev/urandom | base64
```

### Local development

Set via environment variables:

```bash
export RefreshToken__Secret="$(openssl rand -base64 64)"
export RefreshToken__CookieName=rt
export RefreshToken__CookiePath=/stats
export RefreshToken__Days=60
```

Or store in user‑secrets (recommended during local dev):

```bash
dotnet user-secrets set "RefreshToken:Secret" "$(openssl rand -base64 64)" \
  --project junie-des-1942stats/junie-des-1942stats.csproj
```

### Kubernetes example

Provision the secret and wire it as env var (the deployment already references these keys):

```bash
kubectl -n bf42-stats create secret generic bf42-stats-secrets \
  --from-literal=refresh-token-secret="$(openssl rand -base64 64)" \
  --dry-run=client -o yaml | kubectl apply -f -
```

Deployment excerpt:

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
  # In production, consider setting the cookie domain to your apex
  - name: RefreshToken__CookieDomain
    value: 1942.munyard.dev
```

Security reminder:
- Never reuse the JWT private key as the refresh token secret. JWTs use RS256 (asymmetric) with the private key, while refresh tokens use HMAC‑SHA256 (symmetric) with `RefreshToken:Secret`.