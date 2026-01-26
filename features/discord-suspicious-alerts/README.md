# Discord Suspicious Round Alerts

Automated Discord notifications when BF1942 rounds complete with suspicious player scores.

## Overview

When a round ends on a BF1942 server, the system checks if any players achieved a score >= 200. If so, a Discord notification is sent with details about the suspicious activity.

## Configuration

Config is provided via `DiscordSuspicious` (IOptions / `appsettings` or env `DiscordSuspicious__*`). The API deployment maps these from Kubernetes.

### Configuration Keys

| Key (config / env) | Required | Default | Description |
|-------------------|----------|---------|-------------|
| `DiscordSuspicious:RoundWebhookUrl` / `DiscordSuspicious__RoundWebhookUrl` | No | - | Discord webhook URL to send alerts. Typically from a secret. |
| `DiscordSuspicious:ScoreThreshold` / `DiscordSuspicious__ScoreThreshold` | No | 200 | Score threshold to trigger alerts |

### Kubernetes Secret Setup

The webhook URL is in the `discord-secrets` secret under `suspicious-round-webhook` and is injected as `DiscordSuspicious__RoundWebhookUrl`. Create/patch the secret via kubectl — see **deploy/app/DISCORD_SECRETS.md** for the full `kubectl create secret generic` and `kubectl patch` commands.

The `suspicious-round-webhook` key is optional (`optional: true` in the deployment); if missing, alerts are skipped.

## Alert Format

When triggered, Discord receives an embed message:

```
🚨 Suspicious Round Detected

**El Alamein** on **DCG Server**
Player scores >= 200

**Players:**
• PlayerName1: 450 score (25 kills, 3 deaths)
• PlayerName2: 230 score (18 kills, 2 deaths)

🔗 View Round Report
https://bfstats.io/rounds/<round-id>/report
```

## Scope

- **Games**: BF1942 only (not FH2 or BFV)
- **Servers**: All servers
- **Trigger**: Round completion (map change detected)
- **Criteria**: Total score >= threshold (default 200)

## Implementation Details

- **Service**: `api/DiscordNotifications/DiscordWebhookService.cs`
- **Hook Point**: `PlayerTrackingService.EnsureActiveRoundAsync()` - fires after round is marked complete
- **Execution**: Fire-and-forget (non-blocking, errors logged but don't affect round tracking)
