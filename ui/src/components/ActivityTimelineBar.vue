<script setup lang="ts">
import { computed } from 'vue'

interface HourlyOverlap {
  hour: number
  player1Minutes: number
  player2Minutes: number
  overlapMinutes: number
}

interface Props {
  hourlyOverlap: HourlyOverlap[]
  player1Name: string
  player2Name: string
}

const props = defineProps<Props>()

// Ensure all 24 hours are represented
const hourlyData = computed(() => {
  const byHour = new Map<number, HourlyOverlap>()
  for (const entry of props.hourlyOverlap) {
    byHour.set(entry.hour, entry)
  }
  return Array.from({ length: 24 }, (_, hour) =>
    byHour.get(hour) ?? { hour, player1Minutes: 0, player2Minutes: 0, overlapMinutes: 0 }
  )
})

// Calculate max value for scaling
const maxMinutes = computed(() => {
  return Math.max(...hourlyData.value.map(h => Math.max(h.player1Minutes, h.player2Minutes)), 1)
})

// Calculate overlap percentage for each hour
const hourStats = computed(() => {
  return hourlyData.value.map(hour => {
    const maxVal = Math.max(hour.player1Minutes, hour.player2Minutes, 1)
    return {
      ...hour,
      player1Height: (hour.player1Minutes / maxMinutes.value) * 100,
      player2Height: (hour.player2Minutes / maxMinutes.value) * 100,
      overlapPercentage: maxVal > 0 ? (hour.overlapMinutes / maxVal * 100) : 0,
      hasOverlap: hour.overlapMinutes > 0
    }
  })
})

// Calculate overlap statistics
const stats = computed(() => {
  const totalHours = hourlyData.value.length
  const hoursWithOverlap = hourlyData.value.filter(h => h.overlapMinutes > 0).length
  const totalOverlapMinutes = hourlyData.value.reduce((sum, h) => sum + h.overlapMinutes, 0)
  const totalPlayer1Minutes = hourlyData.value.reduce((sum, h) => sum + h.player1Minutes, 0)
  const totalPlayer2Minutes = hourlyData.value.reduce((sum, h) => sum + h.player2Minutes, 0)

  return {
    hoursWithOverlap,
    overlapPercentage: hoursWithOverlap > 0 ? ((hoursWithOverlap / 24) * 100).toFixed(1) : '0',
    totalOverlapMinutes: Math.round(totalOverlapMinutes),
    totalPlayer1Minutes: Math.round(totalPlayer1Minutes),
    totalPlayer2Minutes: Math.round(totalPlayer2Minutes)
  }
})

const formatTime = (hour: number) => `${hour.toString().padStart(2, '0')}:00`
const formatMinutes = (minutes: number) => {
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return mins > 0 ? `${hours}h ${mins}m` : `${hours}h`
}
</script>

