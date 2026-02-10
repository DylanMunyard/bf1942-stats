# Ingress via Cloudflare tunnel

CloudFlare has a tunnel to a [deployment running](./cloudflared-tunnel.yml) inside the cluster.

The deployment forwards all traffic to [haproxy](./deployment.yaml), which acts like a load balancer in to the cluster.

Go to [CloudFlare Tunnels](https://one.dash.cloudflare.com/eec964a0ad385fef75646d4b6b7d2f51/networks/tunnels) to see status of the link.

```bash
# Install cloudflared locally
wget https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64
chmod +x cloudflared-linux-amd64

# Login to Cloudflare
cloudflared tunnel login

# Create tunnel
cloudflared tunnel create k3s-tunnel

# Note the Tunnel ID and credentials file location
```

Create the Kube secret from tunnel credentials

```bash
kubectl create ns cloudflared
kubectl create secret generic tunnel-credentials \
  --from-file=credentials.json=/home/dylan/.cloudflared/70d35215-771d-4e6e-a220-cf113b0fb1ae.json --namespace cloudflared
```

Apply [cloudflared-tunnel.yml](./cloudflared-tunnel.yml)

Route DNS via the tunnel

```bash
cloudflared tunnel route dns aks-tunnel bfstats.io
cloudflared tunnel route dns aks-tunnel staging.bfstats.io
```
