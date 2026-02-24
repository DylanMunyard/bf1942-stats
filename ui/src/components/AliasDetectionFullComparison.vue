<template>
  <div class="comparison-container">
    <!-- Loading State -->
    <div v-if="isLoading" class="loading-state">
      <div class="spinner"></div>
      <p>Loading comparison data...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="error-state">
      <p>{{ error }}</p>
    </div>

    <!-- Rich Forensic Comparison -->
    <div v-else-if="comparisonData" class="comparison-sections">
      <!-- Core Statistics -->
      <div class="section">
        <ComparisonCoreStats
          :player1-name="player1Name"
          :player2-name="player2Name"
          :player1-kill-rate="player1KillRate"
          :player2-kill-rate="player2KillRate"
          :player1-average-ping="player1AveragePing"
          :player2-average-ping="player2AveragePing"
        />
      </div>

      <!-- Activity Timeline -->
      <div v-if="comparisonData.hourlyOverlap && comparisonData.hourlyOverlap.length > 0" class="section">
        <h3 class="section-title">Activity Timeline</h3>
        <ActivityTimelineBar
          :hourly-overlap="comparisonData.hourlyOverlap"
          :player1-name="player1Name"
          :player2-name="player2Name"
        />
      </div>

      <!-- Performance Over Time -->
      <div v-if="comparisonData.bucketTotals && comparisonData.bucketTotals.length > 0" class="section">
        <PerformanceOverTime
          :bucket-totals="comparisonData.bucketTotals"
          :player1-name="player1Name"
          :player2-name="player2Name"
        />
      </div>

      <!-- Map Performance -->
      <div v-if="comparisonData.mapPerformance && comparisonData.mapPerformance.length > 0" class="section">
        <MapPerformanceTable
          :map-performance="comparisonData.mapPerformance"
          :player1-name="player1Name"
          :player2-name="player2Name"
        />
      </div>

      <!-- Head-to-Head Encounters -->
      <div v-if="comparisonData.headToHead && comparisonData.headToHead.length > 0" class="section">
        <HeadToHeadTable
          :head-to-head="comparisonData.headToHead"
          :player1-name="player1Name"
          :player2-name="player2Name"
          :player1-input="player1Name"
          :player2-input="player2Name"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import ComparisonCoreStats from './ComparisonCoreStats.vue'
import PerformanceOverTime from './PerformanceOverTime.vue'
import ActivityTimelineBar from './ActivityTimelineBar.vue'
import MapPerformanceTable from './MapPerformanceTable.vue'
import HeadToHeadTable from './HeadToHeadTable.vue'

interface Props {
  player1Name: string
  player2Name: string
}

interface PlayerComparisonResult {
  player1: string
  player2: string
  killRates: Array<{ playerName: string; killRate: number }>
  bucketTotals: Array<{
    bucket: string
    player1Totals: { score: number; kills: number; deaths: number; playTimeMinutes?: number }
    player2Totals: { score: number; kills: number; deaths: number; playTimeMinutes?: number }
  }>
  averagePing: Array<{ playerName: string; averagePing: number }>
  mapPerformance: Array<{
    mapName: string
    player1Totals: { score: number; kills: number; deaths: number }
    player2Totals: { score: number; kills: number; deaths: number }
  }>
  headToHead: Array<{
    timestamp: string
    serverGuid: string
    mapName: string
    player1Score: number
    player1Kills: number
    player1Deaths: number
    player2Score: number
    player2Kills: number
    player2Deaths: number
  }>
  hourlyOverlap: Array<{
    hour: number
    player1Minutes: number
    player2Minutes: number
    overlapMinutes: number
  }>
}

const props = defineProps<Props>()

const isLoading = ref(true)
const error = ref<string | null>(null)
const comparisonData = ref<PlayerComparisonResult | null>(null)
const chartKey = ref(0)

const player1KillRate = computed(() => {
  if (!comparisonData.value?.killRates) return 0
  const data = comparisonData.value.killRates.find(kr => kr.playerName === props.player1Name)
  return data?.killRate || 0
})

const player2KillRate = computed(() => {
  if (!comparisonData.value?.killRates) return 0
  const data = comparisonData.value.killRates.find(kr => kr.playerName === props.player2Name)
  return data?.killRate || 0
})

const player1AveragePing = computed(() => {
  if (!comparisonData.value?.averagePing) return 0
  const data = comparisonData.value.averagePing.find(ap => ap.playerName === props.player1Name)
  return data?.averagePing || 0
})

const player2AveragePing = computed(() => {
  if (!comparisonData.value?.averagePing) return 0
  const data = comparisonData.value.averagePing.find(ap => ap.playerName === props.player2Name)
  return data?.averagePing || 0
})

const fetchComparisonData = async () => {
  isLoading.value = true
  error.value = null

  try {
    const url = `/stats/players/compare?player1=${encodeURIComponent(props.player1Name)}&player2=${encodeURIComponent(props.player2Name)}`
    const response = await fetch(url)

    if (!response.ok) {
      throw new Error('Failed to fetch comparison data')
    }

    const data = await response.json()
    comparisonData.value = data
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to load comparison data'
    console.error(err)
  } finally {
    isLoading.value = false
  }
}

onMounted(() => {
  fetchComparisonData()
})
</script>

<style scoped>
.comparison-container {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding: 1rem 0;
}

.loading-state,
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1rem;
  text-align: center;
  color: #94a3b8;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid rgba(255, 255, 255, 0.2);
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
  margin-bottom: 1rem;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.error-state {
  color: #fca5a5;
}

.comparison-sections {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.section {
  width: 100%;
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
  color: #cbd5e1;
  margin-bottom: 1rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
</style>
