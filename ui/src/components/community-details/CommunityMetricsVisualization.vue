<script setup lang="ts">
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const getCohesionColor = (score: number) => {
  if (score >= 0.8) return 'from-cyan-500 to-cyan-400'
  if (score >= 0.6) return 'from-green-500 to-green-400'
  if (score >= 0.4) return 'from-yellow-500 to-yellow-400'
  return 'from-orange-500 to-orange-400'
}

const getCohesionLabel = (score: number) => {
  if (score >= 0.8) return 'Very Tight'
  if (score >= 0.6) return 'Tight'
  if (score >= 0.4) return 'Moderate'
  return 'Loose'
}

const getDensityLevel = (avgSessions: number) => {
  if (avgSessions >= 10) return 'Very High'
  if (avgSessions >= 7) return 'High'
  if (avgSessions >= 4) return 'Moderate'
  return 'Low'
}

const getCoreRatio = () => {
  return props.community.memberCount > 0
    ? ((props.community.coreMembers.length / props.community.memberCount) * 100).toFixed(1)
    : '0'
}
</script>

<template>
  <div class="space-y-6">
    <!-- Main Metrics Grid -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <!-- Cohesion Score -->
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-end justify-between mb-4">
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Cohesion Score</div>
              <div class="text-3xl font-bold text-neutral-200 font-mono">
                {{ (community.cohesionScore * 100).toFixed(0) }}%
              </div>
            </div>
            <div class="text-sm text-neutral-400">{{ getCohesionLabel(community.cohesionScore) }}</div>
          </div>

          <!-- Cohesion Bar -->
          <div class="space-y-2">
            <div class="h-3 bg-neutral-800 rounded-full overflow-hidden">
              <div
                :class="`h-full bg-gradient-to-r ${getCohesionColor(community.cohesionScore)} rounded-full transition-all duration-500`"
                :style="{ width: `${community.cohesionScore * 100}%` }"
              />
            </div>
            <div class="flex justify-between text-xs text-neutral-500">
              <span>Loose (0%)</span>
              <span>Tight (100%)</span>
            </div>
          </div>

          <!-- Description -->
          <p class="text-xs text-neutral-400 mt-4">
            {{ community.cohesionScore >= 0.7
              ? 'This is a very tight-knit group with strong connections'
              : community.cohesionScore >= 0.5
                ? 'This community has good internal connections'
                : 'This community is still forming with emerging connections'
            }}
          </p>
        </div>
      </div>

      <!-- Connection Density -->
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-end justify-between mb-4">
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Avg Sessions/Pair</div>
              <div class="text-3xl font-bold text-neutral-200 font-mono">
                {{ community.avgSessionsPerPair.toFixed(1) }}
              </div>
            </div>
            <div class="text-sm text-neutral-400">{{ getDensityLevel(community.avgSessionsPerPair) }}</div>
          </div>

          <!-- Density Bar -->
          <div class="space-y-2">
            <div class="h-3 bg-neutral-800 rounded-full overflow-hidden">
              <div
                class="h-full bg-gradient-to-r from-purple-500 to-purple-400 rounded-full transition-all duration-500"
                :style="{ width: `${Math.min(community.avgSessionsPerPair / 15 * 100, 100)}%` }"
              />
            </div>
            <div class="flex justify-between text-xs text-neutral-500">
              <span>Low (1)</span>
              <span>High (15+)</span>
            </div>
          </div>

          <!-- Description -->
          <p class="text-xs text-neutral-400 mt-4">
            Members have played together an average of <strong>{{ community.avgSessionsPerPair.toFixed(1) }}</strong> times
          </p>
        </div>
      </div>
    </div>

    <!-- Member Composition -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">MEMBER COMPOSITION</h2>
      </div>
      <div class="explorer-card-body space-y-4">
        <div>
          <div class="flex items-end justify-between mb-3">
            <div class="text-sm text-neutral-400">Core Members</div>
            <div class="text-lg font-mono font-bold text-cyan-400">{{ community.coreMembers.length }} / {{ community.memberCount }}</div>
          </div>
          <div class="h-4 bg-neutral-800 rounded-full overflow-hidden">
            <div
              class="h-full bg-gradient-to-r from-cyan-500 to-cyan-400 rounded-full"
              :style="{ width: `${getCoreRatio()}%` }"
            />
          </div>
          <p class="text-xs text-neutral-500 mt-2">
            {{ getCoreRatio() }}% of members are core/highly connected players
          </p>
        </div>

        <div>
          <div class="flex items-end justify-between mb-3">
            <div class="text-sm text-neutral-400">Regular Members</div>
            <div class="text-lg font-mono font-bold text-green-400">{{ community.memberCount - community.coreMembers.length }}</div>
          </div>
          <div class="h-4 bg-neutral-800 rounded-full overflow-hidden">
            <div
              class="h-full bg-gradient-to-r from-green-500 to-green-400 rounded-full"
              :style="{ width: `${100 - parseFloat(getCoreRatio())}%` }"
            />
          </div>
          <p class="text-xs text-neutral-500 mt-2">
            {{ 100 - parseFloat(getCoreRatio()) }}% are regular/emerging members
          </p>
        </div>
      </div>
    </div>

    <!-- Health Summary -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">COMMUNITY HEALTH</h2>
      </div>
      <div class="explorer-card-body space-y-3">
        <div class="grid grid-cols-3 gap-2">
          <div class="text-center p-3 bg-neutral-800/50 rounded">
            <div class="text-2xl mb-1">👥</div>
            <div class="text-xs text-neutral-500">Size</div>
            <div class="text-sm font-bold text-neutral-300 mt-1">{{ community.memberCount }}</div>
          </div>
          <div class="text-center p-3 bg-neutral-800/50 rounded">
            <div class="text-2xl mb-1">🔗</div>
            <div class="text-xs text-neutral-500">Density</div>
            <div class="text-sm font-bold text-neutral-300 mt-1">{{ getDensityLevel(community.avgSessionsPerPair) }}</div>
          </div>
          <div class="text-center p-3 bg-neutral-800/50 rounded">
            <div class="text-2xl mb-1">✨</div>
            <div class="text-xs text-neutral-500">Status</div>
            <div class="text-sm font-bold text-neutral-300 mt-1">{{ community.isActive ? 'Active' : 'Dormant' }}</div>
          </div>
        </div>

        <div class="p-3 bg-neutral-800/30 rounded border border-neutral-700/50 text-sm text-neutral-300">
          <p>
            This {{ community.cohesionScore >= 0.7 ? 'tight-knit' : 'developing' }} community of {{ community.memberCount }} players
            with {{ getDensityLevel(community.avgSessionsPerPair).toLowerCase() }} density interaction.
            {{ community.isActive ? 'The community is currently active.' : 'The community is currently dormant.' }}
          </p>
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
