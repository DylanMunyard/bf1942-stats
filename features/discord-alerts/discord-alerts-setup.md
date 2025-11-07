# Discord Alerts Setup

## Overview
Integration of Discord webhooks for real-time alerts on application performance metrics using Grafana and Prometheus/Kube-Prometheus stack.

## Alert Metrics

### 1. High Response Time
**Threshold:** p99 response time > 3 seconds (sustained 5 minutes)
**Data Source:** Application metrics (Prometheus)

```promql
histogram_quantile(0.99, rate(http_server_request_duration_seconds_bucket[5m])) > 3
```

### 2. High Memory Usage
**Threshold:** Container working set memory > 1.5GB (sustained 5 minutes)
**Data Source:** Kube-Prometheus (cluster metrics)

```promql
sum(
    container_memory_working_set_bytes{
      cluster=~"$cluster",
      namespace="bf1942-stats",
      container!="",
      image!=""
    }
  * on(namespace,pod)
    group_left(workload, workload_type) namespace_workload_pod:kube_pod_owner:relabel{
      cluster=~"$cluster",
      namespace="bf1942-stats",
      workload="bf42-stats",
      workload_type="deployment"
    }
) by (pod) / 1024 / 1024 / 1024 > 1.5
```

### 3. High CPU Usage
**Threshold:** >= 1 CPU core (sustained 5 minutes)
**Data Source:** Kube-Prometheus (cluster metrics)

```promql
sum(
    node_namespace_pod_container:container_cpu_usage_seconds_total:sum_rate5m{
      cluster=~"$cluster",
      namespace="bf1942-stats"
    }
  * on(namespace,pod)
    group_left(workload, workload_type) namespace_workload_pod:kube_pod_owner:relabel{
      cluster=~"$cluster",
      namespace="bf1942-stats",
      workload="bf42-stats"
    }
)
```

### 4. High Error Rate

## Metrics Availability

| Metric | Source | Exposed At |
|--------|--------|-----------|
| Response Time | Application | `http://api:8080/metrics` |
| Memory | Kube-Prometheus Stack | Cluster-wide |
| Error Rate | Application | `http://api:8080/metrics` |
| CPU | Kube-Prometheus Stack | Cluster-wide |
**Threshold:** > 5% of requests returning 4xx/5xx (sustained 5 minutes)
**Data Source:** Application metrics (Prometheus)

```promql
sum(rate(http_server_request_duration_seconds_bucket{http_response_status_code=~"[45].."}[5m])) / sum(rate(http_server_request_duration_seconds_bucket[5m])) > 0.05
```

## Grafana Configuration

### Discord Contact Point Setup
1. **Alerting → Contact points**
2. **New contact point**
   - **Name:** `discord-alerts`
   - **Type:** Discord
   - **Discord webhook URL:** [Your regenerated webhook URL]
3. **Save**

### Alert Rule Setup (for each rule above)
1. **Alerting → Alert rules → New alert rule**
2. Configure:
   - **Name:** (e.g., "High Response Time")
   - **Data source:** Prometheus (for rules 1, 2, 4) or Prometheus (for rule 3)
   - **Query A:** [Copy query from section above]
   - **Alert condition:** `A` (default)
   - **For:** `5m`
   - **Contact point:** `discord-alerts`
   - **Labels** (optional): `severity: warning`
3. **Save rule**

## Implementation Notes

- All queries use a 5-minute sustain window to reduce false positives
- Response time uses 99th percentile to catch genuine performance issues
- CPU and Memory monitoring use kube-prometheus `cluster=~"$cluster"` variable - ensure Grafana has this variable defined
- Error rate threshold (5%) can be adjusted based on acceptable error tolerance
- Memory threshold (1.5GB) reflects actual container working set memory from Kubernetes

## Alert Latency

Typical alert delivery time: 5-10 seconds after threshold breach
- Prometheus scrape interval: ~15s
- Alert evaluation: 5m window
- Discord delivery: immediate
