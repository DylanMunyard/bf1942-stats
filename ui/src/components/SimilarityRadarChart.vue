<script setup lang="ts">
import { computed } from 'vue'
import { Radar } from 'vue-chartjs'
import {
  Chart as ChartJS,
  RadarController,
  RadialLinearScale,
  PointElement,
  LineElement,
  Filler,
  Tooltip,
  Legend
} from 'chart.js'

ChartJS.register(RadarController, RadialLinearScale, PointElement, LineElement, Filler, Tooltip, Legend)

interface StatAnalysis {
  score: number
  kdRatioDifference: number
  killRateDifference: number
  mapPerformanceSimilarity: number
  serverPerformanceSimilarity: number
}

interface BehavioralAnalysis {
  score: number
  playTimeOverlapScore: number
  serverAffinityScore: number
  pingConsistencyScore: number
  sessionPatternScore: number
}

interface NetworkAnalysis {
  score: number
  sharedTeammateCount: number
  teammateOverlapPercentage: number
  hasDirectConnection: boolean
}

interface TemporalAnalysis {
  score: number
  temporalOverlapMinutes: number
  invertedActivityScore: number
  significantTemporalOverlap: boolean
}

interface Props {
  statAnalysis: StatAnalysis
  behavioralAnalysis: BehavioralAnalysis
  networkAnalysis: NetworkAnalysis
  temporalAnalysis: TemporalAnalysis
  isDarkMode?: boolean
  chartKey?: number
}

const props = withDefaults(defineProps<Props>(), {
  isDarkMode: true,
  chartKey: 0
})

// Extract key metrics from each analysis
const metricsData = computed(() => {
  return {
    'Stats Profile': props.statAnalysis.score,
    'K/D Similarity': props.statAnalysis.kdRatioDifference,
    'Kill Rate Match': props.statAnalysis.killRateDifference,
    'Map Performance': props.statAnalysis.mapPerformanceSimilarity,
    'Server Performance': props.statAnalysis.serverPerformanceSimilarity,
    'Play Time Overlap': props.behavioralAnalysis.playTimeOverlapScore,
    'Server Affinity': props.behavioralAnalysis.serverAffinityScore,
    'Ping Consistency': props.behavioralAnalysis.pingConsistencyScore,
    'Session Pattern': props.behavioralAnalysis.sessionPatternScore,
    'Network Match': props.networkAnalysis.score,
    'Teammate Overlap': props.networkAnalysis.teammateOverlapPercentage,
    'Temporal Consistency': props.temporalAnalysis.score
  }
})

const labels = computed(() => Object.keys(metricsData.value))

const chartData = computed(() => {
  const dataValues = Object.values(metricsData.value).map(v => Math.round(v * 100))

  return {
    labels: labels.value,
    datasets: [
      {
        label: 'Similarity Score',
        data: dataValues,
        borderColor: '#22d3ee',
        backgroundColor: 'rgba(34, 211, 238, 0.25)',
        borderWidth: 2.5,
        pointBackgroundColor: '#22d3ee',
        pointBorderColor: '#fff',
        pointBorderWidth: 2,
        pointRadius: 5,
        pointHoverRadius: 7,
        fill: true,
        tension: 0.3
      }
    ]
  }
})

const chartOptions = computed(() => ({
  responsive: true,
  maintainAspectRatio: true,
  scales: {
    r: {
      beginAtZero: true,
      max: 100,
      ticks: {
        color: props.isDarkMode ? '#94a3b8' : '#475569',
        backdropColor: 'transparent',
        callback: (value: string | number) => `${value}%`,
        font: {
          size: 11
        }
      },
      grid: {
        color: props.isDarkMode ? 'rgba(148, 163, 184, 0.15)' : 'rgba(15, 23, 42, 0.08)'
      },
      pointLabels: {
        color: props.isDarkMode ? '#cbd5e1' : '#1f2937',
        font: {
          size: 12,
          weight: 600
        },
        padding: 12
      }
    }
  },
  plugins: {
    legend: {
      display: false
    },
    tooltip: {
      backgroundColor: 'rgba(15, 23, 42, 0.9)',
      titleColor: '#e2e8f0',
      bodyColor: '#cbd5e1',
      borderColor: 'rgba(34, 211, 238, 0.5)',
      borderWidth: 1,
      padding: 12,
      titleFont: { size: 13, weight: 'bold' },
      bodyFont: { size: 12 },
      callbacks: {
        label: function(context: any) {
          return `Match: ${context.parsed.r}%`
        }
      }
    }
  }
}))

