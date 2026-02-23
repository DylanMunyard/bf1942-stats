<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { fetchCommunity, type PlayerCommunity } from '@/services/playerRelationshipsApi'
import CommunityMembersSection from '@/components/community-details/CommunityMembersSection.vue'
import CommunityServersSection from '@/components/community-details/CommunityServersSection.vue'
import CommunityNetworkGraph from '@/components/community-details/CommunityNetworkGraph.vue'
import CommunityActivityTimeline from '@/components/community-details/CommunityActivityTimeline.vue'

const route = useRoute()
const router = useRouter()

const community = ref<PlayerCommunity | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const activeTab = ref<'overview' | 'members' | 'servers' | 'network' | 'activity'>('overview')

const cohesionPercentage = computed(() =>
  community.value ? Math.round(community.value.cohesionScore * 100) : 0
)

const statusColor = computed(() => {
  if (!community.value?.isActive) return 'text-neutral-500'
  if (community.value.cohesionScore >= 0.7) return 'text-cyan-400'
  if (community.value.cohesionScore >= 0.5) return 'text-green-400'
  return 'text-yellow-400'
})

const statusLabel = computed(() => {
  if (!community.value?.isActive) return 'INACTIVE'
  if (community.value.cohesionScore >= 0.7) return 'TIGHT-KNIT'
  if (community.value.cohesionScore >= 0.5) return 'ACTIVE'
  return 'EMERGING'
})

const formatDate = (dateStr: string) => new Date(dateStr).toLocaleDateString()

const loadCommunity = async () => {
  loading.value = true
  error.value = null
  try {
    const communityId = route.params.id as string
    community.value = await fetchCommunity(decodeURIComponent(communityId))
  } catch (err) {
    error.value = 'Failed to load community details'
    console.error(err)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadCommunity()
})
</script>

