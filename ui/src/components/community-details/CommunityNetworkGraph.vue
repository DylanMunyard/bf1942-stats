<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const loading = ref(true)
const networkNodes = ref<Array<{ id: string; connections: number }>>([])
const highlightedNode = ref<string | null>(null)

onMounted(() => {
  // Simulate network calculation - in a real implementation this would
  // analyze relationships between community members
  setTimeout(() => {
    // For now, create a simple node list based on member prominence
    // Core members get higher connection scores, others get lower
    const nodes = props.community.members.map(member => ({
      id: member,
      connections: props.community.coreMembers.includes(member)
        ? Math.floor(Math.random() * 10) + 8
        : Math.floor(Math.random() * 6) + 2
    }))

    // Sort by connections (descending)
    nodes.sort((a, b) => b.connections - a.connections)
    networkNodes.value = nodes

    loading.value = false
  }, 300)
})

const hubPlayers = computed(() =>
  networkNodes.value.slice(0, 10)
)

const avgConnections = computed(() =>
  networkNodes.value.length > 0
    ? (networkNodes.value.reduce((sum, n) => sum + n.connections, 0) / networkNodes.value.length).toFixed(1)
    : '0'
)

const maxConnections = computed(() =>
  networkNodes.value.length > 0
    ? Math.max(...networkNodes.value.map(n => n.connections))
    : 0
)

const getNormalizedWidth = (connections: number) => {
  return ((connections / (maxConnections.value || 1)) * 100)
}

const getConnectionColor = (connections: number) => {
  const ratio = connections / (maxConnections.value || 1)
  if (ratio > 0.75) return 'bg-cyan-500'
  if (ratio > 0.5) return 'bg-green-500'
  if (ratio > 0.25) return 'bg-yellow-500'
  return 'bg-neutral-500'
}
</script>

<template>
  <div class="space-y-4">
    <!-- Note: Full interactive graph visualization would use D3.js or vis.js -->
    <div class="explorer-card">
      <div class="explorer-card-body">
        <div class="bg-neutral-900 rounded border border-neutral-700 p-4 mb-4">
          <p class="text-xs text-neutral-500 mb-2">📊 NETWORK VISUALIZATION</p>
          <p class="text-sm text-neutral-400">
            This community has <strong>{{ props.community.memberCount }}</strong> members with
            <strong>{{ props.community.avgSessionsPerPair.toFixed(1) }}</strong> average sessions per pair.
            A full interactive graph would be rendered here using D3.js or vis.js.
          </p>
        </div>
      </div>
    </div>

    <!-- Hub Players (Most Connected) -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">HUB PLAYERS (Most Connected)</h2>
      </div>
      <div class="explorer-card-body">
        <div v-if="loading" class="space-y-2">
          <div v-for="i in 5" :key="i" class="h-8 bg-neutral-800/50 rounded animate-pulse" />
        </div>

        <div v-else class="space-y-3">
          <div
            v-for="(node, idx) in hubPlayers"
            :key="node.id"
            class="space-y-1 p-3 bg-neutral-800/30 rounded hover:bg-neutral-700/30 transition-colors"
            @mouseenter="highlightedNode = node.id"
            @mouseleave="highlightedNode = null"
          >
            <div class="flex items-center justify-between">
              <router-link
                :to="`/players/${encodeURIComponent(node.id)}`"
                class="flex items-center gap-2 text-sm text-cyan-400 hover:text-cyan-300 transition-colors"
              >
                <span class="font-bold text-neutral-500 w-6">{{ idx + 1 }}.</span>
                <span>{{ node.id }}</span>
              </router-link>
              <span class="text-xs font-mono text-neutral-400">{{ node.connections }} connections</span>
            </div>
            <div class="h-2 bg-neutral-800 rounded-full overflow-hidden">
              <div
                :class="getConnectionColor(node.connections)"
                class="h-full transition-all duration-300"
                :style="{ width: `${getNormalizedWidth(node.connections)}%` }"
              />
            </div>
          </div>
        </div>

        <div v-if="!loading && hubPlayers.length > 0" class="mt-4 pt-3 border-t border-neutral-700">
          <div class="grid grid-cols-2 gap-4 text-sm">
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Max Connections</div>
              <div class="text-lg font-mono font-bold text-cyan-400">{{ maxConnections }}</div>
            </div>
            <div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Avg Connections</div>
              <div class="text-lg font-mono font-bold text-green-400">{{ avgConnections }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Network Stats -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Cohesion Score</div>
          <div class="text-2xl font-bold text-purple-400 font-mono">{{ (props.community.cohesionScore * 100).toFixed(0) }}%</div>
          <div class="text-xs text-neutral-500 mt-2">
            How tightly connected the community is overall
          </div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Network Density</div>
          <div class="text-2xl font-bold text-green-400 font-mono">
            {{ ((props.community.avgSessionsPerPair / 10) * 100).toFixed(0) }}%
          </div>
          <div class="text-xs text-neutral-500 mt-2">
            Average interaction frequency between members
          </div>
        </div>
      </div>
    </div>

    <!-- Network Info -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">NETWORK INFO</h2>
      </div>
      <div class="explorer-card-body space-y-3">
        <div>
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Core Members</div>
          <p class="text-sm text-neutral-300">
            {{ props.community.coreMembers.length }} core members form the backbone of this community
          </p>
        </div>
        <div>
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Community Type</div>
          <p class="text-sm text-neutral-300">
            {{ props.community.cohesionScore > 0.7 ? 'Tight-knit squad' : props.community.cohesionScore > 0.5 ? 'Active community' : 'Emerging group' }}
          </p>
        </div>
        <div>
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Avg Sessions Per Pair</div>
          <p class="text-sm text-neutral-300">
            Members play together {{ props.community.avgSessionsPerPair.toFixed(1) }} times on average
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