<template>
  <div class="timeline-container">
    <!-- Stats Header -->
    <div class="stats-header">
      <div class="stat-item">
        <span class="stat-label">Hours Overlapping</span>
        <span class="stat-value">{{ stats.hoursWithOverlap }}/24 ({{ stats.overlapPercentage }}%)</span>
      </div>
      <div class="stat-item">
        <span class="stat-label">Total Overlap Time</span>
        <span class="stat-value">{{ formatMinutes(stats.totalOverlapMinutes) }}</span>
      </div>
    </div>

    <!-- Timeline Visualization -->
    <div class="timeline-wrapper">
      <!-- Player 1 Timeline -->
      <div class="player-timeline">
        <div class="player-label">{{ player1Name }}</div>
        <div class="hours-grid">
          <div
            v-for="hour in hourStats"
            :key="`p1-${hour.hour}`"
            class="hour-cell"
            :title="`${formatTime(hour.hour)}: ${hour.player1Minutes}m`"
          >
            <div class="bar-container">
              <div
                v-if="hour.player1Minutes > 0"
                class="activity-bar player1-bar"
                :style="{ height: hour.player1Height + '%' }"
              />
            </div>
            <div class="hour-label">{{ hour.hour }}</div>
          </div>
        </div>
      </div>

      <!-- Overlap Indicator -->
      <div class="overlap-timeline">
        <div class="overlap-label">Overlap</div>
        <div class="hours-grid">
          <div
            v-for="hour in hourStats"
            :key="`overlap-${hour.hour}`"
            class="hour-cell overlap-cell"
            :class="{ 'has-overlap': hour.hasOverlap }"
            :title="hour.hasOverlap ? `${formatTime(hour.hour)}: ${hour.overlapMinutes}m overlap` : 'No overlap'"
          >
            <div v-if="hour.hasOverlap" class="overlap-indicator" :style="{ opacity: Math.min(hour.overlapPercentage / 100, 1) }" />
          </div>
        </div>
      </div>

      <!-- Player 2 Timeline -->
      <div class="player-timeline">
        <div class="player-label">{{ player2Name }}</div>
        <div class="hours-grid">
          <div
            v-for="hour in hourStats"
            :key="`p2-${hour.hour}`"
            class="hour-cell"
            :title="`${formatTime(hour.hour)}: ${hour.player2Minutes}m`"
          >
            <div class="bar-container">
              <div
                v-if="hour.player2Minutes > 0"
                class="activity-bar player2-bar"
                :style="{ height: hour.player2Height + '%' }"
              />
            </div>
            <div class="hour-label">{{ hour.hour }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Legend -->
    <div class="legend">
      <div class="legend-item">
        <div class="legend-color player1-bar"></div>
        <span>{{ player1Name }} Activity</span>
      </div>
      <div class="legend-item">
        <div class="legend-color overlap-indicator"></div>
        <span>Overlap Time</span>
      </div>
      <div class="legend-item">
        <div class="legend-color player2-bar"></div>
        <span>{{ player2Name }} Activity</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.timeline-container {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.stats-header {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin-bottom: 1rem;
}

.stat-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem;
  background: rgba(148, 163, 184, 0.1);
  border-radius: 6px;
  border-left: 3px solid #3b82f6;
}

.stat-label {
  font-size: 0.85rem;
  color: #94a3b8;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  font-weight: 600;
}

.stat-value {
  font-size: 1.1rem;
  color: #e2e8f0;
  font-weight: 700;
  font-mono: 'courier new';
}

.timeline-wrapper {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.player-timeline {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.overlap-timeline {
  display: flex;
  gap: 1rem;
  align-items: flex-start;
}

.player-label,
.overlap-label {
  min-width: 140px;
  font-size: 0.9rem;
  font-weight: 600;
  color: #cbd5e1;
  padding-top: 0.5rem;
  text-align: right;
}

.overlap-label {
  color: #a855f7;
}

.hours-grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(24, 1fr);
  gap: 2px;
  padding: 0.5rem;
  background: rgba(15, 23, 42, 0.3);
  border-radius: 6px;
  border: 1px solid rgba(148, 163, 184, 0.2);
}

.hour-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-end;
  height: 80px;
  background: rgba(15, 23, 42, 0.5);
  border-radius: 3px;
  padding: 2px;
  position: relative;
  transition: all 0.2s ease;
}

.hour-cell:hover {
  background: rgba(15, 23, 42, 0.8);
  border: 1px solid rgba(59, 130, 246, 0.5);
}

.overlap-cell {
  height: 40px;
  justify-content: center;
}

.bar-container {
  width: 100%;
  height: 60px;
  display: flex;
  align-items: flex-end;
  justify-content: center;
}

.activity-bar {
  width: 80%;
  border-radius: 2px 2px 0 0;
  transition: all 0.2s ease;
  min-height: 2px;
}

.player1-bar {
  background: linear-gradient(to top, #22d3ee, #06b6d4);
  box-shadow: 0 0 8px rgba(34, 211, 238, 0.3);
}

.player1-bar:hover {
  box-shadow: 0 0 12px rgba(34, 211, 238, 0.6);
}

.player2-bar {
  background: linear-gradient(to top, #fb923c, #f97316);
  box-shadow: 0 0 8px rgba(251, 146, 60, 0.3);
}

.player2-bar:hover {
  box-shadow: 0 0 12px rgba(251, 146, 60, 0.6);
}

.overlap-indicator {
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, #a855f7, #d946ef);
  border-radius: 3px;
  box-shadow: 0 0 8px rgba(168, 85, 247, 0.4);
  transition: all 0.2s ease;
}

.overlap-cell.has-overlap:hover .overlap-indicator {
  box-shadow: 0 0 12px rgba(168, 85, 247, 0.8);
}

.hour-label {
  font-size: 0.65rem;
  color: #64748b;
  margin-top: 2px;
  font-weight: 600;
}

.legend {
  display: flex;
  gap: 2rem;
  justify-content: center;
  padding: 1rem;
  border-top: 1px solid rgba(148, 163, 184, 0.1);
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  color: #cbd5e1;
}

.legend-color {
  width: 12px;
  height: 12px;
  border-radius: 2px;
}

.legend-color.player1-bar {
  background: linear-gradient(135deg, #22d3ee, #06b6d4);
}

.legend-color.player2-bar {
  background: linear-gradient(135deg, #fb923c, #f97316);
}

.legend-color.overlap-indicator {
  background: linear-gradient(135deg, #a855f7, #d946ef);
}

@media (max-width: 768px) {
  .hours-grid {
    grid-template-columns: repeat(12, 1fr);
  }

  .hour-cell {
    height: 60px;
  }

  .bar-container {
    height: 45px;
  }

  .overlap-cell {
    height: 30px;
  }

  .player-label,
  .overlap-label {
    min-width: 100px;
    font-size: 0.8rem;
  }

  .stats-header {
    grid-template-columns: 1fr;
  }

  .legend {
    flex-direction: column;
    gap: 0.75rem;
  }
}
</style>
