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
          {{ isPlaying ? '❚❚ PAUSE' : '▶ PLAY' }}
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

      <!-- Chart container -->
      <div class="chart-container">
        <canvas ref="chartCanvas"></canvas>
      </div>
    </div>

    <!-- No data state -->
    <div v-else class="no-data">
      No map performance data available
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue';
import { fetchMapPerformanceTimeline } from '@/services/playerStatsApi';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
import type { MapPerformanceTimelineResponse } from '@/types/playerStatsTypes';

Chart.register(...registerables);

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
const playbackSpeed = ref(1500); // ms between frames (slower for fewer periods)

const chartCanvas = ref<HTMLCanvasElement>();
let chart: Chart<'bar'> | null = null;
let playbackInterval: number | null = null;

const metrics = [
  { value: 'kdRatio', label: 'K/D' },
  { value: 'score', label: 'SCORE' },
  { value: 'kills', label: 'KILLS' }
];

// Get current month data
const currentMonth = computed(() => {
  if (!timelineData.value) return null;
  return timelineData.value.months[currentMonthIndex.value];
});

// Get top 10 maps for current month sorted by selected metric
const topMaps = computed(() => {
  if (!currentMonth.value) return [];
  
  const sorted = [...currentMonth.value.maps].sort((a, b) => {
    switch (selectedMetric.value) {
      case 'kdRatio':
        return b.kdRatio - a.kdRatio;
      case 'kills':
        return b.kills - a.kills;
      case 'score':
      default:
        return b.score - a.score;
    }
  });
  
  return sorted.slice(0, 10);
});

// Generate stable color for each map based on string hash
function getMapColor(mapName: string): string {
  let hash = 0;
  for (let i = 0; i < mapName.length; i++) {
    hash = mapName.charCodeAt(i) + ((hash << 5) - hash);
  }
  
  const hue = Math.abs(hash % 360);
  return `hsla(${hue}, 70%, 50%, 0.8)`;
}

// Generate chart data
const chartData = computed(() => {
  const labels = topMaps.value.map(m => m.mapName);
  const data = topMaps.value.map(m => {
    switch (selectedMetric.value) {
      case 'kdRatio':
        return m.kdRatio;
      case 'kills':
        return m.kills;
      case 'score':
      default:
        return m.score;
    }
  });

  const colors = topMaps.value.map(m => getMapColor(m.mapName));

  return {
    labels,
    datasets: [{
      data,
      backgroundColor: colors,
      borderColor: colors.map(c => c.replace('0.8', '1')),
      borderWidth: 1
    }]
  };
});

// Chart configuration
const chartConfig = computed<ChartConfiguration<'bar'>>(() => ({
  type: 'bar',
  data: chartData.value,
  options: {
    indexAxis: 'y',
    responsive: true,
    maintainAspectRatio: false,
    animation: {
      duration: 400,
      easing: 'easeOutQuart'
    },
    plugins: {
      legend: {
        display: false
      },
      tooltip: {
        backgroundColor: 'rgba(0, 0, 0, 0.9)',
        titleColor: '#F59E0B',
        bodyColor: '#ffffff',
        borderColor: '#30363d',
        borderWidth: 1,
        padding: 12,
        displayColors: false,
        callbacks: {
          title: (tooltipItems) => {
            const map = topMaps.value[tooltipItems[0].dataIndex];
            return map.mapName;
          },
          label: (tooltipItem) => {
            const map = topMaps.value[tooltipItem.dataIndex];
            const lines = [
              `K/D: ${map.kdRatio.toFixed(2)}`,
              `Score: ${map.score.toLocaleString()}`,
              `Kills: ${map.kills.toLocaleString()}`,
              `Deaths: ${map.deaths.toLocaleString()}`,
              `Sessions: ${map.sessions}`,
              `Time: ${Math.round(map.playTimeMinutes)}m`
            ];
            return lines;
          }
        }
      }
    },
    scales: {
      x: {
        grid: {
          color: 'rgba(48, 54, 61, 0.5)',
          borderColor: '#30363d'
        },
        ticks: {
          color: '#8b949e',
          callback: function(value) {
            switch (selectedMetric.value) {
              case 'kdRatio':
                return Number(value).toFixed(2);
              default:
                return value.toLocaleString();
            }
          }
        }
      },
      y: {
        grid: {
          display: false
        },
        ticks: {
          color: '#8b949e',
          font: {
            size: 11
          }
        }
      }
    }
  }
}));

// Create or update chart
function updateChart() {
  if (!chartCanvas.value) return;

  if (chart) {
    chart.data = chartData.value;
    // Use 'none' to skip animations and avoid jarring re-animation from 0
    chart.update('none');
  } else {
    chart = new Chart(chartCanvas.value, chartConfig.value);
  }
}

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

  // Auto-restart if at the end
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

// Load timeline data
async function loadData() {
  loading.value = true;
  error.value = null;

  try {
    timelineData.value = await fetchMapPerformanceTimeline(
      props.playerName,
      props.game || 'bf1942',
      12 // 12 months
    );

    // Start from the beginning
    currentMonthIndex.value = 0;

    // Set loading to false first to render the DOM
    loading.value = false;

    // Wait for DOM to update with canvas
    await nextTick();

    // Initialize chart after DOM is ready
    updateChart();
  } catch (err) {
    error.value = 'Failed to load map performance timeline';
    console.error(err);
    loading.value = false;
  }
}

// Watch for changes that require chart update
watch([currentMonthIndex, selectedMetric], () => {
  updateChart();
});

onMounted(() => {
  loadData();
});

onUnmounted(() => {
  stopPlayback();
  if (chart) {
    chart.destroy();
  }
});
</script>

<style scoped>
.map-performance-race {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.loading-state,
.error-state,
.no-data {
  padding: 32px;
  text-align: center;
  color: var(--text-secondary);
}

.error-state {
  color: var(--error-color);
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
  cursor: pointer;
  transition: all 0.2s;
}

.control-btn:hover {
  border-color: var(--neon-cyan);
  color: var(--neon-cyan);
}

.control-btn.play {
  background: rgba(0, 217, 255, 0.1);
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
  cursor: pointer;
  transition: all 0.2s;
}

.metric-btn.active {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  border-color: var(--neon-cyan);
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

.chart-container {
  flex: 1;
  min-height: 350px;
  position: relative;
}

canvas {
  width: 100% !important;
  height: 100% !important;
}
</style>