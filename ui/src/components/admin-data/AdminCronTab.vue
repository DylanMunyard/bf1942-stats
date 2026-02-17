<template>
  <section class="portal-card portal-cron">
    <div class="portal-cron-head">
      <h3 class="portal-cron-title">[ CRON ]</h3>
      <p class="portal-cron-desc">Trigger background jobs on demand. After deleting a round, run <strong>Daily Aggregate Refresh</strong> to recalc kills/deaths for affected periods.</p>
    </div>
    <div v-if="jobError" class="portal-cron-err">{{ jobError }}</div>
    <div v-if="jobSuccess" class="portal-cron-ok">{{ jobSuccess }}</div>
    <div class="portal-cron-list">
      <div class="portal-cron-item portal-cron-item--priority">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Daily Aggregate Refresh</span>
          <span class="portal-cron-item-desc">ServerHourlyPatterns, HourlyPlayerPredictions, MapGlobalAverages. Run after round delete.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--primary portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('daily-aggregate-refresh', true)"
        >
          {{ jobRunning === 'daily-aggregate-refresh' ? 'running...' : 'Run' }}
        </button>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Weekly Cleanup</span>
          <span class="portal-cron-item-desc">Stale this_week best scores, prune ServerOnlineCounts.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--ghost portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('weekly-cleanup', true)"
        >
          {{ jobRunning === 'weekly-cleanup' ? 'running...' : 'Run' }}
        </button>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Aggregate Backfill (tier)</span>
          <span class="portal-cron-item-desc">Tier 1–4 by recency. Fire-and-forget; check logs.</span>
        </div>
        <div class="portal-cron-item-actions">
          <select v-model.number="aggregateBackfillTier" class="portal-cron-select">
            <option :value="1">1</option>
            <option :value="2">2</option>
            <option :value="3">3</option>
            <option :value="4">4</option>
          </select>
          <button
            type="button"
            class="portal-btn portal-btn--ghost portal-btn--sm"
            :disabled="jobRunning !== null"
            @click="runJob('aggregate-backfill-tier', false)"
          >
            Start tier {{ aggregateBackfillTier }}
          </button>
        </div>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Aggregate Backfill (full)</span>
          <span class="portal-cron-item-desc">All tiers. Fire-and-forget.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--ghost portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('aggregate-backfill', false)"
        >
          Start
        </button>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Server Map Stats Backfill</span>
          <span class="portal-cron-item-desc">Full from Rounds; daily only ~2 months.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--ghost portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('server-map-stats-backfill', false)"
        >
          Start
        </button>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Map Hourly Patterns Backfill</span>
          <span class="portal-cron-item-desc">Full from Rounds; daily ~60 days.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--ghost portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('map-hourly-patterns-backfill', false)"
        >
          Start
        </button>
      </div>
      <div class="portal-cron-item">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Run All</span>
          <span class="portal-cron-item-desc">Daily refresh + weekly cleanup. Fire-and-forget.</span>
        </div>
        <button
          type="button"
          class="portal-btn portal-btn--ghost portal-btn--sm"
          :disabled="jobRunning !== null"
          @click="runJob('run-all', false)"
        >
          Run all
        </button>
      </div>
      <div v-if="neo4jEnabled" class="portal-cron-item portal-cron-item--neo4j">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Neo4j Sync (Player Relationships)</span>
          <span class="portal-cron-item-desc">
            Sync player co-play data to graph database. Last {{ neo4jSyncDays }} days. 
            <span v-if="neo4jStatus" class="portal-cron-neo4j-status">
              Schema: {{ neo4jStatus.isUpToDate ? '✓ up-to-date' : `⚠ ${neo4jStatus.pendingCount} pending` }}
            </span>
          </span>
        </div>
        <div class="portal-cron-item-actions">
          <select v-model.number="neo4jSyncDays" class="portal-cron-select">
            <option :value="1">1 day</option>
            <option :value="7">7 days</option>
            <option :value="30">30 days</option>
            <option :value="90">90 days</option>
          </select>
          <button
            type="button"
            class="portal-btn portal-btn--primary portal-btn--sm"
            :disabled="jobRunning !== null"
            @click="runJob('neo4j-sync', true)"
          >
            {{ jobRunning === 'neo4j-sync' ? 'syncing...' : 'Sync' }}
          </button>
        </div>
      </div>
      <div v-if="!neo4jEnabled && neo4jChecked" class="portal-cron-item portal-cron-item--disabled">
        <div class="portal-cron-item-body">
          <span class="portal-cron-item-name">Neo4j Sync</span>
          <span class="portal-cron-item-desc">Neo4j is not enabled in API configuration.</span>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import * as adminJobsService from '@/services/adminJobsService';

