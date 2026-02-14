<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <!-- Hero Section -->
      <div class="w-full rounded-lg border border-[var(--portal-border)] bg-[var(--portal-surface)] mb-6">
        <div class="w-full max-w-screen-2xl mx-auto px-0 sm:px-8 lg:px-12 py-8">
        <!-- Search Input -->
        <div class="flex justify-center px-4 sm:px-0">
          <div class="relative w-full max-w-xl">
            <!-- Search Icon -->
            <div class="absolute left-4 top-1/2 -translate-y-1/2 pointer-events-none">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                width="20"
                height="20"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                class="text-neutral-500"
              >
                <circle cx="11" cy="11" r="8"></circle>
                <path d="m21 21-4.35-4.35"></path>
              </svg>
            </div>

            <!-- Search Input -->
            <input
              ref="searchInputRef"
              v-model="searchQuery"
              type="text"
              placeholder="Search players..."
              class="w-full pl-12 pr-12 py-3.5 bg-[var(--portal-surface-elevated)] border border-[var(--portal-border)] rounded-lg text-[var(--portal-text-bright)] placeholder-[var(--portal-text)] focus:outline-none focus:border-[var(--portal-accent)] transition-colors"
              @input="onInput"
              @keydown.enter.prevent="executeSearchNow"
            >

            <!-- Clear Button / Loading Spinner -->
            <div class="absolute right-3 top-1/2 -translate-y-1/2 flex items-center">
              <div
                v-if="isLoading"
                class="w-5 h-5 border-2 border-slate-600 border-t-cyan-400 rounded-full animate-spin"
              />
              <button
                v-else-if="searchQuery.trim()"
                type="button"
                class="p-1 text-slate-500 hover:text-slate-300 transition-colors"
                title="Clear search"
                @click="clearSearch"
              >
                <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M18 6 6 18"/>
                  <path d="m6 6 12 12"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
        </div>
      </div>

      <!-- Main Content -->
      <div class="w-full max-w-screen-2xl mx-auto px-0 sm:px-8 lg:px-12 py-8">
        <PlayersPage
        ref="playersPageRef"
        :search-query="activeSearchQuery"
        :manual-search="true"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PlayersPage from '../components/PlayersPage.vue'

const route = useRoute()
const router = useRouter()

const searchQuery = ref('')
const activeSearchQuery = ref('')
const searchInputRef = ref<HTMLInputElement | null>(null)
const playersPageRef = ref<InstanceType<typeof PlayersPage> | null>(null)
let debounceTimer: ReturnType<typeof setTimeout> | null = null

const isLoading = computed(() => playersPageRef.value?.loading ?? false)

function initFromRoute() {
  const q = route.query.q
  if (typeof q === 'string' && q.trim()) {
    const trimmed = q.trim()
    searchQuery.value = trimmed
    activeSearchQuery.value = trimmed
  }
}

onMounted(() => {
  initFromRoute()
  searchInputRef.value?.focus()
})

watch(() => route.query.q, () => initFromRoute())

const triggerSearch = (query: string) => {
  const trimmed = query.trim()
  if (!trimmed) {
    activeSearchQuery.value = ''
    router.replace({ path: '/players', query: {} })
    return
  }
  activeSearchQuery.value = trimmed
  router.replace({ path: '/players', query: { ...route.query, q: trimmed } })
}

const onInput = () => {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    triggerSearch(searchQuery.value)
  }, 350)
}

const executeSearchNow = () => {
  if (debounceTimer) clearTimeout(debounceTimer)
  triggerSearch(searchQuery.value)
}

const clearSearch = () => {
  searchQuery.value = ''
  if (debounceTimer) clearTimeout(debounceTimer)
  triggerSearch('')
  searchInputRef.value?.focus()
}
</script>

<style src="./portal-layout.css"></style>
