<template>
  <div class="competitive-rankings-chart">
    <!-- Metric toggle -->
    <div class="metric-toggle">
      <button
        v-for="metric in metrics"
        :key="metric.value"
        :class="['metric-btn', { active: sortBy === metric.value }]"
        @click="sortBy = metric.value"
      >
        {{ metric.label }}
      </button>
    </div>

    <!-- Custom bar chart with animated reordering -->
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
              :class="getBarClass(map, index)"
            />
          </div>
          <div class="bar-value">{{ formatValue(map) }}</div>
        </div>
      </TransitionGroup>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';

interface MapRanking {
  mapName: string;
  rank: number;
  totalPlayers: number;
  percentile: number;
  totalScore: number;
  totalKills: number;
  totalDeaths: number;
  kdRatio: number;
  playTimeMinutes: number;
}

const props = defineProps<{
  rankings: MapRanking[];
  sortBy?: 'kdRatio' | 'kills' | 'timePlayed' | 'score';
}>();

const sortBy = ref<'kdRatio' | 'kills' | 'timePlayed' | 'score'>(props.sortBy || 'kdRatio');

const metrics: { value: 'kdRatio' | 'kills' | 'timePlayed' | 'score'; label: string }[] = [
  { value: 'kdRatio', label: 'K/D' },
  { value: 'kills', label: 'KILLS' },
  { value: 'timePlayed', label: 'TIME' },
  { value: 'score', label: 'SCORE' }
];

const getValue = (m: MapRanking): number => {
  switch (sortBy.value) {
    case 'kdRatio': return m.kdRatio;
    case 'kills': return m.totalKills;
    case 'timePlayed': return m.playTimeMinutes;
    case 'score': return m.totalScore;
    default: return m.totalScore;
  }
};

const topMaps = computed(() => {
  const sorted = [...props.rankings].sort((a, b) => getValue(b) - getValue(a));
  return sorted.slice(0, 15);
});

const maxValue = computed(() => {
  if (topMaps.value.length === 0) return 1;
  return Math.max(...topMaps.value.map(getValue));
});

const getBarWidth = (map: MapRanking): number => {
  const val = getValue(map);
  return maxValue.value > 0 ? (val / maxValue.value) * 100 : 0;
};

const getBarClass = (map: MapRanking, index: number): string => {
  if (map.rank === 1) return 'bar-fill--gold';
  if (index === 0) return 'bar-fill--top';
  return '';
};

const formatValue = (map: MapRanking): string => {
  switch (sortBy.value) {
    case 'kdRatio': return map.kdRatio.toFixed(2);
    case 'kills': return map.totalKills.toLocaleString();
    case 'timePlayed': {
      const hours = Math.floor(map.playTimeMinutes / 60);
      const mins = Math.floor(map.playTimeMinutes % 60);
      return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
    }
    case 'score': return map.totalScore.toLocaleString();
    default: return '';
  }
};
</script>

<style scoped>
.competitive-rankings-chart {
  display: flex;
  flex-direction: column;
}

.metric-toggle {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  justify-content: center;
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
  font-family: 'JetBrains Mono', monospace;
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

.bar-fill--gold {
  background: rgba(251, 191, 36, 0.85);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.3);
}

.bar-fill--top {
  background: rgba(245, 158, 11, 0.8);
}

.bar-value {
  width: 70px;
  min-width: 70px;
  font-size: 11px;
  font-family: 'JetBrains Mono', monospace;
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