// Calculate average and min/max scores
const stats = computed(() => {
  const values = Object.values(metricsData.value)
  const avg = values.reduce((a, b) => a + b, 0) / values.length
  const max = Math.max(...values)
  const min = Math.min(...values)

  return {
    avgScore: (avg * 100).toFixed(1),
    maxScore: (max * 100).toFixed(0),
    minScore: (min * 100).toFixed(0),
    variance: (max - min).toFixed(2)
  }
})

// Identify strongest and weakest areas
const topMetrics = computed(() => {
  const entries = Object.entries(metricsData.value)
  const sorted = [...entries].sort((a, b) => b[1] - a[1])
  return sorted.slice(0, 3)
})

const weakMetrics = computed(() => {
  const entries = Object.entries(metricsData.value)
  const sorted = [...entries].sort((a, b) => a[1] - b[1])
  return sorted.slice(0, 2)
})
</script>

<template>
  <div class="radar-container">
    <!-- Stats Overview -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-label">Average Match</div>
        <div class="stat-value" :class="{ 'high-score': parseFloat(stats.avgScore) > 75 }">
          {{ stats.avgScore }}%
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Highest Match</div>
        <div class="stat-value high-score">{{ stats.maxScore }}%</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Lowest Match</div>
        <div class="stat-value low-score">{{ stats.minScore }}%</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">Score Variance</div>
        <div class="stat-value">{{ stats.variance }}</div>
      </div>
    </div>

    <!-- Chart Container -->
    <div class="chart-wrapper">
      <Radar
        :key="chartKey"
        :data="chartData"
        :options="chartOptions"
        :style="{ position: 'relative', height: '400px' }"
      />
    </div>

    <!-- Analysis Summary -->
    <div class="analysis-summary">
      <div class="summary-section">
        <h4 class="summary-title">Strongest Indicators</h4>
        <ul class="metric-list">
          <li v-for="[metric, score] in topMetrics" :key="metric" class="metric-item">
            <span class="metric-name">{{ metric }}</span>
            <span class="metric-bar-container">
              <span class="metric-bar" :style="{ width: (score * 100) + '%' }"></span>
            </span>
            <span class="metric-percentage">{{ (score * 100).toFixed(0) }}%</span>
          </li>
        </ul>
      </div>

      <div class="summary-section">
        <h4 class="summary-title">Areas of Divergence</h4>
        <ul class="metric-list">
          <li v-for="[metric, score] in weakMetrics" :key="metric" class="metric-item">
            <span class="metric-name">{{ metric }}</span>
            <span class="metric-bar-container">
              <span class="metric-bar divergence" :style="{ width: (score * 100) + '%' }"></span>
            </span>
            <span class="metric-percentage">{{ (score * 100).toFixed(0) }}%</span>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>

<style scoped>
.radar-container {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 1rem;
}

.stat-card {
  padding: 1rem;
  background: rgba(148, 163, 184, 0.05);
  border: 1px solid rgba(148, 163, 184, 0.2);
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}

.stat-label {
  font-size: 0.75rem;
  color: #94a3b8;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  font-weight: 600;
  text-align: center;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  color: #cbd5e1;
  font-family: 'courier new', monospace;
}

.stat-value.high-score {
  color: #22d3ee;
}

.stat-value.low-score {
  color: #f87171;
}

.chart-wrapper {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 400px;
}

.analysis-summary {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 2rem;
  padding: 1.5rem;
  background: rgba(148, 163, 184, 0.05);
  border-radius: 6px;
}

.summary-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.summary-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: #cbd5e1;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin: 0;
}

.metric-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.metric-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.85rem;
}

.metric-name {
  min-width: 140px;
  color: #cbd5e1;
  font-weight: 500;
}

.metric-bar-container {
  flex: 1;
  height: 6px;
  background: rgba(148, 163, 184, 0.2);
  border-radius: 3px;
  overflow: hidden;
}

.metric-bar {
  display: block;
  height: 100%;
  background: linear-gradient(to right, #22d3ee, #06b6d4);
  border-radius: 3px;
  transition: all 0.3s ease;
}

.metric-bar.divergence {
  background: linear-gradient(to right, #f87171, #ef4444);
}

.metric-percentage {
  min-width: 35px;
  text-align: right;
  color: #94a3b8;
  font-weight: 600;
  font-family: 'courier new', monospace;
}

@media (max-width: 768px) {
  .stats-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .analysis-summary {
    grid-template-columns: 1fr;
    gap: 1.5rem;
  }

  .chart-wrapper {
    min-height: 300px;
  }
}
</style>
