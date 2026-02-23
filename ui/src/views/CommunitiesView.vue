<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { fetchAllCommunities, triggerCommunityDetection, type PlayerCommunity } from '@/services/playerRelationshipsApi'
import CommunityCard from '@/components/CommunityCard.vue'
import { useAuth } from '@/composables/useAuth'

const communities = ref<PlayerCommunity[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const triggering = ref(false)

const minSize = ref(3)
const activeOnly = ref(true)
const sortBy = ref<'cohesion' | 'members' | 'recent'>('cohesion')
const searchQuery = ref('')

const { isSupport } = useAuth()

const sortedAndFilteredCommunities = computed(() => {
  let result = [...communities.value]

  // Search filter
  if (searchQuery.value.trim()) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(c =>
      c.name.toLowerCase().includes(query) ||
      c.id.toLowerCase().includes(query) ||
      c.members.some(m => m.toLowerCase().includes(query))
    )
  }

  // Sort
  switch (sortBy.value) {
    case 'cohesion':
      result.sort((a, b) => b.cohesionScore - a.cohesionScore)
      break
    case 'members':
      result.sort((a, b) => b.memberCount - a.memberCount)
      break
    case 'recent':
      result.sort((a, b) => new Date(b.lastActiveDate).getTime() - new Date(a.lastActiveDate).getTime())
      break
  }

  return result
})

const stats = computed(() => ({
  totalCommunities: communities.value.length,
  avgCohesion: communities.value.length > 0
    ? (communities.value.reduce((sum, c) => sum + c.cohesionScore, 0) / communities.value.length * 100).toFixed(1)
    : 0,
  totalMembers: communities.value.reduce((sum, c) => sum + c.memberCount, 0),
  activeCommunities: communities.value.filter(c => c.isActive).length
}))

const loadCommunities = async () => {
  loading.value = true
  error.value = null
  try {
    communities.value = await fetchAllCommunities(minSize.value, activeOnly.value)
  } catch (err) {
    error.value = 'Failed to load communities'
    console.error(err)
  } finally {
    loading.value = false
  }
}

const triggerDetection = async () => {
  triggering.value = true
  try {
    const result = await triggerCommunityDetection()
    // Show success message
    await new Promise(resolve => setTimeout(resolve, 1500))
    // Reload communities after detection completes
    await loadCommunities()
  } catch (err) {
    error.value = 'Failed to trigger community detection'
    console.error(err)
  } finally {
    triggering.value = false
  }
}

