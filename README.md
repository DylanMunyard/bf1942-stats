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

```bash
# Add TCP entrypoint
kubectl patch deployment traefik -n kube-system --type='json' \
  -p='[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--entrypoints.redis.address=:6379/tcp"}]'

# Add service port
kubectl patch service traefik -n kube-system --type='merge' \
  -p='{"spec":{"ports":[{"name":"web","port":80,"protocol":"TCP","targetPort":"web"},{"name":"websecure","port":443,"protocol":"TCP","targetPort":"websecure"},{"name":"redis","port":6379,"protocol":"TCP","targetPort":"redis"}]}}'

# Add container port
kubectl patch deployment traefik -n kube-system --type='json' \
  -p='[{"op":"add","path":"/spec/template/spec/containers/0/ports/-","value":{"containerPort":6379,"name":"redis","protocol":"TCP"}}]'
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