<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PlayerNetworkGraph from '@/components/PlayerNetworkGraph.vue'
import {
  fetchPlayerNetworkStats,
  fetchPlayerTeammates,
  fetchRecentConnections,
  fetchPotentialConnections,
  type PlayerNetworkStats,
  type PlayerRelationship
} from '@/services/playerRelationshipsApi'
import { formatDistanceToNow } from 'date-fns'

const route = useRoute()
const router = useRouter()

const playerName = computed(() => route.params.playerName as string)

const networkStats = ref<PlayerNetworkStats | null>(null)
const teammates = ref<PlayerRelationship[]>([])
const recentConnectionsList = ref<PlayerRelationship[]>([])
const potentialConnectionsList = ref<string[]>([])

const loadingStats = ref(false)
const loadingTeammates = ref(false)
const loadingRecent = ref(false)
const loadingSuggestions = ref(false)

const formatDate = (dateStr?: string) => {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString()
}

const formatRelativeDate = (dateStr?: string) => {
  if (!dateStr) return ''
  return formatDistanceToNow(new Date(dateStr), { addSuffix: true })
}

const loadNetworkStats = async () => {
  loadingStats.value = true
  try {
    networkStats.value = await fetchPlayerNetworkStats(playerName.value)
  } catch {
    console.error('Failed to fetch network stats')
  } finally {
    loadingStats.value = false
  }
}

const loadTeammates = async () => {
  loadingTeammates.value = true
  try {
    teammates.value = await fetchPlayerTeammates(playerName.value)
  } catch {
    console.error('Failed to fetch teammates')
  } finally {
    loadingTeammates.value = false
  }
}

const loadRecentConnections = async () => {
  loadingRecent.value = true
  try {
    recentConnectionsList.value = await fetchRecentConnections(playerName.value, 30)
  } catch {
    console.error('Failed to fetch recent connections')
  } finally {
    loadingRecent.value = false
  }
}

const loadPotentialConnections = async () => {
  loadingSuggestions.value = true
  try {
    potentialConnectionsList.value = await fetchPotentialConnections(playerName.value, 20, 30)
  } catch {
    console.error('Failed to fetch potential connections')
  } finally {
    loadingSuggestions.value = false
  }
}

onMounted(() => {
  loadNetworkStats()
  loadTeammates()
  loadRecentConnections()
  loadPotentialConnections()
})
</script>

