<template>
  <div class="map-performance-race">
    <!-- Loading state -->
    <div v-if="loading" class="loading-state">
      Loading map performance timeline...
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="error-state">
      {{ error }}
    </div>

    <!-- Race chart content -->
    <div v-else-if="timelineData && timelineData.months.length > 0" class="race-content">
      <!-- Month label -->
      <div class="month-label">
        {{ currentMonth?.monthLabel || '' }}
      </div>

      <!-- Controls -->
      <div class="race-controls">
        <button
          @click="togglePlayback"
          :class="['control-btn', isPlaying ? 'pause' : 'play']"
        >
          {{ isPlaying ? '&#10074;&#10074; PAUSE' : '&#9654; PLAY' }}
        </button>

        <div class="metric-toggle">
          <button
            v-for="metric in metrics"
            :key="metric.value"
            :class="['metric-btn', { active: selectedMetric === metric.value }]"
            @click="selectedMetric = metric.value"
          >
            {{ metric.label }}
          </button>
        </div>
      </div>

      <!-- Month scrubber -->
      <div class="month-scrubber">
        <input
          type="range"
          v-model.number="currentMonthIndex"
          :min="0"
          :max="timelineData.months.length - 1"
          :disabled="isPlaying"
          class="scrubber"
        />
        <div class="scrubber-labels">
          <span>{{ timelineData.months[0].monthLabel }}</span>
          <span>{{ timelineData.months[timelineData.months.length - 1].monthLabel }}</span>
        </div>
      </div>

      <!-- Bar chart with animated reordering -->
      <div class="bar-chart">
        <TransitionGroup name="bar-reorder" tag="div" class="bar-list">
          <div
            v-for="(map, index) in topMaps"
            :key="map.mapName"
            class="bar-row"
          >
            <div class="bar-label" :title="map.mapName">{{ map.mapName }}</div>
            <div class="bar-track">
              <div
                class="bar-fill"
                :style="{ width: getBarWidth(map) + '%' }"
                :class="getBarClass(index)"
              />
            </div>
            <div class="bar-value">{{ formatValue(map) }}</div>
          </div>
        </TransitionGroup>
      </div>
    </div>

    <!-- No data state -->
    <div v-else class="no-data">
      No map performance data available
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { fetchMapPerformanceTimeline } from '@/services/playerStatsApi';
import type { MapPerformanceTimelineResponse } from '@/types/playerStatsTypes';

const props = defineProps<{
  playerName: string;
  game?: string;
}>();

const loading = ref(true);
const error = ref<string | null>(null);
const timelineData = ref<MapPerformanceTimelineResponse | null>(null);
const currentMonthIndex = ref(0);
const isPlaying = ref(false);
const selectedMetric = ref<'kdRatio' | 'score' | 'kills'>('kdRatio' as 'kdRatio' | 'score' | 'kills');
const playbackSpeed = ref(1500);

let playbackInterval: number | null = null;

const metrics: { value: 'kdRatio' | 'score' | 'kills'; label: string }[] = [
  { value: 'kdRatio', label: 'K/D' },
  { value: 'score', label: 'SCORE' },
  { value: 'kills', label: 'KILLS' }
];

const currentMonth = computed(() => {
  if (!timelineData.value) return null;
  return timelineData.value.months[currentMonthIndex.value];
});

const getValue = (m: { kdRatio: number; score: number; kills: number }): number => {
  switch (selectedMetric.value) {
    case 'kdRatio': return m.kdRatio;
    case 'kills': return m.kills;
    case 'score': return m.score;
    default: return m.score;
  }
};

const topMaps = computed(() => {
  if (!currentMonth.value) return [];
  const sorted = [...currentMonth.value.maps].sort((a, b) => getValue(b) - getValue(a));
  return sorted.slice(0, 10);
});

const maxValue = computed(() => {
  if (topMaps.value.length === 0) return 1;
  return Math.max(...topMaps.value.map(getValue));
});

const getBarWidth = (map: { kdRatio: number; score: number; kills: number }): number => {
  const val = getValue(map);
  return maxValue.value > 0 ? (val / maxValue.value) * 100 : 0;
};

const getBarClass = (index: number): string => {
  if (index === 0) return 'bar-fill--top';
  return '';
};

const formatValue = (map: { kdRatio: number; score: number; kills: number; playTimeMinutes: number }): string => {
  switch (selectedMetric.value) {
    case 'kdRatio': return map.kdRatio.toFixed(2);
    case 'kills': return map.kills.toLocaleString();
    case 'score': return map.score.toLocaleString();
    default: return '';
  }
};

// Playback control
function togglePlayback() {
  if (isPlaying.value) {
    stopPlayback();
  } else {
    startPlayback();
  }
}

