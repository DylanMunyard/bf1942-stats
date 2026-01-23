# Production Snippets

Handy commands for managing the bfstats production environment.

## Azure AKS

### Create a new system node pool

```bash
az aks nodepool add \
  --resource-group bfstats-io \
  --cluster-name bfstats-aks \
  --name bfstat2core \
  --node-count 1 \
  --max-pods 90 \
  --node-vm-size Standard_E2ps_v6 \
  --mode System
```

Creates a new system node pool with:
- **Standard_E2ps_v6**: 2 vCPUs, 16 GB RAM, ARM64-based Cobalt 100 processor (cost-effective)
- **System mode**: Required for running critical system pods (CoreDNS, etc.)
- **max-pods 90**: Azure CNI default, adjust based on workload density