<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <div class="data-explorer">
        <div class="explorer-inner">

          <!-- Header -->
          <div class="mb-6">
            <button class="explorer-btn explorer-btn--ghost explorer-btn--sm mb-4 flex items-center gap-2" @click="router.push(`/players/${encodeURIComponent(playerName)}`)">
              &larr; BACK TO PROFILE
            </button>
            <h1 class="text-2xl sm:text-3xl font-bold text-[var(--portal-text-bright,#e5e7eb)] font-mono">
              NETWORK: {{ playerName }}
            </h1>
            <p class="text-sm text-neutral-500 mt-1">
              Explore connections and relationships between players
            </p>
          </div>

          <div class="grid grid-cols-1 lg:grid-cols-4 gap-4 sm:gap-6">
            <!-- Sidebar -->
            <div class="lg:col-span-1 space-y-4 sm:space-y-6">

              <!-- Network Stats -->
              <div class="explorer-card">
                <div class="explorer-card-header">
                  <h2 class="explorer-card-title">NETWORK STATISTICS</h2>
                </div>
                <div class="explorer-card-body">
                  <div v-if="loadingStats" class="space-y-3">
                    <div v-for="i in 4" :key="i" class="animate-pulse bg-neutral-800 h-16 rounded" />
                  </div>
                  <div v-else-if="networkStats" class="space-y-3">
                    <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/30">
                      <div class="text-2xl font-bold text-cyan-400 font-mono">{{ networkStats.connectionCount }}</div>
                      <div class="text-xs text-neutral-500 uppercase tracking-wider">Total Connections</div>
                    </div>
                    <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/30">
                      <div class="text-2xl font-bold text-[var(--portal-accent,#00e5a0)] font-mono">{{ networkStats.connectionCount > 0 ? Math.round(networkStats.totalCoPlaySessions / networkStats.connectionCount) : 0 }}</div>
                      <div class="text-xs text-neutral-500 uppercase tracking-wider">Avg Overlap / Teammate</div>
                    </div>
                    <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/30">
                      <div class="text-2xl font-bold text-purple-400 font-mono">{{ networkStats.serverCount }}</div>
                      <div class="text-xs text-neutral-500 uppercase tracking-wider">Servers Played</div>
                    </div>
                    <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/30">
                      <div class="text-xs text-neutral-500 uppercase tracking-wider">Active Since</div>
                      <div class="text-sm font-medium text-neutral-300 mt-1">{{ formatDate(networkStats.firstSeen) }}</div>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Recent Connections -->
              <div class="explorer-card">
                <div class="explorer-card-header">
                  <h2 class="explorer-card-title">RECENT CONNECTIONS</h2>
                </div>
                <div class="explorer-card-body">
                  <div v-if="loadingRecent" class="space-y-2">
                    <div v-for="i in 5" :key="i" class="animate-pulse bg-neutral-800 h-12 rounded" />
                  </div>
                  <div v-else-if="recentConnectionsList.length > 0" class="space-y-2">
                    <router-link
                      v-for="conn in recentConnectionsList.slice(0, 5)"
                      :key="conn.player2Name"
                      :to="`/players/${encodeURIComponent(conn.player2Name)}`"
                      class="block p-2.5 bg-neutral-800/40 hover:bg-neutral-700/40 rounded border border-transparent hover:border-neutral-600/50 transition-colors"
                    >
                      <div class="font-medium text-sm text-neutral-200">{{ conn.player2Name }}</div>
                      <div class="text-xs text-neutral-500 mt-0.5">
                        First played {{ formatRelativeDate(conn.firstPlayedTogether) }}
                      </div>
                    </router-link>
                  </div>
                  <div v-else class="text-neutral-500 text-sm text-center py-4">
                    No recent connections
                  </div>
                </div>
              </div>
            </div>

            <!-- Main Content -->
            <div class="lg:col-span-3 space-y-4 sm:space-y-6">

              <!-- Network Graph -->
              <div class="explorer-card network-graph-card">
                <div class="explorer-card-header hidden sm:block">
                  <h2 class="explorer-card-title">NETWORK VISUALIZATION</h2>
                </div>
                <div class="explorer-card-body p-0">
                  <PlayerNetworkGraph :player-name="playerName" />
                </div>
              </div>

              <!-- Most Frequent Teammates -->
              <div class="explorer-card">
                <div class="explorer-card-header">
                  <h2 class="explorer-card-title">MOST FREQUENT TEAMMATES</h2>
                </div>
                <div class="explorer-card-body">
                  <div v-if="loadingTeammates" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                    <div v-for="i in 6" :key="i" class="animate-pulse bg-neutral-800 h-24 rounded" />
                  </div>
                  <div v-else-if="teammates.length > 0" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                    <div
                      v-for="teammate in teammates.slice(0, 9)"
                      :key="teammate.player2Name"
                      class="p-3 bg-neutral-800/40 rounded-lg border border-neutral-700/30"
                    >
                      <router-link
                        :to="`/players/${encodeURIComponent(teammate.player2Name)}`"
                        class="font-medium text-sm text-cyan-400 hover:text-cyan-300 transition-colors"
                      >
                        {{ teammate.player2Name }}
                      </router-link>
                      <div class="mt-2 space-y-1 text-xs">
                        <div class="flex justify-between">
                          <span class="text-neutral-500">Sessions</span>
                          <span class="font-mono text-neutral-300">{{ teammate.sessionCount }}</span>
                        </div>
                        <div class="flex justify-between">
                          <span class="text-neutral-500">Last played</span>
                          <span class="text-neutral-400">{{ formatRelativeDate(teammate.lastPlayedTogether) }}</span>
                        </div>
                        <div class="text-neutral-600 mt-1.5">
                          {{ teammate.serverGuids.length }} server{{ teammate.serverGuids.length !== 1 ? 's' : '' }}
                        </div>
                      </div>
                    </div>
                  </div>
                  <div v-else class="text-neutral-500 text-sm text-center py-6">
                    No teammates found
                  </div>
                </div>
              </div>

              <!-- Potential Connections -->
              <div class="explorer-card">
                <div class="explorer-card-header flex items-center justify-between">
                  <h2 class="explorer-card-title">SUGGESTED SQUAD MATES</h2>
                  <span class="text-[10px] text-neutral-500 font-mono">SAME SERVERS, HAVEN'T MET</span>
                </div>
                <div class="explorer-card-body">
                  <div v-if="loadingSuggestions" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2">
                    <div v-for="i in 8" :key="i" class="animate-pulse bg-neutral-800 h-9 rounded" />
                  </div>
                  <div v-else-if="potentialConnectionsList.length > 0" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-2">
                    <router-link
                      v-for="player in potentialConnectionsList"
                      :key="player"
                      :to="`/players/${encodeURIComponent(player)}`"
                      class="px-3 py-2 bg-neutral-800/40 hover:bg-neutral-700/40 rounded text-center text-sm text-neutral-300 hover:text-neutral-100 border border-transparent hover:border-neutral-600/50 transition-colors"
                    >
                      {{ player }}
                    </router-link>
                  </div>
                  <div v-else class="text-neutral-500 text-sm text-center py-6">
                    No suggestions available
                  </div>
                </div>
              </div>
            </div>
          </div>

        </div>
      </div>
    </div>
  </div>
</template>

<style src="./portal-layout.css"></style>
<style scoped src="./DataExplorer.vue.css"></style>
<style scoped>
/* Mobile optimizations for network view */
@media (max-width: 767px) {
  /* Remove all padding on mobile */
  .portal-page .portal-inner {
    padding: 0;
  }
  
  .explorer-inner {
    padding: 0.5rem;
    padding-bottom: 1rem;
  }
  
  /* Stack layout on mobile */
  .grid {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  
  /* Compact sidebar cards on mobile */
  .lg\\:col-span-1 .explorer-card {
    margin-bottom: 0.5rem;
  }
  
  .lg\\:col-span-1 .explorer-card-header {
    padding: 0.5rem 0.75rem;
  }
  
  .lg\\:col-span-1 .explorer-card-title {
    font-size: 0.65rem;
  }
  
  .lg\\:col-span-1 .explorer-card-body {
    max-height: 120px;
    overflow-y: auto;
    padding: 0.5rem;
  }
  
  /* Network graph takes remaining space */
  .network-graph-card {
    border-radius: 0;
    border-left: none;
    border-right: none;
    margin: 0 -0.5rem;
  }
  
  .network-graph-card .explorer-card-body {
    min-height: 450px;
    padding: 0;
  }
}

/* Tablet optimizations */
@media (min-width: 768px) and (max-width: 1023px) {
  .network-graph-card .explorer-card-body {
    min-height: 500px;
  }
}
</style>