function startPlayback() {
  if (!timelineData.value) return;

  if (currentMonthIndex.value >= timelineData.value.months.length - 1) {
    currentMonthIndex.value = 0;
  }

  isPlaying.value = true;

  playbackInterval = window.setInterval(() => {
    if (currentMonthIndex.value < timelineData.value!.months.length - 1) {
      currentMonthIndex.value++;
    } else {
      stopPlayback();
    }
  }, playbackSpeed.value);
}

function stopPlayback() {
  isPlaying.value = false;
  if (playbackInterval !== null) {
    clearInterval(playbackInterval);
    playbackInterval = null;
  }
}

async function loadData() {
  loading.value = true;
  error.value = null;

  try {
    timelineData.value = await fetchMapPerformanceTimeline(
      props.playerName,
      props.game || 'bf1942',
      12
    );
    currentMonthIndex.value = 0;
  } catch (err) {
    error.value = 'Failed to load map performance timeline';
    console.error(err);
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  loadData();
});

onUnmounted(() => {
  stopPlayback();
});
</script>

<style scoped>
.map-performance-race {
  height: 100%;
  display: flex;
  flex-direction: column;
  font-family: 'JetBrains Mono', monospace;
}

.loading-state,
.error-state,
.no-data {
  padding: 32px;
  text-align: center;
  color: var(--text-secondary);
}

.error-state {
  color: var(--neon-red);
}

.race-content {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.month-label {
  font-size: 24px;
  font-weight: 600;
  text-align: center;
  color: var(--neon-cyan);
  text-shadow: 0 0 12px rgba(245, 158, 11, 0.4);
  margin-bottom: 16px;
}

.race-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.control-btn {
  padding: 8px 16px;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  color: var(--text-primary);
  font-size: 13px;
  font-family: 'JetBrains Mono', monospace;
  cursor: pointer;
  transition: all 0.2s;
}

.control-btn:hover {
  border-color: var(--neon-cyan);
  color: var(--neon-cyan);
}

.control-btn.play {
  background: rgba(245, 158, 11, 0.1);
  border-color: var(--neon-cyan);
  color: var(--neon-cyan);
}

.control-btn.pause {
  background: rgba(255, 107, 107, 0.1);
  border-color: #ff6b6b;
  color: #ff6b6b;
}

.metric-toggle {
  display: flex;
  gap: 8px;
}

.metric-btn {
  padding: 4px 12px;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  color: var(--text-secondary);
  font-size: 12px;
  font-family: 'JetBrains Mono', monospace;
  cursor: pointer;
  transition: all 0.2s;
}

.metric-btn.active {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  border-color: var(--neon-cyan);
}

.metric-btn:hover:not(.active) {
  border-color: var(--neon-cyan);
  color: var(--neon-cyan);
}

.month-scrubber {
  margin-bottom: 16px;
}

.scrubber {
  width: 100%;
  height: 4px;
  -webkit-appearance: none;
  appearance: none;
  background: var(--bg-panel);
  outline: none;
  cursor: pointer;
}

.scrubber::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 16px;
  height: 16px;
  background: var(--neon-cyan);
  cursor: pointer;
  border-radius: 50%;
}

.scrubber::-moz-range-thumb {
  width: 16px;
  height: 16px;
  background: var(--neon-cyan);
  cursor: pointer;
  border-radius: 50%;
  border: none;
}

.scrubber:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

.scrubber-labels {
  display: flex;
  justify-content: space-between;
  margin-top: 4px;
  font-size: 11px;
  color: var(--text-secondary);
}

/* Bar chart */
.bar-chart {
  position: relative;
}

.bar-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  position: relative;
}

.bar-row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 28px;
}

.bar-label {
  width: 120px;
  min-width: 120px;
  font-size: 11px;
  color: var(--text-secondary);
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

@media (max-width: 640px) {
  .bar-label {
    width: 80px;
    min-width: 80px;
    font-size: 10px;
  }
}

.bar-track {
  flex: 1;
  height: 20px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 3px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  background: rgba(245, 158, 11, 0.6);
  border-radius: 3px;
  transition: width 0.5s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.bar-fill--top {
  background: rgba(251, 191, 36, 0.85);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.3);
}

.bar-value {
  width: 70px;
  min-width: 70px;
  font-size: 11px;
  color: var(--text-primary);
  text-align: right;
}

@media (max-width: 640px) {
  .bar-value {
    width: 55px;
    min-width: 55px;
    font-size: 10px;
  }
}

/* FLIP animation for reordering */
.bar-reorder-move {
  transition: transform 0.5s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.bar-reorder-enter-active {
  transition: all 0.4s ease;
}

.bar-reorder-leave-active {
  transition: all 0.3s ease;
  position: absolute;
  width: 100%;
}

.bar-reorder-enter-from {
  opacity: 0;
  transform: translateX(-20px);
}

.bar-reorder-leave-to {
  opacity: 0;
  transform: translateX(20px);
}
</style>
