<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <div class="data-explorer">
        <div class="explorer-inner">

          <!-- Header -->
          <div class="mb-6">
            <h1 class="text-2xl sm:text-3xl font-bold text-[var(--portal-text-bright,#e5e7eb)] font-mono mb-2">
              ALIAS DETECTION
            </h1>
            <p class="text-sm text-neutral-500">
              Investigate player relationships and identify potential alternate accounts
            </p>
          </div>

          <!-- Search & Input Section -->
          <div class="explorer-card mb-6">
            <div class="explorer-card-body">
              <AliasDetectionForm
                ref="formRef"
                @search="onSearch"
                :loading="isLoading"
                :initial-player1="player1"
                :initial-player2="player2"
              />
            </div>
          </div>

          <!-- Loading State -->
          <div v-if="isLoading" class="explorer-card">
            <div class="explorer-card-body text-center py-12">
              <div class="flex items-center justify-center mb-4">
                <div class="w-12 h-12 border-4 border-neutral-700 rounded-full animate-spin" />
                <div class="absolute w-12 h-12 border-4 border-cyan-500 rounded-full border-t-transparent animate-spin" />
              </div>
              <p class="text-neutral-400">Analyzing players...</p>
            </div>
          </div>

          <!-- Error State -->
          <div v-else-if="error" class="explorer-card">
            <div class="explorer-card-body text-center py-8">
              <div class="w-12 h-12 bg-red-500/20 rounded-full flex items-center justify-center border border-red-500/50 mx-auto mb-4">
                <span class="text-2xl">⚠</span>
              </div>
              <h3 class="text-lg font-bold text-red-400 mb-2">Investigation Error</h3>
              <p class="text-neutral-400 mb-6">{{ error }}</p>
              <button
                @click="error = null"
                class="px-4 py-2 bg-red-500/20 border border-red-500/50 rounded text-red-400 text-sm hover:bg-red-500/30 transition-colors"
              >
                Dismiss
              </button>
            </div>
          </div>

          <!-- Results Section -->
          <div v-else-if="report" class="space-y-6">
            <div class="explorer-card">
              <div class="explorer-card-body">
                <AliasDetectionReport :report="report" />
              </div>
            </div>

            <div class="explorer-card">
              <div class="explorer-card-body">
                <AliasDetectionFullComparison
                  :player1-name="report.player1"
                  :player2-name="report.player2"
                />
              </div>
            </div>
          </div>

          <!-- Empty State -->
          <div v-else class="explorer-card">
            <div class="explorer-card-body text-center py-12">
              <div class="flex flex-col items-center justify-center mb-4">
                <div class="text-5xl mb-4">⚔️</div>
                <h3 class="text-lg font-bold text-neutral-300 mb-2">No Investigation Yet</h3>
                <p class="text-neutral-500 w-full sm:max-w-md px-4 sm:px-0">
                  Enter two player names above to begin analyzing their relationship patterns.
                </p>
              </div>
            </div>
          </div>

        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AliasDetectionForm from '../components/AliasDetectionForm.vue'
import AliasDetectionReport from '../components/AliasDetectionReport.vue'
import AliasDetectionFullComparison from '../components/AliasDetectionFullComparison.vue'
import { aliasDetectionService } from '../services/aliasDetectionService'
import type { PlayerAliasSuspicionReport } from '../types/alias-detection'

const route = useRoute()
const router = useRouter()

const report = ref<PlayerAliasSuspicionReport | null>(null)
const isLoading = ref(false)
const error = ref<string | null>(null)
const player1 = ref<string>('')
const player2 = ref<string>('')
const formRef = ref<any>(null)

const onSearch = async (p1: string, p2: string) => {
  if (!p1 || !p2) {
    error.value = 'Please enter both player names'
    return
  }

  if (p1.toLowerCase() === p2.toLowerCase()) {
    error.value = 'Cannot compare a player with themselves'
    return
  }

  // Update URL with query params
  await router.push({
    path: '/alias-detection',
    query: {
      player1: p1,
      player2: p2
    }
  })

  // Load the comparison
  await loadComparison(p1, p2)
}

const loadComparison = async (p1: string, p2: string) => {
  isLoading.value = true
  error.value = null
  report.value = null

  try {
    const result = await aliasDetectionService.comparePlayersAsync(p1, p2)
    report.value = result
    player1.value = p1
    player2.value = p2
    // Close the dropdowns after comparison
    formRef.value?.closeDropdowns()
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to analyze players'
  } finally {
    isLoading.value = false
  }
}

onMounted(() => {
  // Check if we have query params on initial load
  const p1 = route.query.player1 as string
  const p2 = route.query.player2 as string

  if (p1 && p2) {
    player1.value = p1
    player2.value = p2
    loadComparison(p1, p2)
  }
})

// Watch for URL changes (e.g., back button)
watch(
  () => ({ player1: route.query.player1, player2: route.query.player2 }),
  (newVal) => {
    const p1 = newVal.player1 as string
    const p2 = newVal.player2 as string
    if (p1 && p2 && (player1.value !== p1 || player2.value !== p2)) {
      player1.value = p1
      player2.value = p2
      loadComparison(p1, p2)
    }
  }
)
</script>

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
