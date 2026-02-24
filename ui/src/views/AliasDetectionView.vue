<template>
  <div class="alias-detection-container">
    <div class="detection-header">
      <div class="header-content">
        <h1 class="header-title">Alias Detection</h1>
        <p class="header-subtitle">Investigate player relationships and identify potential alternate accounts</p>
      </div>
    </div>

    <div class="detection-grid">
      <!-- Search & Input Section -->
      <div class="search-section">
        <AliasDetectionForm
          ref="formRef"
          @search="onSearch"
          :loading="isLoading"
          :initial-player1="player1"
          :initial-player2="player2"
        />
      </div>

      <!-- Results Section -->
      <div v-if="report" class="results-section">
        <AliasDetectionReport :report="report" />
        <AliasDetectionFullComparison
          :player1-name="report.player1"
          :player2-name="report.player2"
        />
      </div>

      <!-- Empty State -->
      <div v-if="!report && !isLoading" class="empty-state">
        <div class="empty-icon">⚔</div>
        <h2>No Investigation Yet</h2>
        <p>Enter two player names above to begin analyzing their relationship patterns.</p>
      </div>

      <!-- Error State -->
      <div v-if="error" class="error-state">
        <div class="error-icon">!</div>
        <h2>Investigation Error</h2>
        <p>{{ error }}</p>
        <button @click="error = null" class="btn-clear-error">Dismiss</button>
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
.alias-detection-container {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: #0f0f15;
  padding: 1rem;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
}

.detection-header {
  margin-bottom: 1.5rem;
  text-align: left;
}

.header-content {
  width: 100%;
}

.header-title {
  font-size: 1.75rem;
  font-weight: 700;
  color: #e5e7eb;
  margin: 0 0 0.5rem 0;
  letter-spacing: 0;
}

.header-subtitle {
  font-size: 0.95rem;
  color: #6b7280;
  margin: 0;
  font-weight: 400;
}

.detection-grid {
  width: 100%;
  display: grid;
  gap: 1.5rem;
  flex: 1;
}

.search-section {
  grid-column: 1 / -1;
}

.results-section {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: 2rem;
  flex: 1;
}

.empty-state,
.error-state {
  grid-column: 1 / -1;
  padding: 2rem;
  text-align: center;
  border: 1px solid #1a1a24;
  border-radius: 6px;
  background: transparent;
}

.empty-icon,
.error-icon {
  font-size: 2.5rem;
  margin-bottom: 0.75rem;
}

.empty-state h2,
.error-state h2 {
  color: #e5e7eb;
  margin: 0 0 0.5rem 0;
  font-size: 1.25rem;
}

.empty-state p,
.error-state p {
  color: #9ca3af;
  margin: 0;
  font-size: 0.9rem;
}

.error-state {
  border-color: #dc2626;
}

.error-state h2 {
  color: #dc2626;
}

.btn-clear-error {
  margin-top: 1.5rem;
  padding: 0.5rem 1.5rem;
  background: #ef4444;
  color: white;
  border: none;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  transition: background-color 0.2s;
}

.btn-clear-error:hover {
  background: #dc2626;
}

@media (max-width: 768px) {
  .detection-header {
    margin-bottom: 1rem;
  }

  .detection-grid {
    gap: 1rem;
  }
}
</style>