const jobRunning = ref<string | null>(null);
const jobError = ref<string | null>(null);
const jobSuccess = ref<string | null>(null);
const aggregateBackfillTier = ref(1);
const neo4jSyncDays = ref(7);
const neo4jEnabled = ref(false);
const neo4jChecked = ref(false);
const neo4jStatus = ref<{
  appliedCount: number;
  pendingCount: number;
  isUpToDate: boolean;
  appliedMigrations: string[];
  pendingMigrations: string[];
} | null>(null);

onMounted(async () => {
  // Check if Neo4j is enabled by trying to get migration status
  try {
    neo4jStatus.value = await adminJobsService.getNeo4jMigrationStatus();
    neo4jEnabled.value = true;
  } catch (e) {
    // Neo4j not enabled or not accessible
    neo4jEnabled.value = false;
  } finally {
    neo4jChecked.value = true;
  }
});

async function runJob(jobKey: string, _isBlocking: boolean) {
  jobError.value = null;
  jobSuccess.value = null;
  jobRunning.value = jobKey;
  
  if (jobKey === 'neo4j-sync') {
    try {
      const res = await adminJobsService.triggerNeo4jSync(neo4jSyncDays.value);
      if (res.success) {
        jobSuccess.value = `Synced ${res.relationshipsProcessed} relationships in ${res.durationMs}ms`;
        // Refresh migration status
        try {
          neo4jStatus.value = await adminJobsService.getNeo4jMigrationStatus();
        } catch (e) {
          // Ignore error, migration status is optional
        }
      } else {
        jobError.value = res.errorMessage || 'Neo4j sync failed';
      }
    } catch (e) {
      jobError.value = e instanceof Error ? e.message : 'Neo4j sync failed';
    } finally {
      jobRunning.value = null;
    }
    return;
  }

  let fn: () => Promise<{ message?: string; error?: string }>;
  switch (jobKey) {
    case 'daily-aggregate-refresh':
      fn = adminJobsService.triggerDailyAggregateRefresh;
      break;
    case 'weekly-cleanup':
      fn = adminJobsService.triggerWeeklyCleanup;
      break;
    case 'aggregate-backfill-tier':
      fn = () => adminJobsService.triggerAggregateBackfillTier(aggregateBackfillTier.value);
      break;
    case 'aggregate-backfill':
      fn = adminJobsService.triggerAggregateBackfill;
      break;
    case 'server-map-stats-backfill':
      fn = adminJobsService.triggerServerMapStatsBackfill;
      break;
    case 'map-hourly-patterns-backfill':
      fn = adminJobsService.triggerMapHourlyPatternsBackfill;
      break;
    case 'run-all':
      fn = adminJobsService.triggerRunAll;
      break;
    default:
      jobRunning.value = null;
      return;
  }
  try {
    const res = await fn();
    jobSuccess.value = res.message ?? (_isBlocking ? 'Done.' : 'Started. Check logs.');
  } catch (e) {
    jobError.value = e instanceof Error ? e.message : 'Job failed';
  } finally {
    jobRunning.value = null;
  }
}
</script>

<style scoped>
.portal-cron-item--neo4j {
  border-left: 3px solid #00d4aa;
}

.portal-cron-neo4j-status {
  font-size: 0.85em;
  opacity: 0.8;
  margin-left: 0.5em;
}

.portal-cron-item--disabled {
  opacity: 0.5;
}

.portal-cron-item--disabled .portal-cron-item-name {
  text-decoration: line-through;
}
</style>