onMounted(() => {
  loadCommunities()
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
            <h1 class="text-2xl sm:text-3xl font-bold text-[var(--portal-text-bright,#e5e7eb)] font-mono mb-2">
              PLAYER COMMUNITIES
            </h1>
            <p class="text-sm text-neutral-500">
              Explore detected player communities and their connections
            </p>
          </div>

          <!-- Stats Overview -->
          <div class="grid grid-cols-2 sm:grid-cols-4 gap-3 sm:gap-4 mb-6">
            <div class="explorer-stat-card">
              <div class="text-2xl font-bold text-cyan-400 font-mono">{{ stats.totalCommunities }}</div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Communities</div>
            </div>
            <div class="explorer-stat-card">
              <div class="text-2xl font-bold text-green-400 font-mono">{{ stats.activeCommunities }}</div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Active</div>
            </div>
            <div class="explorer-stat-card">
              <div class="text-2xl font-bold text-purple-400 font-mono">{{ stats.avgCohesion }}%</div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Avg Cohesion</div>
            </div>
            <div class="explorer-stat-card">
              <div class="text-2xl font-bold text-yellow-400 font-mono">{{ stats.totalMembers }}</div>
              <div class="text-xs text-neutral-500 uppercase tracking-wider">Total Members</div>
            </div>
          </div>

          <!-- Controls -->
          <div class="explorer-card mb-6">
            <div class="explorer-card-body">
              <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <!-- Search -->
                <div>
                  <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Search</label>
                  <input
                    v-model="searchQuery"
                    type="text"
                    placeholder="Search communities..."
                    class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200 placeholder-neutral-600"
                  />
                </div>

                <!-- Sort -->
                <div>
                  <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Sort By</label>
                  <select
                    v-model="sortBy"
                    class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200"
                  >
                    <option value="cohesion">Cohesion (High to Low)</option>
                    <option value="members">Members (Most to Least)</option>
                    <option value="recent">Recently Active</option>
                  </select>
                </div>

                <!-- Min Size -->
                <div>
                  <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Min Members</label>
                  <select
                    v-model.number="minSize"
                    @change="loadCommunities"
                    class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200"
                  >
                    <option :value="3">3+ members</option>
                    <option :value="5">5+ members</option>
                    <option :value="10">10+ members</option>
                  </select>
                </div>

                <!-- Active Only -->
                <div>
                  <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Status</label>
                  <select
                    v-model="activeOnly"
                    @change="loadCommunities"
                    class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200"
                  >
                    <option :value="true">Active Only</option>
                    <option :value="false">All Communities</option>
                  </select>
                </div>
              </div>
            </div>
          </div>

          <!-- Loading State -->
          <div v-if="loading" class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            <div v-for="i in 6" :key="i" class="animate-pulse bg-neutral-800 h-96 rounded" />
          </div>

          <!-- Error State -->
          <div v-else-if="error" class="explorer-card">
            <div class="explorer-card-body text-center">
              <p class="text-red-400 mb-4">{{ error }}</p>
              <button
                @click="loadCommunities"
                class="px-4 py-2 bg-red-500/20 border border-red-500/50 rounded text-red-400 text-sm hover:bg-red-500/30 transition-colors"
              >
                Try Again
              </button>
            </div>
          </div>

          <!-- Empty State -->
          <div v-else-if="sortedAndFilteredCommunities.length === 0" class="explorer-card">
            <div class="explorer-card-body text-center py-12">
              <div v-if="communities.length === 0">
                <div class="mb-4">
                  <div class="text-5xl mb-4">🔍</div>
                  <h3 class="text-lg font-bold text-neutral-300 mb-2">No Communities Detected Yet</h3>
                  <p class="text-neutral-500 mb-6 max-w-md mx-auto">
                    Communities are detected automatically once per day. Community detection requires player session data to analyze relationships and group players who frequently play together.
                  </p>
                </div>
                <div class="space-y-3">
                  <p class="text-sm text-neutral-600 mb-4">
                    Community detection runs automatically at scheduled intervals. If you're an admin, you can trigger detection manually:
                  </p>
                  <button
                    v-if="isSupport"
                    @click="triggerDetection"
                    :disabled="triggering"
                    class="px-6 py-2 bg-cyan-500/20 border border-cyan-500/50 rounded text-cyan-400 text-sm hover:bg-cyan-500/30 disabled:opacity-50 disabled:cursor-not-allowed transition-colors font-medium"
                  >
                    {{ triggering ? 'Detecting Communities...' : 'Trigger Detection Now' }}
                  </button>
                  <p v-else class="text-xs text-neutral-600 italic">
                    Only admins can manually trigger community detection
                  </p>
                </div>
              </div>
              <div v-else>
                <p class="text-neutral-500 mb-4">No communities found matching your criteria</p>
                <button
                  @click="() => { searchQuery = ''; minSize = 3; activeOnly = true; loadCommunities() }"
                  class="px-4 py-2 bg-neutral-800 border border-neutral-700 rounded text-neutral-300 text-sm hover:bg-neutral-700 transition-colors"
                >
                  Reset Filters
                </button>
              </div>
            </div>
          </div>

          <!-- Communities Grid -->
          <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            <CommunityCard
              v-for="community in sortedAndFilteredCommunities"
              :key="community.id"
              :community="community"
            />
          </div>

          <!-- Results Summary -->
          <div v-if="!loading && sortedAndFilteredCommunities.length > 0" class="mt-6 text-center text-sm text-neutral-500">
            Showing {{ sortedAndFilteredCommunities.length }} of {{ communities.length }} communities
          </div>

        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.explorer-stat-card {
  padding: 1rem;
  background: var(--portal-surface, #0f0f15);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
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

<style src="./portal-layout.css"></style>
<style src="./DataExplorer.vue.css"></style>
