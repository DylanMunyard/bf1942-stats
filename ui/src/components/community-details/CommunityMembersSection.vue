<script setup lang="ts">
import { ref, computed } from 'vue'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const searchQuery = ref('')
const sortBy = ref<'name' | 'sessions'>('name')

const filteredMembers = computed(() => {
  let result = [...props.community.members]

  if (searchQuery.value.trim()) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(member => member.toLowerCase().includes(query))
  }

  switch (sortBy.value) {
    case 'sessions':
      // Note: We don't have per-member session count, so keep original order
      break
    case 'name':
    default:
      result.sort((a, b) => a.localeCompare(b))
  }

  return result
})

const coreSet = new Set(props.community.coreMembers)

const isCoreHelper = (memberName: string) => coreSet.has(memberName)
</script>

<template>
  <div class="space-y-4">
    <!-- Search and Sort -->
    <div class="explorer-card">
      <div class="explorer-card-body">
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Search</label>
            <input
              v-model="searchQuery"
              type="text"
              placeholder="Search members..."
              class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200 placeholder-neutral-600"
            />
          </div>
          <div>
            <label class="block text-xs text-neutral-500 uppercase tracking-wider mb-2">Sort By</label>
            <select
              v-model="sortBy"
              class="w-full px-3 py-2 bg-neutral-800/50 border border-neutral-700 rounded text-sm text-neutral-200"
            >
              <option value="name">Name (A-Z)</option>
              <option value="sessions">Core Members First</option>
            </select>
          </div>
        </div>
      </div>
    </div>

    <!-- Members List -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">
          ALL MEMBERS ({{ filteredMembers.length }}/{{ props.community.memberCount }})
        </h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-2">
          <router-link
            v-for="member in filteredMembers"
            :key="member"
            :to="`/players/${encodeURIComponent(member)}`"
            class="flex items-center justify-between p-3 bg-neutral-800/30 rounded hover:bg-neutral-700/30 transition-colors group"
          >
            <div class="flex items-center gap-3 min-w-0 flex-1">
              <div
                v-if="isCoreHelper(member)"
                class="flex-shrink-0 w-2 h-2 rounded-full bg-cyan-400"
                title="Core member"
              />
              <div v-else class="flex-shrink-0 w-2 h-2" />
              <span class="text-sm text-neutral-300 group-hover:text-cyan-400 transition-colors truncate">
                {{ member }}
              </span>
            </div>
            <div
              v-if="isCoreHelper(member)"
              class="flex-shrink-0 text-xs px-2 py-1 bg-cyan-500/20 border border-cyan-500/50 rounded text-cyan-300"
            >
              Core
            </div>
          </router-link>
        </div>

        <!-- Empty State -->
        <div v-if="filteredMembers.length === 0" class="text-center py-8">
          <p class="text-neutral-500 text-sm">No members found matching your search</p>
        </div>
      </div>
    </div>

    <!-- Stats -->
    <div class="grid grid-cols-2 gap-4">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Core Members</div>
          <div class="text-2xl font-bold text-cyan-400 font-mono">{{ props.community.coreMembers.length }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Regular Members</div>
          <div class="text-2xl font-bold text-green-400 font-mono">
            {{ props.community.memberCount - props.community.coreMembers.length }}
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
