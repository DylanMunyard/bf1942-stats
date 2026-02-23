<script setup lang="ts">
import { computed } from 'vue'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'
import { formatDistanceToNow } from 'date-fns'

const props = defineProps<{
  community: PlayerCommunity
}>()

const cohesionPercentage = computed(() => Math.round(props.community.cohesionScore * 100))

const formatDate = (dateStr: string) => new Date(dateStr).toLocaleDateString()

const statusColor = computed(() => {
  if (!props.community.isActive) return 'text-neutral-500'
  if (props.community.cohesionScore >= 0.7) return 'text-cyan-400'
  if (props.community.cohesionScore >= 0.5) return 'text-green-400'
  return 'text-yellow-400'
})

const statusLabel = computed(() => {
  if (!props.community.isActive) return 'INACTIVE'
  if (props.community.cohesionScore >= 0.7) return 'TIGHT-KNIT'
  if (props.community.cohesionScore >= 0.5) return 'ACTIVE'
  return 'EMERGING'
})
</script>

<template>
  <div class="community-card">
    <!-- Header -->
    <div class="community-header">
      <div class="flex-1 min-w-0">
        <h3 class="community-name">{{ community.name }}</h3>
        <p class="community-id">{{ community.id }}</p>
      </div>
      <div class="status-badge" :class="statusColor">
        {{ statusLabel }}
      </div>
    </div>

    <!-- Stats Grid -->
    <div class="stats-grid">
      <div class="stat-item">
        <div class="stat-value">{{ community.memberCount }}</div>
        <div class="stat-label">Members</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ cohesionPercentage }}%</div>
        <div class="stat-label">Cohesion</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ community.avgSessionsPerPair.toFixed(1) }}</div>
        <div class="stat-label">Avg Sessions</div>
      </div>
      <div class="stat-item">
        <div class="stat-value">{{ (community.memberCount > 0 ? (community.primaryServers.length / community.memberCount * 10).toFixed(1) : 0) }}</div>
        <div class="stat-label">Servers</div>
      </div>
    </div>

    <!-- Core Members -->
    <div class="section">
      <h4 class="section-title">CORE MEMBERS</h4>
      <div class="members-list">
        <router-link
          v-for="member in community.coreMembers.slice(0, 5)"
          :key="member"
          :to="`/players/${encodeURIComponent(member)}`"
          class="member-link"
        >
          {{ member }}
        </router-link>
        <span v-if="community.coreMembers.length > 5" class="member-more">
          +{{ community.coreMembers.length - 5 }}
        </span>
      </div>
    </div>

    <!-- Primary Servers -->
    <div class="section">
      <h4 class="section-title">PRIMARY SERVERS</h4>
      <div class="servers-list">
        <div
          v-for="(serverName, idx) in community.primaryServers.slice(0, 3)"
          :key="idx"
          class="server-item"
        >
          <div class="server-name">{{ serverName }}</div>
        </div>
      </div>
    </div>

    <!-- Dates -->
    <div class="dates-section">
      <div class="date-item">
        <span class="date-label">Formed</span>
        <span class="date-value">{{ formatDate(community.formationDate) }}</span>
      </div>
      <div class="date-item">
        <span class="date-label">Last Active</span>
        <span class="date-value">{{ formatDistanceToNow(new Date(community.lastActiveDate), { addSuffix: true }) }}</span>
      </div>
    </div>

    <!-- View Button -->
    <router-link :to="`/communities/${encodeURIComponent(community.id)}`" class="view-button">
      View Community →
    </router-link>
  </div>
</template>

<style scoped>
.community-card {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.25rem;
  background: var(--portal-surface, #0f0f15);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 8px;
  transition: all 0.2s ease;
}

.community-card:hover {
  border-color: var(--portal-accent, #00e5a0);
  background: var(--portal-surface-hover, #13131b);
}

.community-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  min-width: 0;
}

.community-name {
  font-size: 1rem;
  font-weight: 600;
  color: var(--portal-text-bright, #e5e7eb);
  margin: 0;
  word-break: break-word;
}

.community-id {
  font-size: 0.75rem;
  color: var(--portal-text-muted, #6b7280);
  margin: 0.25rem 0 0 0;
  font-family: 'JetBrains Mono', monospace;
  word-break: break-all;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.375rem 0.75rem;
  background: var(--portal-surface-elevated, #111118);
  border: 1px solid currentColor;
  border-radius: 4px;
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  white-space: nowrap;
  flex-shrink: 0;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.75rem;
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 0.75rem;
  background: var(--portal-surface-elevated, #111118);
  border: 1px solid var(--portal-border-dim, rgba(26, 26, 36, 0.5));
  border-radius: 6px;
}

.stat-value {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--portal-accent, #00e5a0);
  font-family: 'JetBrains Mono', monospace;
}

.stat-label {
  font-size: 0.65rem;
  color: var(--portal-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-top: 0.25rem;
}

.section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.section-title {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--portal-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 0;
}

.members-list {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.member-link {
  display: inline-flex;
  align-items: center;
  padding: 0.375rem 0.75rem;
  background: var(--portal-surface-elevated, #111118);
  border: 1px solid var(--portal-border-dim, rgba(26, 26, 36, 0.5));
  border-radius: 4px;
  font-size: 0.8rem;
  color: var(--portal-accent, #00e5a0);
  text-decoration: none;
  transition: all 0.2s ease;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100%;
}

.member-link:hover {
  background: var(--portal-surface, #0f0f15);
  border-color: var(--portal-accent, #00e5a0);
}

.member-more {
  display: inline-flex;
  align-items: center;
  padding: 0.375rem 0.75rem;
  font-size: 0.8rem;
  color: var(--portal-text-muted, #6b7280);
}

.servers-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.server-item {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.server-name {
  font-size: 0.875rem;
  color: var(--portal-text, #9ca3af);
  word-break: break-word;
}

.server-activity {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.activity-bar {
  flex: 1;
  height: 6px;
  background: var(--portal-surface-elevated, #111118);
  border-radius: 3px;
  overflow: hidden;
}

.activity-fill {
  height: 100%;
  background: var(--portal-accent, #00e5a0);
  border-radius: 3px;
  transition: width 0.2s ease;
}

.activity-label {
  font-size: 0.65rem;
  color: var(--portal-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.02em;
  font-weight: 600;
  min-width: 60px;
  text-align: right;
}

.dates-section {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--portal-border-dim, rgba(26, 26, 36, 0.5));
}

.date-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.date-label {
  font-size: 0.65rem;
  color: var(--portal-text-muted, #6b7280);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.date-value {
  font-size: 0.875rem;
  color: var(--portal-text, #9ca3af);
}

.view-button {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.75rem 1rem;
  background: var(--portal-accent-dim, rgba(0, 229, 160, 0.12));
  border: 1px solid var(--portal-accent, #00e5a0);
  border-radius: 6px;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--portal-accent, #00e5a0);
  text-decoration: none;
  transition: all 0.2s ease;
  margin-top: 0.25rem;
}

.view-button:hover {
  background: var(--portal-accent, #00e5a0);
  color: var(--portal-bg, #06060a);
}

@media (max-width: 640px) {
  .community-card {
    padding: 1rem;
    gap: 0.75rem;
  }

  .stats-grid {
    grid-template-columns: repeat(2, 1fr);
    gap: 0.5rem;
  }

  .stat-item {
    padding: 0.5rem;
  }

  .stat-value {
    font-size: 1rem;
  }

  .stat-label {
    font-size: 0.6rem;
  }
}
</style>
