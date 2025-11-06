# Grafana Alerting Setup with Discord

This guide will help you set up **free Discord alerts** for your LGTP (Loki-Grafana-Tempo-Prometheus) monitoring stack.

## 🎯 Why Discord?

- **100% Free** - No cost at all, perfect for tight Azure budgets
- **Instant notifications** - Get alerts in real-time
- **Easy setup** - Just create a webhook URL
- **Mobile support** - Discord app sends push notifications

## 📋 Prerequisites

- A Discord account (free)
- A Discord server where you have admin permissions (or create a new one)

## 🚀 Setup Instructions

### Step 1: Create a Discord Webhook

1. **Open Discord** and go to your server
2. **Create or select a channel** for alerts (e.g., `#bf1942-alerts`)
3. **Right-click the channel** → **Edit Channel**
4. Go to **Integrations** → **Webhooks** → **New Webhook**
5. **Name the webhook** (e.g., "BF1942 Stats Alerts")
6. **Copy the Webhook URL** - it looks like:
   ```
   https://discord.com/api/webhooks/123456789/abcdefghijklmnop
   ```

### Step 2: Set the Environment Variable

#### Option A: Using .env file (Recommended)

1. Create a `.env` file in the project root (if it doesn't exist):
   ```bash
   touch .env
   ```

2. Add your Discord webhook URL:
   ```env
   DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN
   ```

3. Make sure `.env` is in your `.gitignore` (it should be already)

#### Option B: Export as environment variable

```bash
export DISCORD_WEBHOOK_URL="https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN"
```

### Step 3: Start/Restart Grafana

If Grafana is already running, restart it to pick up the changes:

```bash
docker-compose -f docker-compose.dev.yml down grafana
docker-compose -f docker-compose.dev.yml up -d grafana
```

Or restart the entire stack:

```bash
docker-compose -f docker-compose.dev.yml down
docker-compose -f docker-compose.dev.yml up -d
```

### Step 4: Verify Setup in Grafana

1. **Open Grafana** at http://localhost:3000
2. **Login** (default: admin/admin)
3. Go to **Alerting** → **Contact points**
4. You should see **discord-alerts** configured
5. Click **Test** to send a test notification to your Discord channel

### Step 5: Check Alert Rules

1. In Grafana, go to **Alerting** → **Alert rules**
2. You should see the following pre-configured alerts:
   - **High Error Rate** - Triggers when >10 errors/sec for 2 minutes
   - **Slow API Response Time** - Triggers when >5 slow responses/sec for 3 minutes
   - **Critical Errors Detected** - Triggers on any critical/fatal errors
   - **No Logs Received** - Triggers if service stops logging for 5 minutes

## 📊 Pre-configured Alerts

All alerts are configured to work with **Loki logs** and look for the following patterns:

### 1. High Error Rate
- **Severity**: Warning
- **Threshold**: >10 errors per second
- **Duration**: 2 minutes
- **Looks for**: `error`, `exception`, `fail` in logs

### 2. Slow Response Time
- **Severity**: Warning
- **Threshold**: >5 slow responses per second
- **Duration**: 3 minutes
- **Looks for**: `timeout`, `slow`, `5xx` status codes in logs

### 3. Critical Errors
- **Severity**: Critical
- **Threshold**: >1 critical error
- **Duration**: 1 minute
- **Looks for**: `critical`, `fatal` in logs

### 4. No Logs Received
- **Severity**: Critical
- **Threshold**: <1 log entry in 5 minutes
- **Duration**: 5 minutes
- **Purpose**: Detect if service is down or not logging

## ⚙️ Customizing Alerts

### Adjusting Thresholds

Edit `grafana/provisioning/alerting/alert-rules.yaml` and modify the threshold values:

```yaml
conditions:
  - evaluator:
      params:
        - 10  # Change this number to adjust threshold
```

### Adjusting Alert Duration

Change the `for` field:

```yaml
for: 2m  # Alert only if condition is true for 2 minutes
```

### Adding New Alert Rules

You can add more rules in Grafana UI:

1. **Alerting** → **Alert rules** → **New alert rule**
2. Choose **Loki** as data source
3. Write a LogQL query, e.g.:
   ```
   sum(rate({job="bf1942-stats"} |= "database" |~ "error" [5m]))
   ```
4. Set conditions and thresholds
5. Assign to **discord-alerts** contact point

### Example: Alert on Database Errors

```yaml
- uid: database_errors
  title: Database Connection Errors
  condition: C
  data:
    - refId: A
      datasourceUid: loki
      model:
        expr: 'sum(rate({job="bf1942-stats"} |= "database" |~ "(?i)(error|timeout)" [5m]))'
  for: 2m
  labels:
    severity: critical
```

## 🧪 Testing Your Alerts

### Test Discord Notification

1. Go to **Alerting** → **Contact points** in Grafana
2. Click the **Test** button next to `discord-alerts`
3. Check your Discord channel for the test message

### Trigger a Real Alert

Generate some errors in your application to test the alert rules:

```bash
# Example: Generate errors by sending bad requests
for i in {1..100}; do
  curl -X POST http://localhost:5000/api/invalid-endpoint
done
```

Watch your Discord channel for alerts!

## 💰 Cost Breakdown

- **Discord Webhooks**: $0/month (free)
- **Loki Storage**: Included in your Docker volumes (free)
- **Grafana**: Open source (free)
- **Total**: **$0/month** 🎉

## 🔧 Troubleshooting

### Alerts not appearing in Discord

1. **Check webhook URL** is correct in `.env`
2. **Verify Grafana container** has the environment variable:
   ```bash
   docker exec bf1942-grafana env | grep DISCORD
   ```
3. **Check Grafana logs** for errors:
   ```bash
   docker logs bf1942-grafana
   ```
4. **Test the webhook** manually:
   ```bash
   curl -X POST "$DISCORD_WEBHOOK_URL" \
     -H "Content-Type: application/json" \
     -d '{"content": "Test alert from curl"}'
   ```

### Alert rules not firing

1. **Check if logs are flowing** into Loki:
   - Open Grafana → Explore
   - Select Loki datasource
   - Run query: `{job="bf1942-stats"}`
2. **Verify alert rule queries** match your log format
3. **Check evaluation interval** in alert rules (default: 1m)

### No logs in Loki

1. **Verify your app** is sending logs to Loki:
   - Check `LOKI_URL` environment variable in your app
   - Look for Loki connection errors in app logs
2. **Check Loki is running**:
   ```bash
   docker ps | grep loki
   curl http://localhost:3100/ready
   ```

## 📚 Additional Resources

- [Grafana Alerting Docs](https://grafana.com/docs/grafana/latest/alerting/)
- [LogQL Query Language](https://grafana.com/docs/loki/latest/logql/)
- [Discord Webhook Docs](https://discord.com/developers/docs/resources/webhook)

## 🎨 Customizing Discord Messages

To customize how alerts look in Discord, edit the contact point settings:

```yaml
settings:
  url: ${DISCORD_WEBHOOK_URL}
  title: 'Your Custom Title'
  avatar_url: 'https://your-avatar-url.com/image.png'
  use_discord_username: false
  message: |
    **{{ .GroupLabels.alertname }}**
    {{ .Annotations.summary }}
```

## 🚨 Alert Severity Levels

Alerts are configured with two severity levels:

- **warning**: Non-critical issues (e.g., slow responses, high error rates)
- **critical**: Critical issues (e.g., service down, critical errors)

You can create different Discord channels for different severities by creating multiple contact points and notification policies.

---

**That's it!** You now have free, real-time Discord alerts for your BF1942 Stats monitoring stack. No Gmail setup needed, no SMTP hassles, and $0 cost! 🎉
