<template>
  <div class="player-activity-heatmap">
    <!-- Toggle buttons -->
    <div class="view-toggle">
      <button 
        :class="['toggle-btn', { active: viewMode === 'heatmap' }]"
        @click="viewMode = 'heatmap'"
      >
        HEATMAP
      </button>
      <button 
        :class="['toggle-btn', { active: viewMode === 'table' }]"
        @click="viewMode = 'table'"
      >
        TABLE
      </button>
    </div>

    <!-- Loading state -->
    <div v-if="loading" class="loading-state">
      Loading activity data...
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="error-state">
      {{ error }}
    </div>

    <!-- Heatmap view -->
    <div v-else-if="viewMode === 'heatmap' && heatmapData" class="heatmap-container">
      <!-- Hour labels -->
      <div class="hour-labels">
        <div class="day-label"></div>
        <div v-for="hour in 24" :key="hour" class="hour-label">
          {{ hour - 1 }}
        </div>
      </div>

      <!-- Heatmap grid -->
      <div v-for="(dayName, dayIndex) in dayNames" :key="dayIndex" class="heatmap-row">
        <div class="day-label">{{ dayName }}</div>
        <div 
          v-for="hour in 24" 
          :key="hour"
          class="heatmap-cell"
          :style="getCellStyle(dayIndex, hour - 1)"
          @mouseenter="showTooltip($event, dayIndex, hour - 1)"
          @mouseleave="hideTooltip"
        >
        </div>
      </div>
    </div>

    <!-- Table view -->
    <div v-else-if="viewMode === 'table' && heatmapData" class="table-container">
      <table class="activity-table">
        <thead>
          <tr>
            <th>Day</th>
            <th>Hour</th>
            <th>Minutes Active</th>
            <th>Most Played Map</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="cell in sortedTableData" :key="`${cell.dayOfWeek}-${cell.hour}`">
            <td>{{ dayNames[cell.dayOfWeek] }}</td>
            <td>{{ formatHourRange(cell.hour) }}</td>
            <td>{{ cell.minutesActive }}</td>
            <td>{{ cell.mostPlayedMap || '-' }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Tooltip -->
    <div 
      v-if="tooltip"
      class="heatmap-tooltip"
      :style="{ left: tooltip.x + 'px', top: tooltip.y + 'px' }"
    >
      <div class="tooltip-content">
        <strong>{{ tooltip.dayName }}, {{ tooltip.hourRange }}</strong>
        <div>{{ tooltip.minutes }} minutes active</div>
        <div v-if="tooltip.map">Most played: {{ tooltip.map }}</div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { fetchPlayerActivityHeatmap } from '../services/playerStatsApi';
import type { ActivityHeatmapResponse, HeatmapCell } from '../types/playerStatsTypes';

const props = defineProps<{
  playerName: string;
  game?: string;
}>();

const loading = ref(true);
const error = ref<string | null>(null);
const heatmapData = ref<ActivityHeatmapResponse | null>(null);
const viewMode = ref<'heatmap' | 'table'>('heatmap');
const tooltip = ref<{
  x: number;
  y: number;
  dayName: string;
  hourRange: string;
  minutes: number;
  map?: string;
} | null>(null);

const dayNames = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

// Create a map for quick cell lookup
const cellMap = computed(() => {
  if (!heatmapData.value) return new Map();
  
  const map = new Map<string, HeatmapCell>();
  heatmapData.value.cells.forEach(cell => {
    map.set(`${cell.dayOfWeek}-${cell.hour}`, cell);
  });
  return map;
});

// Find the maximum minutes for scaling
const maxMinutes = computed(() => {
  if (!heatmapData.value || heatmapData.value.cells.length === 0) return 1;
  return Math.max(...heatmapData.value.cells.map(c => c.minutesActive));
});

// Sorted data for table view
const sortedTableData = computed(() => {
  if (!heatmapData.value) return [];
  return [...heatmapData.value.cells]
    .filter(cell => cell.minutesActive > 0)
    .sort((a, b) => b.minutesActive - a.minutesActive);
});

function getCellStyle(dayOfWeek: number, hour: number) {
  const cell = cellMap.value.get(`${dayOfWeek}-${hour}`);
  if (!cell || cell.minutesActive === 0) {
    return { backgroundColor: 'var(--bg-panel)' };
  }

  // 5-step opacity scale
  const intensity = cell.minutesActive / maxMinutes.value;
  let opacity: number;
  
  if (intensity <= 0.2) opacity = 0.2;
  else if (intensity <= 0.4) opacity = 0.4;
  else if (intensity <= 0.6) opacity = 0.6;
  else if (intensity <= 0.8) opacity = 0.8;
  else opacity = 1.0;

  return {
    backgroundColor: `rgba(0, 217, 255, ${opacity})` // var(--neon-cyan) with opacity
  };
}

function formatHourRange(hour: number) {
  const start = hour === 0 ? '12am' : hour <= 12 ? `${hour}am` : `${hour - 12}pm`;
  const endHour = (hour + 1) % 24;
  const end = endHour === 0 ? '12am' : endHour <= 12 ? `${endHour}am` : `${endHour - 12}pm`;
  return `${start}-${end}`;
}

function showTooltip(event: MouseEvent, dayOfWeek: number, hour: number) {
  const cell = cellMap.value.get(`${dayOfWeek}-${hour}`);
  if (!cell || cell.minutesActive === 0) {
    hideTooltip();
    return;
  }

  const rect = (event.target as HTMLElement).getBoundingClientRect();
  tooltip.value = {
    x: rect.left + rect.width / 2,
    y: rect.top - 10,
    dayName: dayNames[dayOfWeek],
    hourRange: formatHourRange(hour),
    minutes: cell.minutesActive,
    map: cell.mostPlayedMap
  };
}

function hideTooltip() {
  tooltip.value = null;
}

async function loadData() {
  loading.value = true;
  error.value = null;

  try {
    heatmapData.value = await fetchPlayerActivityHeatmap(
      props.playerName,
      props.game || 'bf1942',
      90
    );
  } catch (err) {
    error.value = 'Failed to load activity heatmap';
    console.error(err);
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  loadData();
});
</script>

<style scoped>
.player-activity-heatmap {
  position: relative;
}

.view-toggle {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.toggle-btn {
  padding: 4px 12px;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  color: var(--text-secondary);
  font-size: 12px;
  cursor: pointer;
  transition: all 0.2s;
}

.toggle-btn.active {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  border-color: var(--neon-cyan);
}

.loading-state,
.error-state {
  padding: 32px;
  text-align: center;
  color: var(--text-secondary);
}

.error-state {
  color: var(--error-color);
}

/* Heatmap styles */
.heatmap-container {
  display: grid;
  grid-template-rows: auto repeat(7, 1fr);
  gap: 2px;
}

.hour-labels {
  display: grid;
  grid-template-columns: 40px repeat(24, 1fr);
  gap: 2px;
  margin-bottom: 2px;
}

.hour-label {
  font-size: 10px;
  text-align: center;
  color: var(--text-secondary);
}

.heatmap-row {
  display: grid;
  grid-template-columns: 40px repeat(24, 1fr);
  gap: 2px;
}

.day-label {
  font-size: 11px;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
}

.heatmap-cell {
  aspect-ratio: 1;
  background: var(--bg-panel);
  cursor: pointer;
  transition: transform 0.1s;
}

.heatmap-cell:hover {
  transform: scale(1.1);
  z-index: 1;
}

/* Table styles */
.table-container {
  max-height: 400px;
  overflow-y: auto;
}

.activity-table {
  width: 100%;
  border-collapse: collapse;
}

.activity-table th,
.activity-table td {
  padding: 8px;
  text-align: left;
  border-bottom: 1px solid var(--border-color);
}

.activity-table th {
  background: var(--bg-card);
  color: var(--text-secondary);
  font-weight: normal;
  font-size: 12px;
  text-transform: uppercase;
  position: sticky;
  top: 0;
}

.activity-table td {
  font-size: 13px;
}

/* Tooltip */
.heatmap-tooltip {
  position: fixed;
  z-index: 1000;
  pointer-events: none;
  transform: translate(-50%, -100%);
}

.tooltip-content {
  background: var(--bg-dark);
  border: 1px solid var(--border-color);
  padding: 8px 12px;
  font-size: 12px;
  white-space: nowrap;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.tooltip-content strong {
  color: var(--neon-cyan);
}

.tooltip-content div {
  color: var(--text-secondary);
  margin-top: 2px;
}
</style>