<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <div class="data-explorer">
        <div class="explorer-inner">

          <!-- Back Button -->
          <router-link to="/communities" class="back-button">
            ← Back to Communities
          </router-link>

          <!-- Loading State -->
          <div v-if="loading" class="loading-skeleton">
            <div class="animate-pulse space-y-4">
              <div class="h-12 bg-neutral-800 rounded w-3/4" />
              <div class="h-6 bg-neutral-800 rounded w-1/2" />
              <div class="grid grid-cols-4 gap-4 mt-6">
                <div v-for="i in 4" :key="i" class="h-24 bg-neutral-800 rounded" />
              </div>
            </div>
          </div>

          <!-- Error State -->
          <div v-else-if="error" class="explorer-card">
            <div class="explorer-card-body text-center">
              <p class="text-red-400 mb-4">{{ error }}</p>
              <button
                @click="loadCommunity"
                class="px-4 py-2 bg-red-500/20 border border-red-500/50 rounded text-red-400 text-sm hover:bg-red-500/30 transition-colors"
              >
                Try Again
              </button>
            </div>
          </div>

          <!-- Community Details -->
          <div v-else-if="community">
            <!-- Header -->
            <div class="mb-6">
              <div class="flex items-start justify-between gap-4 mb-4">
                <div class="flex-1 min-w-0">
                  <h1 class="text-3xl font-bold text-[var(--portal-text-bright,#e5e7eb)] font-mono mb-1">
                    {{ community.name }}
                  </h1>
                  <p class="text-sm text-neutral-500 font-mono">
                    {{ community.id }}
                  </p>
                </div>
                <div class="status-badge" :class="statusColor">
                  {{ statusLabel }}
                </div>
              </div>

              <p class="text-neutral-400 text-sm">
                Community detected and analyzed for player relationships and group dynamics
              </p>
            </div>

            <!-- Stats Grid -->
            <div class="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4 mb-6">
              <div class="explorer-stat-card">
                <div class="text-2xl font-bold text-cyan-400 font-mono">{{ community.memberCount }}</div>
                <div class="text-xs text-neutral-500 uppercase tracking-wider">Total Members</div>
              </div>
              <div class="explorer-stat-card">
                <div class="text-2xl font-bold text-purple-400 font-mono">{{ cohesionPercentage }}%</div>
                <div class="text-xs text-neutral-500 uppercase tracking-wider">Cohesion</div>
              </div>
              <div class="explorer-stat-card">
                <div class="text-2xl font-bold text-green-400 font-mono">{{ community.avgSessionsPerPair.toFixed(1) }}</div>
                <div class="text-xs text-neutral-500 uppercase tracking-wider">Avg Sessions</div>
              </div>
              <div class="explorer-stat-card">
                <div class="text-2xl font-bold text-yellow-400 font-mono">{{ community.primaryServers.length }}</div>
                <div class="text-xs text-neutral-500 uppercase tracking-wider">Servers</div>
              </div>
            </div>

            <!-- Key Dates -->
            <div class="explorer-card mb-6">
              <div class="explorer-card-body">
                <div class="grid grid-cols-2 gap-4">
                  <div>
                    <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Formation Date</div>
                    <div class="text-sm text-neutral-200">{{ formatDate(community.formationDate) }}</div>
                  </div>
                  <div>
                    <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Last Active</div>
                    <div class="text-sm text-neutral-200">{{ formatDate(community.lastActiveDate) }}</div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Tab Navigation -->
            <div class="explorer-card mb-6 overflow-x-auto">
              <div class="explorer-card-body">
                <div class="flex gap-2 whitespace-nowrap">
                  <button
                    v-for="tab in ['overview', 'members', 'servers', 'network', 'activity']"
                    :key="tab"
                    @click="activeTab = tab as any"
                    class="px-4 py-2 rounded text-sm font-medium transition-colors"
                    :class="activeTab === tab
                      ? 'bg-cyan-500/30 border border-cyan-500 text-cyan-300'
                      : 'bg-neutral-800/50 border border-neutral-700 text-neutral-400 hover:text-neutral-300'"
                  >
                    {{ tab.charAt(0).toUpperCase() + tab.slice(1) }}
                  </button>
                </div>
              </div>
            </div>

            <!-- Tab Content -->
            <div class="space-y-6">
              <!-- Overview Tab -->
              <div v-if="activeTab === 'overview'" class="space-y-6">
                <div class="explorer-card">
                  <div class="explorer-card-header">
                    <h2 class="font-mono font-bold text-cyan-300">CORE MEMBERS</h2>
                  </div>
                  <div class="explorer-card-body">
                    <div class="flex flex-wrap gap-2">
                      <router-link
                        v-for="member in community.coreMembers"
                        :key="member"
                        :to="`/players/${encodeURIComponent(member)}`"
                        class="px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-cyan-400 hover:bg-neutral-700 hover:border-cyan-500 transition-colors"
                      >
                        {{ member }}
                      </router-link>
                    </div>
                  </div>
                </div>

                <div class="explorer-card">
                  <div class="explorer-card-header">
                    <h2 class="font-mono font-bold text-cyan-300">PRIMARY SERVERS</h2>
                  </div>
                  <div class="explorer-card-body">
                    <div class="space-y-2">
                      <div
                        v-for="(server, idx) in community.primaryServers.slice(0, 10)"
                        :key="idx"
                        class="flex items-center justify-between p-2 bg-neutral-800/30 rounded text-sm"
                      >
                        <span class="text-neutral-300">{{ server }}</span>
                      </div>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Members Tab -->
              <CommunityMembersSection v-else-if="activeTab === 'members'" :community="community" />

              <!-- Servers Tab -->
              <CommunityServersSection v-else-if="activeTab === 'servers'" :community="community" />

              <!-- Network Tab -->
              <CommunityNetworkGraph v-else-if="activeTab === 'network'" :community="community" />

              <!-- Activity Tab -->
              <CommunityActivityTimeline v-else-if="activeTab === 'activity'" :community="community" />
            </div>

          </div>

        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.back-button {
  display: inline-flex;
  align-items: center;
  padding: 0.5rem 1rem;
  margin-bottom: 1.5rem;
  background: var(--portal-surface, #0f0f15);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 6px;
  font-size: 0.875rem;
  color: var(--portal-accent, #00e5a0);
  text-decoration: none;
  transition: all 0.2s ease;
}

.back-button:hover {
  border-color: var(--portal-accent, #00e5a0);
  background: var(--portal-surface-hover, #13131b);
}

.loading-skeleton {
  padding: 2rem;
}

.explorer-stat-card {
  padding: 1rem;
  background: var(--portal-surface, #0f0f15);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  padding: 0.5rem 1rem;
  background: var(--portal-surface-elevated, #111118);
  border: 1px solid currentColor;
  border-radius: 6px;
  font-size: 0.875rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  white-space: nowrap;
  flex-shrink: 0;
}

.explorer-card-header {
  padding: 1rem;
  border-bottom: 1px solid var(--portal-border, #1a1a24);
}

.explorer-card-header h2 {
  margin: 0;
  font-size: 0.875rem;
}

@media (max-width: 640px) {
  .explorer-stat-card {
    padding: 0.75rem;
  }

  .explorer-stat-card > div:first-child {
    font-size: 1.5rem;
  }

  .explorer-stat-card > div:last-child {
    font-size: 0.6rem;
  }
}
</style>

<style src="../views/portal-layout.css"></style>
<style src="../views/DataExplorer.vue.css"></style>
