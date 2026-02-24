<template>
  <div class="details-section">
    <!-- Expandable Sections Header -->
    <div class="section-header">
      <h3>Deep Dive Comparison</h3>
      <p class="section-subtitle">Expand to view detailed player data, servers, maps, recent sessions, and rankings</p>
    </div>

    <!-- Time Range Control -->
    <div class="time-range-selector">
      <div class="range-label">Look back:</div>
      <div class="range-buttons">
        <button
          v-for="range in timeRanges"
          :key="range.value"
          class="range-btn"
          :class="{ active: selectedRange === range.value }"
          @click="selectedRange = range.value"
        >
          {{ range.label }}
        </button>
      </div>
    </div>

    <!-- Details Grid -->
    <div class="details-grid">
      <!-- Player 1 Section -->
      <div class="player-section">
        <button class="section-toggle" @click="expandedPlayer1 = !expandedPlayer1" :aria-expanded="expandedPlayer1">
          <div class="toggle-header">
            <h4>{{ player1Name }}</h4>
            <span class="toggle-icon" :class="{ expanded: expandedPlayer1 }">▼</span>
          </div>
        </button>

        <div v-if="expandedPlayer1" class="section-content">
          <div v-if="loading1" class="loading-state">
            <div class="spinner"></div>
            <p>Loading data...</p>
          </div>

          <div v-else-if="error1" class="error-state">
            <p>{{ error1 }}</p>
          </div>

          <div v-else-if="stats1" class="data-sections">
            <!-- Summary Stats -->
            <div class="data-block">
              <h5>Overall Stats</h5>
              <div class="stats-grid">
                <div class="stat-item">
                  <span class="stat-label">Playtime</span>
                  <span class="stat-value">{{ formatPlayTime(stats1.totalPlayTimeMinutes || 0) }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">K/D</span>
                  <span class="stat-value">{{ ((stats1.totalKills || 0) / Math.max(stats1.totalDeaths || 1, 1)).toFixed(2) }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">Kill Rate</span>
                  <span class="stat-value">{{ ((stats1.totalKills || 0) / Math.max(stats1.totalPlayTimeMinutes || 1, 1)).toFixed(2) }}/min</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">Total Rounds</span>
                  <span class="stat-value">{{ (stats1.servers?.reduce((sum, s) => sum + (s.totalRounds || 0), 0) || 0).toLocaleString() }}</span>
                </div>
              </div>
            </div>

            <!-- Top Servers -->
            <div class="data-block">
              <h5>Top Servers (by playtime)</h5>
              <div v-if="stats1.servers?.length" class="data-list">
                <div v-for="server in stats1.servers.slice(0, 8)" :key="server.serverGuid" class="list-item server-item">
                  <div class="item-name">{{ server.serverName }}</div>
                  <div class="item-stats">
                    <span>{{ formatPlayTime(server.totalMinutes) }}</span>
                    <span class="stat-sep">•</span>
                    <span>K/D {{ server.kdRatio.toFixed(2) }}</span>
                  </div>
                </div>
              </div>
              <p v-else class="no-data">No server data</p>
            </div>

            <!-- Best Scores -->
            <div class="data-block">
              <h5>Best Scores</h5>
              <div v-if="bestScores1.length" class="data-list">
                <div v-for="(score, idx) in bestScores1.slice(0, 5)" :key="idx" class="list-item score-item">
                  <div class="score-rank">{{ idx + 1 }}</div>
                  <div class="score-details">
                    <div class="score-points">{{ score.score.toLocaleString() }} PTS</div>
                    <div class="score-meta">{{ score.mapName }} • {{ score.serverName }}</div>
                  </div>
                </div>
              </div>
              <p v-else class="no-data">No scores recorded</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Player 2 Section -->
      <div class="player-section">
        <button class="section-toggle" @click="expandedPlayer2 = !expandedPlayer2" :aria-expanded="expandedPlayer2">
          <div class="toggle-header">
            <h4>{{ player2Name }}</h4>
            <span class="toggle-icon" :class="{ expanded: expandedPlayer2 }">▼</span>
          </div>
        </button>

        <div v-if="expandedPlayer2" class="section-content">
          <div v-if="loading2" class="loading-state">
            <div class="spinner"></div>
            <p>Loading data...</p>
          </div>

          <div v-else-if="error2" class="error-state">
            <p>{{ error2 }}</p>
          </div>

          <div v-else-if="stats2" class="data-sections">
            <!-- Summary Stats -->
            <div class="data-block">
              <h5>Overall Stats</h5>
              <div class="stats-grid">
                <div class="stat-item">
                  <span class="stat-label">Playtime</span>
                  <span class="stat-value">{{ formatPlayTime(stats2.totalPlayTimeMinutes || 0) }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">K/D</span>
                  <span class="stat-value">{{ ((stats2.totalKills || 0) / Math.max(stats2.totalDeaths || 1, 1)).toFixed(2) }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">Kill Rate</span>
                  <span class="stat-value">{{ ((stats2.totalKills || 0) / Math.max(stats2.totalPlayTimeMinutes || 1, 1)).toFixed(2) }}/min</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">Total Rounds</span>
                  <span class="stat-value">{{ (stats2.servers?.reduce((sum, s) => sum + (s.totalRounds || 0), 0) || 0).toLocaleString() }}</span>
                </div>
              </div>
            </div>

            <!-- Top Servers -->
            <div class="data-block">
              <h5>Top Servers (by playtime)</h5>
              <div v-if="stats2.servers?.length" class="data-list">
                <div v-for="server in stats2.servers.slice(0, 8)" :key="server.serverGuid" class="list-item server-item">
                  <div class="item-name">{{ server.serverName }}</div>
                  <div class="item-stats">
                    <span>{{ formatPlayTime(server.totalMinutes) }}</span>
                    <span class="stat-sep">•</span>
                    <span>K/D {{ server.kdRatio.toFixed(2) }}</span>
                  </div>
                </div>
              </div>
              <p v-else class="no-data">No server data</p>
            </div>

            <!-- Best Scores -->
            <div class="data-block">
              <h5>Best Scores</h5>
              <div v-if="bestScores2.length" class="data-list">
                <div v-for="(score, idx) in bestScores2.slice(0, 5)" :key="idx" class="list-item score-item">
                  <div class="score-rank">{{ idx + 1 }}</div>
                  <div class="score-details">
                    <div class="score-points">{{ score.score.toLocaleString() }} PTS</div>
                    <div class="score-meta">{{ score.mapName }} • {{ score.serverName }}</div>
                  </div>
                </div>
              </div>
              <p v-else class="no-data">No scores recorded</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { PlayerTimeStatistics, fetchPlayerStats } from '../services/playerStatsService'

interface Props {
  player1Name: string
  player2Name: string
}

const props = defineProps<Props>()

const expandedPlayer1 = ref(false)
const expandedPlayer2 = ref(false)

const stats1 = ref<PlayerTimeStatistics | null>(null)
const stats2 = ref<PlayerTimeStatistics | null>(null)

const loading1 = ref(false)
const loading2 = ref(false)

const error1 = ref<string | null>(null)
const error2 = ref<string | null>(null)

const selectedRange = ref('thisWeek')

const timeRanges = [
  { label: 'This Week', value: 'thisWeek' },
  { label: 'Last 30 Days', value: 'last30Days' },
  { label: 'All Time', value: 'allTime' }
]

// Get best scores based on selected time range
const bestScores1 = computed(() => {
  if (!stats1.value?.bestScores) return []
  return stats1.value.bestScores[selectedRange.value as keyof typeof stats1.value.bestScores] || []
})

const bestScores2 = computed(() => {
  if (!stats2.value?.bestScores) return []
  return stats2.value.bestScores[selectedRange.value as keyof typeof stats2.value.bestScores] || []
})

const formatPlayTime = (minutes: number): string => {
  const hours = Math.floor(minutes / 60)
  const mins = Math.floor(minutes % 60)
  if (hours === 0) return `${mins}m`
  return `${hours}h ${mins}m`
}

const fetchPlayerData = async (playerName: string, isPlayer1: boolean) => {
  const loadingRef = isPlayer1 ? loading1 : loading2
  const errorRef = isPlayer1 ? error1 : error2
  const statsRef = isPlayer1 ? stats1 : stats2

  loadingRef.value = true
  errorRef.value = null

  try {
    const data = await fetchPlayerStats(playerName)
    statsRef.value = data
  } catch (err) {
    errorRef.value = 'Failed to load player details'
    console.error(err)
  } finally {
    loadingRef.value = false
  }
}

onMounted(() => {
  watch(
    () => expandedPlayer1.value,
    (newVal) => {
      if (newVal && !stats1.value && !loading1.value) {
        fetchPlayerData(props.player1Name, true)
      }
    }
  )

  watch(
    () => expandedPlayer2.value,
    (newVal) => {
      if (newVal && !stats2.value && !loading2.value) {
        fetchPlayerData(props.player2Name, false)
      }
    }
  )
})
</script>

<style scoped>
.details-section {
  grid-column: 1 / -1;
  margin-top: 2rem;
}

.section-header {
  margin-bottom: 1.5rem;
}

.section-header h3 {
  margin: 0 0 0.5rem 0;
  color: #e2e8f0;
  font-size: 1.25rem;
  font-weight: 600;
}

.section-subtitle {
  margin: 0;
  color: #94a3b8;
  font-size: 0.9rem;
}

.time-range-selector {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1.5rem;
  padding: 1rem;
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 8px;
}

.range-label {
  color: #cbd5e1;
  font-size: 0.9rem;
  font-weight: 600;
  white-space: nowrap;
}

.range-buttons {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.range-btn {
  padding: 0.5rem 1rem;
  background: rgba(15, 23, 42, 0.8);
  border: 1px solid #475569;
  border-radius: 6px;
  color: #cbd5e1;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.range-btn:hover {
  border-color: #3b82f6;
  color: #e2e8f0;
}

.range-btn.active {
  background: #3b82f6;
  border-color: #3b82f6;
  color: white;
}

.details-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
}

@media (max-width: 1024px) {
  .details-grid {
    grid-template-columns: 1fr;
  }
}

.player-section {
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 8px;
  overflow: hidden;
}

.section-toggle {
  width: 100%;
  padding: 1rem;
  background: rgba(30, 41, 59, 0.6);
  border: none;
  border-bottom: 1px solid #334155;
  cursor: pointer;
  transition: background-color 0.2s;
  text-align: left;
}

.section-toggle:hover {
  background: rgba(30, 41, 59, 0.8);
}

.toggle-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.toggle-header h4 {
  margin: 0;
  color: #e2e8f0;
  font-size: 1rem;
  font-weight: 600;
}

.toggle-icon {
  display: inline-block;
  transition: transform 0.3s;
  color: #94a3b8;
  font-size: 0.8rem;
}

.toggle-icon.expanded {
  transform: rotate(180deg);
}

.section-content {
  padding: 1rem;
  background: rgba(15, 23, 42, 0.6);
}

.loading-state,
.error-state {
  padding: 2rem 1rem;
  text-align: center;
  color: #94a3b8;
}

.spinner {
  width: 24px;
  height: 24px;
  border: 2px solid rgba(255, 255, 255, 0.2);
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
  margin: 0 auto 0.5rem;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.error-state {
  color: #fca5a5;
}

.data-sections {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.data-block {
  border-top: 1px solid #334155;
  padding-top: 1rem;
}

.data-block:first-child {
  border-top: none;
  padding-top: 0;
}

.data-block h5 {
  margin: 0 0 0.75rem 0;
  color: #cbd5e1;
  font-size: 0.85rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.no-data {
  margin: 0;
  color: #64748b;
  font-size: 0.85rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 0.75rem;
}

.stat-item {
  padding: 0.75rem;
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.stat-label {
  color: #94a3b8;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.3px;
}

.stat-value {
  color: #e2e8f0;
  font-size: 0.95rem;
  font-weight: 600;
}

.data-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.list-item {
  padding: 0.75rem;
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.server-item {
  justify-content: space-between;
}

.item-name {
  color: #e2e8f0;
  font-size: 0.9rem;
  font-weight: 500;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.item-stats {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8rem;
  color: #94a3b8;
  white-space: nowrap;
}

.stat-sep {
  color: #475569;
}

.score-item {
  gap: 1rem;
}

.score-rank {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(59, 130, 246, 0.2);
  border: 1px solid #3b82f6;
  border-radius: 4px;
  color: #60a5fa;
  font-size: 0.85rem;
  font-weight: 600;
  flex-shrink: 0;
}

.score-details {
  flex: 1;
  min-width: 0;
}

.score-points {
  color: #e2e8f0;
  font-size: 0.9rem;
  font-weight: 600;
  line-height: 1.2;
}

.score-meta {
  color: #94a3b8;
  font-size: 0.8rem;
  line-height: 1.2;
  margin-top: 0.25rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
