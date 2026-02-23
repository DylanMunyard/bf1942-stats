<script setup lang="ts">
import { computed } from 'vue'
import { formatDistanceToNow } from 'date-fns'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const formatDate = (dateStr: string) => new Date(dateStr).toLocaleDateString('en-US', {
  year: 'numeric',
  month: 'long',
  day: 'numeric'
})

const formatDateWithTime = (dateStr: string) => new Date(dateStr).toLocaleString('en-US', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit'
})

const daysActive = computed(() => {
  const formation = new Date(props.community.formationDate).getTime()
  const now = Date.now()
  return Math.floor((now - formation) / (1000 * 60 * 60 * 24))
})

const daysSinceActive = computed(() => {
  const lastActive = new Date(props.community.lastActiveDate).getTime()
  const now = Date.now()
  return Math.floor((now - lastActive) / (1000 * 60 * 60 * 24))
})

const activityStatus = computed(() => {
  if (daysSinceActive.value === 0) return 'Active today'
  if (daysSinceActive.value < 7) return `Active ${daysSinceActive.value}d ago`
  if (daysSinceActive.value < 30) return `Active ${Math.floor(daysSinceActive.value / 7)}w ago`
  return `Active ${Math.floor(daysSinceActive.value / 30)}m ago`
})

const statusColor = computed(() => {
  if (daysSinceActive.value === 0) return 'text-green-400'
  if (daysSinceActive.value < 7) return 'text-cyan-400'
  if (daysSinceActive.value < 30) return 'text-yellow-400'
  return 'text-orange-400'
})

const activityLevel = computed(() => {
  if (daysSinceActive.value === 0) return 'Very Active'
  if (daysSinceActive.value < 7) return 'Active'
  if (daysSinceActive.value < 30) return 'Moderately Active'
  if (daysSinceActive.value < 90) return 'Dormant'
  return 'Inactive'
})

const retentionRate = computed(() => {
  // Estimate based on cohesion and activity
  const cohesionBonus = props.community.cohesionScore * 100
  const activityPenalty = Math.max(0, daysSinceActive.value * 0.5)
  return Math.max(0, cohesionBonus - activityPenalty).toFixed(0)
})
</script>

<template>
  <div class="space-y-4">
    <!-- Activity Status -->
    <div class="explorer-card">
      <div class="explorer-card-body">
        <div class="flex items-center justify-between mb-4">
          <div>
            <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">CURRENT STATUS</div>
            <div class="text-2xl font-bold text-neutral-200 font-mono">{{ activityLevel }}</div>
          </div>
          <div :class="statusColor" class="text-center">
            <div class="text-3xl mb-2">
              {{ daysSinceActive === 0 ? '🔴' : daysSinceActive < 7 ? '🟢' : daysSinceActive < 30 ? '🟡' : '⚫' }}
            </div>
            <div class="text-sm font-mono">{{ activityStatus }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Timeline -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">ACTIVITY TIMELINE</h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-4">
          <!-- Formation -->
          <div class="relative pl-8">
            <div class="absolute left-0 top-1 w-4 h-4 bg-cyan-500 rounded-full" />
            <div class="absolute left-1.5 top-5 w-1 h-12 bg-neutral-700" />
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Community Formed</div>
              <div class="text-sm text-neutral-300 font-mono">{{ formatDate(props.community.formationDate) }}</div>
              <div class="text-xs text-neutral-600 mt-1">{{ daysActive }} days ago</div>
            </div>
          </div>

          <!-- Last Active -->
          <div class="relative pl-8">
            <div :class="{
              'bg-green-500': daysSinceActive === 0,
              'bg-cyan-500': daysSinceActive < 7,
              'bg-yellow-500': daysSinceActive < 30,
              'bg-orange-500': daysSinceActive >= 30
            }" class="absolute left-0 top-1 w-4 h-4 rounded-full" />
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Last Activity</div>
              <div class="text-sm text-neutral-300 font-mono">{{ formatDateWithTime(props.community.lastActiveDate) }}</div>
              <div class="text-xs text-neutral-600 mt-1">{{ activityStatus }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Activity Metrics -->
    <div class="grid grid-cols-2 sm:grid-cols-3 gap-4">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Days Active</div>
          <div class="text-2xl font-bold text-cyan-400 font-mono">{{ daysActive }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Days Since Active</div>
          <div class="text-2xl font-bold text-yellow-400 font-mono">{{ daysSinceActive }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Retention Rate</div>
          <div class="text-2xl font-bold text-green-400 font-mono">{{ retentionRate }}%</div>
        </div>
      </div>
    </div>

    <!-- Activity Info -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">INSIGHTS</h2>
      </div>
      <div class="explorer-card-body space-y-3 text-sm">
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            This community has been active for <strong class="text-cyan-400">{{ daysActive }} days</strong>,
            with members playing together an average of <strong class="text-cyan-400">{{ props.community.avgSessionsPerPair.toFixed(1) }}</strong> times.
          </p>
        </div>
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            Last recorded activity was <strong :class="statusColor">{{ activityStatus }}</strong>.
            {{ daysSinceActive === 0 ? 'The community is thriving!' : 'Check back soon for updates!' }}
          </p>
        </div>
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            With a cohesion score of <strong class="text-purple-400">{{ (props.community.cohesionScore * 100).toFixed(0) }}%</strong>,
            this is a {{ props.community.cohesionScore > 0.7 ? 'very tight-knit' : props.community.cohesionScore > 0.5 ? 'moderately connected' : 'loosely connected' }} community.
          </p>
        </div>
      </div>
    </div>

    <!-- Activity Indicators -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">STATUS INDICATORS</h2>
      </div>
      <div class="explorer-card-body space-y-2">
        <div class="flex items-center gap-3 text-sm">
          <div class="w-3 h-3 bg-green-500 rounded-full" />
          <span class="text-neutral-400">Active in last 24 hours</span>
        </div>
        <div class="flex items-center gap-3 text-sm">
          <div class="w-3 h-3 bg-cyan-500 rounded-full" />
          <span class="text-neutral-400">Active within 7 days</span>
        </div>
        <div class="flex items-center gap-3 text-sm">
          <div class="w-3 h-3 bg-yellow-500 rounded-full" />
          <span class="text-neutral-400">Active within 30 days</span>
        </div>
        <div class="flex items-center gap-3 text-sm">
          <div class="w-3 h-3 bg-orange-500 rounded-full" />
          <span class="text-neutral-400">Dormant (30+ days)</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.explorer-card-header {
  padding: 1rem;
  border-bottom: 1px solid var(--portal-border, #1a1a24);
}

.explorer-card-header h2 {
  margin: 0;
  font-size: 0.875rem;
}
</style>
