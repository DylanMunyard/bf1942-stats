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

    <!-- Chart container -->
    <div class="chart-container">
      <canvas ref="chartCanvas"></canvas>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { Chart, ChartConfiguration, registerables } from 'chart.js';
// Define MapRanking interface locally since it's not exported
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

Chart.register(...registerables);

const props = defineProps<{
  rankings: MapRanking[];
  sortBy?: 'kdRatio' | 'kills' | 'timePlayed' | 'score';
}>();

const chartCanvas = ref<HTMLCanvasElement>();
let chart: Chart<'bar'> | null = null;

const sortBy = ref<'kdRatio' | 'kills' | 'timePlayed' | 'score'>(props.sortBy || 'kdRatio');

const metrics = [
  { value: 'kdRatio', label: 'K/D' },
  { value: 'kills', label: 'KILLS' },
  { value: 'timePlayed', label: 'TIME' },
  { value: 'score', label: 'SCORE' }
];

// Get top 15 maps sorted by selected metric
const topMaps = computed(() => {
  const sorted = [...props.rankings].sort((a, b) => {
    switch (sortBy.value) {
      case 'kdRatio':
        return b.kdRatio - a.kdRatio;
      case 'kills':
        return b.kills - a.kills;
      case 'timePlayed':
        return b.timePlayed - a.timePlayed;
      case 'score':
      default:
        return b.score - a.score;
    }
  });
  
  return sorted.slice(0, 15);
});

// Generate chart data
const chartData = computed(() => {
  const labels = topMaps.value.map(m => m.mapName);
  const data = topMaps.value.map(m => {
    switch (sortBy.value) {
      case 'kdRatio':
        return m.kdRatio;
      case 'kills':
        return m.totalKills;
      case 'timePlayed':
        return m.playTimeMinutes;
      case 'score':
      default:
        return m.totalScore;
    }
  });

  // Generate colors - gold for #1 rank, gradient sky-blue for others
  const colors = topMaps.value.map((map, index) => {
    if (map.rank === 1) {
      return 'rgba(251, 191, 36, 0.8)'; // Material Design amber-400
    }
    // Gradient from bright to dim sky-blue based on position
    const opacity = 0.8 - (index * 0.04);
    return `rgba(245, 158, 11, ${Math.max(opacity, 0.3)})`;
  });

  return {
    labels,
    datasets: [{
      data,
      backgroundColor: colors,
      borderColor: colors.map(c => c.replace('0.8', '1').replace('0.3', '0.5')),
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
              `Rank: #${map.rank} (${map.percentile}th percentile)`,
              `Score: ${map.totalScore.toLocaleString()}`,
              `Kills: ${map.totalKills.toLocaleString()}`,
              `Deaths: ${map.totalDeaths.toLocaleString()}`,
              `K/D: ${map.kdRatio.toFixed(2)}`,
              `Time: ${Math.round(map.playTimeMinutes / 60)}h`
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
            switch (sortBy.value) {
              case 'kdRatio':
                return Number(value).toFixed(2);
              case 'timePlayed':
                return `${Math.round(Number(value) / 60)}h`;
              case 'kills':
              case 'score':
              default:
                return Number(value).toLocaleString();
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
function createChart() {
  if (!chartCanvas.value) return;

  if (chart) {
    chart.data = chartData.value;
    chart.options = chartConfig.value.options;
    chart.update('active');
  } else {
    chart = new Chart(chartCanvas.value, chartConfig.value);
  }
}

// Watch for changes
watch([sortBy, () => props.rankings], () => {
  createChart();
});

onMounted(() => {
  createChart();
});

onUnmounted(() => {
  if (chart) {
    chart.destroy();
  }
});
</script>

<style scoped>
.competitive-rankings-chart {
  height: 100%;
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

.chart-container {
  flex: 1;
  min-height: 400px;
  position: relative;
}

canvas {
  width: 100% !important;
  height: 100% !important;
}
</style>