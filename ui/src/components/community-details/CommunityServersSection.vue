<script setup lang="ts">
import { ref, computed } from 'vue'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const searchQuery = ref('')

const filteredServers = computed(() => {
  if (!searchQuery.value.trim()) {
    return props.community.primaryServers
  }

  const query = searchQuery.value.toLowerCase()
  return props.community.primaryServers.filter(server =>
    server.toLowerCase().includes(query)
  )
})

const isPrimaryHelper = (server: string) => {
  return props.community.primaryServers.indexOf(server) < 5
}
</script>

<template>
  <div class="space-y-4">
    <!-- Search -->
    <div class="explorer-card">
      <div class="explorer-card-body">
        <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Search Servers</label>
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Filter by server name..."
          class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200 placeholder-neutral-600"
        />
      </div>
    </div>

    <!-- Servers List -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">
          COMMUNITY SERVERS ({{ filteredServers.length }})
        </h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-2">
          <div
            v-for="(server, idx) in filteredServers"
            :key="idx"
            class="flex items-center justify-between p-3 bg-neutral-800/30 rounded hover:bg-neutral-700/30 transition-colors group"
          >
            <div class="flex items-center gap-3 min-w-0 flex-1">
              <div
                :class="isPrimaryHelper(server)
                  ? 'w-2 h-2 rounded-full bg-purple-400'
                  : 'w-2 h-2 rounded-full bg-neutral-600'"
                :title="isPrimaryHelper(server) ? 'Primary server' : 'Secondary server'"
              />
              <span class="text-sm text-neutral-300 group-hover:text-cyan-400 transition-colors truncate">
                {{ server }}
              </span>
            </div>
            <div v-if="isPrimaryHelper(server)" class="flex-shrink-0 text-xs px-2 py-1 bg-purple-500/20 border border-purple-500/50 rounded text-purple-300">
              Primary
            </div>
          </div>
        </div>

        <!-- Empty State -->
        <div v-if="filteredServers.length === 0" class="text-center py-8">
          <p class="text-neutral-500 text-sm">No servers found matching your search</p>
        </div>
      </div>
    </div>

    <!-- Server Distribution -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">DISTRIBUTION</h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-3">
          <div>
            <div class="flex justify-between items-center mb-2">
              <span class="text-xs text-neutral-500 uppercase tracking-wider">Primary Servers</span>
              <span class="text-sm font-mono text-purple-400">{{ Math.min(5, props.community.primaryServers.length) }}</span>
            </div>
            <div class="h-2 bg-neutral-800 rounded-full overflow-hidden">
              <div
                class="h-full bg-purple-500"
                :style="{ width: `${(Math.min(5, props.community.primaryServers.length) / props.community.primaryServers.length) * 100}%` }"
              />
            </div>
          </div>

          <div>
            <div class="flex justify-between items-center mb-2">
              <span class="text-xs text-neutral-500 uppercase tracking-wider">All Servers</span>
              <span class="text-sm font-mono text-cyan-400">{{ props.community.primaryServers.length }}</span>
            </div>
            <div class="text-xs text-neutral-500 mt-2">
              The community plays across {{ props.community.primaryServers.length }} different servers,
              with the top {{ Math.min(5, props.community.primaryServers.length) }} being their primary hangouts.
            </div>
          </div>
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
