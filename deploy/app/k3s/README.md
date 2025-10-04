## Cross-Cluster Connectivity

  This setup allows Prometheus running in the on-prem K3s cluster to scrape metrics from services in the AKS cluster via Tailscale.

  ### How it works

  1. **ExternalName Service** (monitoring namespace) - Triggers Tailscale operator to create a proxy pod
     - Annotation `tailscale.com/tailnet-ip` specifies the target service's Tailscale IP
     - Tailscale creates a proxy pod in the `tailscale` namespace that forwards traffic through the tunnel

  2. **ClusterIP Service** (tailscale namespace) - Exposes the Tailscale proxy pod
     - Selects the proxy pod using Tailscale's auto-generated labels
     - Defines the metrics port (9091) for Prometheus to scrape
     - This is required, as Prometheus requires a port to configure the target

  3. **ServiceMonitor** (tailscale namespace) - Configures Prometheus scraping
     - Discovers the ClusterIP service via label selector
     - Prometheus scrapes the service, which forwards requests through Tailscale to AKS

  ### Traffic flow

  Prometheus → ClusterIP Service → Tailscale Proxy Pod → Tailscale Network → AKS Service

  The Tailscale proxy pod performs IP forwarding/NAT, transparently routing traffic from the local cluster to the remote AKS service.

