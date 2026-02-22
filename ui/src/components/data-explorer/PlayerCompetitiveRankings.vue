<template>
  <div class="competitive-rankings">
    <!-- Loading State -->
    <div v-if="isLoading" class="flex flex-col gap-4">
      <div class="explorer-skeleton" style="height: 2.5rem"></div>
      <div class="explorer-skeleton" style="height: 10rem"></div>
      <div class="explorer-skeleton" style="height: 15rem"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="explorer-empty">
      <div class="explorer-empty-icon text-neon-red">!</div>
      <p class="explorer-empty-title text-neon-red">{{ error }}</p>
      <button @click="loadData()" class="explorer-btn explorer-btn--ghost explorer-btn--sm mt-4">
        Try again
      </button>
    </div>

    <!-- Content -->
    <div v-else-if="rankingsData">
      <!-- Summary Hero Stats -->
      <div class="rankings-hero mb-6">
        <div class="rankings-hero-badge" :class="getBadgeClass(rankingsData.summary.percentileCategory)">
          <span class="rankings-hero-icon">{{ getBadgeIcon(rankingsData.summary.percentileCategory) }}</span>
          <span class="rankings-hero-label">{{ getBadgeLabel(rankingsData.summary.percentileCategory) }}</span>
        </div>
        
        <div class="rankings-hero-stats">
          <div class="rankings-hero-stat">
            <div class="rankings-hero-value text-neon-gold">{{ rankingsData.summary.top1Rankings }}</div>
            <div class="rankings-hero-label">🥇 #1 RANKS</div>
          </div>
          <div class="rankings-hero-stat">
            <div class="rankings-hero-value text-neon-cyan">{{ rankingsData.summary.top10Rankings }}</div>
            <div class="rankings-hero-label">TOP 10</div>
          </div>
          <div class="rankings-hero-stat">
            <div class="rankings-hero-value">{{ rankingsData.summary.averagePercentile.toFixed(1) }}%</div>
            <div class="rankings-hero-label">AVG PERCENTILE</div>
          </div>
        </div>
      </div>

      <!-- Tab Navigation -->
      <div class="explorer-tabs mb-4">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          class="explorer-tab"
          :class="{ 'explorer-tab--active': activeTab === tab.id }"
          @click="activeTab = tab.id"
        >
          {{ tab.label }}
        </button>
      </div>

      <!-- Current Rankings Tab -->
      <div v-if="activeTab === 'current'" class="space-y-2">
        <div 
          v-for="ranking in sortedRankings"
          :key="ranking.mapName"
          class="ranking-item"
          @click="navigateToMapRankings(ranking.mapName)"
        >
          <div class="ranking-position">
            <div class="ranking-badge" :class="getRankBadgeClass(ranking.rank)">
              <span v-if="ranking.rank === 1">🥇</span>
              <span v-else-if="ranking.rank === 2">🥈</span>
              <span v-else-if="ranking.rank === 3">🥉</span>
              <span v-else>#{{ ranking.rank }}</span>
            </div>
            <div class="ranking-trend" :class="getTrendClass(ranking.trend)">
              <span v-if="ranking.trend === 'up'">↑</span>
              <span v-else-if="ranking.trend === 'down'">↓</span>
              <span v-else-if="ranking.trend === 'stable'">→</span>
              <span v-else>★</span>
              <span v-if="ranking.previousRank" class="text-xs ml-1">{{ Math.abs(ranking.rank - ranking.previousRank) }}</span>
            </div>
          </div>

          <div class="ranking-details">
            <div class="ranking-map">{{ ranking.mapName }}</div>
            <div class="ranking-stats">
              <span class="ranking-stat">
                <span class="text-neutral-500">Score:</span>
                <span class="font-mono">{{ ranking.totalScore.toLocaleString() }}</span>
              </span>
              <span class="ranking-stat">
                <span class="text-neutral-500">K/D:</span>
                <span class="font-mono">{{ ranking.kdRatio.toFixed(2) }}</span>
              </span>
              <span class="ranking-stat">
                <span class="text-neutral-500">Time:</span>
                <span class="font-mono">{{ formatPlayTime(ranking.playTimeMinutes) }}</span>
              </span>
            </div>
          </div>

          <div class="ranking-percentile">
            <div class="percentile-badge" :class="getPercentileClass(ranking.percentile)">
              TOP {{ (100 - ranking.percentile).toFixed(1) }}%
            </div>
            <div class="text-xs text-neutral-500 font-mono mt-1">
              of {{ ranking.totalPlayers }} players
            </div>
          </div>
        </div>

        <!-- Empty state for no rankings -->
        <div v-if="rankingsData.mapRankings.length === 0" class="explorer-empty">
          <p class="text-neutral-500">No competitive rankings available for this time period.</p>
        </div>
      </div>

      <!-- Timeline Tab -->
      <div v-else-if="activeTab === 'timeline'" class="timeline-content">
        <!-- Map Selector -->
        <div class="mb-4">
          <select 
            v-model="selectedTimelineMap" 
            @change="loadTimeline"
            class="explorer-select w-full sm:w-auto"
          >
            <option value="">All Maps (Average)</option>
            <option v-for="map in availableMaps" :key="map" :value="map">
              {{ map }}
            </option>
          </select>
        </div>

        <!-- Timeline Chart -->
        <div v-if="timelineData && timelineData.timeline.length > 0" class="timeline-chart-container">
          <div class="timeline-chart">
            <canvas ref="timelineCanvas"></canvas>
          </div>
        </div>

        <!-- Loading state for timeline -->
        <div v-else-if="isTimelineLoading" class="flex justify-center py-8">
          <div class="explorer-spinner" />
        </div>

        <!-- No timeline data -->
        <div v-else class="explorer-empty">
          <p class="text-neutral-500">No historical ranking data available.</p>
        </div>
      </div>
    </div>

    <!-- No Data State -->
    <div v-else class="explorer-empty">
      <div class="explorer-empty-icon">📊</div>
      <p class="explorer-empty-title">NO RANKING DATA</p>
      <p class="explorer-empty-desc">Play more matches to establish your competitive rankings.</p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, nextTick, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import Chart from 'chart.js/auto';
import type { ChartConfiguration } from 'chart.js';

const props = defineProps<{
  playerName: string;
  game?: string;
}>();

const router = useRouter();

// Types
interface MapRanking {
  mapName: string;
  rank: number;
  totalPlayers: number;
  percentile: number;
  totalScore: number;
  totalKills: number;
  totalDeaths: number;
  kdRatio: number;
  totalRounds: number;
  playTimeMinutes: number;
  trend: 'up' | 'down' | 'stable' | 'new';
  previousRank?: number;
}

interface RankingSummary {
  totalMapsPlayed: number;
  top1Rankings: number;
  top10Rankings: number;
  top25Rankings: number;
  top100Rankings: number;
  averagePercentile: number;
  bestRankedMap?: string;
  bestRank?: number;
  percentileCategory: 'elite' | 'master' | 'expert' | 'veteran' | 'regular';
}

interface CompetitiveRankingsResponse {
  playerName: string;
  game: string;
  mapRankings: MapRanking[];
  summary: RankingSummary;
  dateRange: {
    days: number;
    fromDate: string;
    toDate: string;
  };
}

interface TimelineSnapshot {
  year: number;
  month: number;
  monthLabel: string;
  rank: number;
  totalPlayers: number;
  percentile: number;
  totalScore: number;
  kdRatio: number;
  hasData: boolean;
}

interface RankingTimelineResponse {
  playerName: string;
  mapName?: string;
  game: string;
  timeline: TimelineSnapshot[];
}

// State
const tabs = [
  { id: 'current', label: 'CURRENT RANKINGS' },
  { id: 'timeline', label: 'RANK TIMELINE' }
];

const activeTab = ref<'current' | 'timeline'>('current');
const rankingsData = ref<CompetitiveRankingsResponse | null>(null);
const timelineData = ref<RankingTimelineResponse | null>(null);
const isLoading = ref(false);
const isTimelineLoading = ref(false);
const error = ref<string | null>(null);
const selectedTimelineMap = ref('');
const timelineCanvas = ref<HTMLCanvasElement | null>(null);
let timelineChart: Chart | null = null;

// Computed
const sortedRankings = computed(() => {
  if (!rankingsData.value) return [];
  return [...rankingsData.value.mapRankings].sort((a, b) => a.rank - b.rank);
});

const availableMaps = computed(() => {
  if (!rankingsData.value) return [];
  return rankingsData.value.mapRankings.map(r => r.mapName).sort();
});

// Methods
const loadData = async () => {
  isLoading.value = true;
  error.value = null;

  try {
    const response = await fetch(
      `/stats/data-explorer/players/${encodeURIComponent(props.playerName)}/competitive-rankings?` +
      `game=${props.game || 'bf1942'}&days=60`
    );

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error('No ranking data found for this player');
      }
      throw new Error('Failed to load competitive rankings');
    }

    rankingsData.value = await response.json();
  } catch (err: any) {
    console.error('Error loading competitive rankings:', err);
    error.value = err.message || 'Failed to load rankings';
  } finally {
    isLoading.value = false;
  }
};

const loadTimeline = async () => {
  isTimelineLoading.value = true;

  try {
    const params = new URLSearchParams({
      game: props.game || 'bf1942',
      months: '12'
    });
    
    if (selectedTimelineMap.value) {
      params.append('mapName', selectedTimelineMap.value);
    }

    const response = await fetch(
      `/stats/data-explorer/players/${encodeURIComponent(props.playerName)}/ranking-timeline?${params}`
    );

    if (!response.ok) {
      throw new Error('Failed to load ranking timeline');
    }

    timelineData.value = await response.json();
    await nextTick();
    drawTimelineChart();
  } catch (err: any) {
    console.error('Error loading timeline:', err);
  } finally {
    isTimelineLoading.value = false;
  }
};

const drawTimelineChart = () => {
  if (!timelineCanvas.value || !timelineData.value) return;

  // Destroy existing chart
  if (timelineChart) {
    timelineChart.destroy();
  }

  const validData = timelineData.value.timeline.filter(t => t.hasData);
  if (validData.length === 0) return;

  const ctx = timelineCanvas.value.getContext('2d');
  if (!ctx) return;

  const isDarkMode = document.documentElement.classList.contains('dark-mode');

  const config: ChartConfiguration = {
    type: 'line',
    data: {
      labels: validData.map(t => t.monthLabel).reverse(),
      datasets: [
        {
          label: 'Rank',
          data: validData.map(t => t.rank).reverse(),
          borderColor: '#00fff2',
          backgroundColor: 'rgba(0, 255, 242, 0.1)',
          borderWidth: 2,
          fill: true,
          tension: 0.3,
          pointRadius: 4,
          pointBackgroundColor: '#00fff2',
          pointBorderColor: '#ffffff',
          pointBorderWidth: 2,
          pointHoverRadius: 6,
          yAxisID: 'y-rank'
        },
        {
          label: 'Percentile',
          data: validData.map(t => t.percentile).reverse(),
          borderColor: '#ff00ff',
          backgroundColor: 'rgba(255, 0, 255, 0.1)',
          borderWidth: 2,
          fill: false,
          tension: 0.3,
          pointRadius: 3,
          pointBackgroundColor: '#ff00ff',
          yAxisID: 'y-percentile'
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: {
        mode: 'index',
        intersect: false
      },
      plugins: {
        legend: {
          display: true,
          labels: {
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' }
          }
        },
        tooltip: {
          backgroundColor: isDarkMode ? 'rgba(35, 21, 53, 0.95)' : 'rgba(0, 0, 0, 0.9)',
          titleColor: '#ffffff',
          bodyColor: '#ffffff',
          borderColor: '#00fff2',
          borderWidth: 1,
          cornerRadius: 6,
          displayColors: true,
          callbacks: {
            afterLabel: (context) => {
              const dataIndex = context.dataIndex;
              const snapshot = validData[validData.length - 1 - dataIndex];
              return [
                `Score: ${snapshot.totalScore.toLocaleString()}`,
                `K/D: ${snapshot.kdRatio.toFixed(2)}`,
                `Players: ${snapshot.totalPlayers}`
              ];
            }
          }
        }
      },
      scales: {
        'y-rank': {
          type: 'linear',
          display: true,
          position: 'left',
          reverse: true, // Lower rank is better
          title: {
            display: true,
            text: 'Rank Position',
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' }
          },
          ticks: {
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' }
          },
          grid: {
            color: isDarkMode ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.1)'
          }
        },
        'y-percentile': {
          type: 'linear',
          display: true,
          position: 'right',
          title: {
            display: true,
            text: 'Top %',
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' }
          },
          ticks: {
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' },
            callback: (value) => `${value}%`
          },
          grid: {
            display: false
          }
        },
        x: {
          ticks: {
            color: isDarkMode ? '#ffffff' : '#000000',
            font: { family: 'JetBrains Mono' }
          },
          grid: {
            color: isDarkMode ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.1)'
          }
        }
      }
    }
  };

  timelineChart = new Chart(ctx, config);
};

// Helper functions
const formatPlayTime = (minutes: number): string => {
  const hours = Math.floor(minutes / 60);
  return hours > 0 ? `${hours}h` : `${Math.floor(minutes)}m`;
};

const navigateToMapRankings = (mapName: string) => {
  router.push({
    path: `/maps/${encodeURIComponent(mapName)}`,
    query: { game: props.game || 'bf1942', highlight: props.playerName }
  });
};

const getRankBadgeClass = (rank: number): string => {
  if (rank === 1) return 'rank-gold';
  if (rank === 2) return 'rank-silver';
  if (rank === 3) return 'rank-bronze';
  if (rank <= 10) return 'rank-top10';
  if (rank <= 25) return 'rank-top25';
  return '';
};

const getPercentileClass = (percentile: number): string => {
  if (percentile >= 99) return 'percentile-elite';
  if (percentile >= 95) return 'percentile-master';
  if (percentile >= 90) return 'percentile-expert';
  if (percentile >= 75) return 'percentile-veteran';
  return '';
};

const getTrendClass = (trend: string): string => {
  switch (trend) {
    case 'up': return 'trend-up';
    case 'down': return 'trend-down';
    case 'new': return 'trend-new';
    default: return 'trend-stable';
  }
};

const getBadgeClass = (category: string): string => {
  return `badge-${category}`;
};

const getBadgeIcon = (category: string): string => {
  switch (category) {
    case 'elite': return '👑';
    case 'master': return '⭐';
    case 'expert': return '💎';
    case 'veteran': return '🎖️';
    default: return '🎯';
  }
};

const getBadgeLabel = (category: string): string => {
  return category.toUpperCase();
};

// Lifecycle
onMounted(() => {
  loadData();
});

onUnmounted(() => {
  if (timelineChart) {
    timelineChart.destroy();
  }
});

watch(activeTab, (newTab) => {
  if (newTab === 'timeline' && !timelineData.value) {
    loadTimeline();
  }
});

watch(() => props.playerName, () => {
  loadData();
  timelineData.value = null;
  selectedTimelineMap.value = '';
});
</script>

<style scoped>
/* Base styles following explorer theme */
.competitive-rankings {
  font-family: 'JetBrains Mono', monospace;
}

/* Hero section */
.rankings-hero {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 1.5rem;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  margin-bottom: 1.5rem;
}

.rankings-hero-badge {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  font-size: 0.875rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  border-radius: 20px;
  margin-bottom: 1rem;
}

.badge-elite {
  background: linear-gradient(135deg, var(--neon-gold) 0%, rgba(255, 215, 0, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(255, 215, 0, 0.5);
}

.badge-master {
  background: linear-gradient(135deg, var(--neon-cyan) 0%, rgba(0, 255, 242, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(0, 255, 242, 0.5);
}

.badge-expert {
  background: linear-gradient(135deg, var(--neon-pink) 0%, rgba(255, 0, 255, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(255, 0, 255, 0.5);
}

.badge-veteran {
  background: var(--bg-panel);
  color: var(--text-primary);
  border: 2px solid var(--neon-cyan);
}

.badge-regular {
  background: var(--bg-panel);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
}

.rankings-hero-icon {
  font-size: 1.25rem;
}

.rankings-hero-stats {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 2rem;
  width: 100%;
  max-width: 24rem;
}

.rankings-hero-stat {
  text-align: center;
}

.rankings-hero-value {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.rankings-hero-label {
  font-size: 0.625rem;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

/* Tabs */
.explorer-tabs {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid var(--border-color);
  margin-bottom: 1rem;
}

.explorer-tab {
  padding: 0.5rem 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-secondary);
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  transition: all 0.2s ease;
}

.explorer-tab:hover {
  color: var(--text-primary);
}

.explorer-tab--active {
  color: var(--neon-cyan);
  border-bottom-color: var(--neon-cyan);
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.5);
}

/* Ranking items */
.ranking-item {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.ranking-item:hover {
  border-color: rgba(0, 255, 242, 0.3);
  transform: translateX(4px);
  box-shadow: 0 0 20px rgba(0, 255, 242, 0.1);
}

.ranking-position {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.ranking-badge {
  width: 3rem;
  height: 3rem;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.875rem;
  font-weight: 700;
  background: var(--bg-panel);
  border: 2px solid var(--border-color);
  border-radius: 8px;
}

.rank-gold {
  color: var(--neon-gold);
  border-color: var(--neon-gold);
  box-shadow: 0 0 15px rgba(255, 215, 0, 0.3);
}

.rank-silver {
  color: #c0c0c0;
  border-color: #c0c0c0;
}

.rank-bronze {
  color: #cd7f32;
  border-color: #cd7f32;
}

.rank-top10 {
  color: var(--neon-cyan);
  border-color: var(--neon-cyan);
}

.rank-top25 {
  color: var(--neon-pink);
  border-color: var(--neon-pink);
}

.ranking-trend {
  display: flex;
  align-items: center;
  font-size: 0.75rem;
  font-weight: 600;
}

.trend-up {
  color: var(--neon-green);
}

.trend-down {
  color: var(--neon-red);
}

.trend-stable {
  color: var(--text-secondary);
}

.trend-new {
  color: var(--neon-gold);
}

.ranking-details {
  flex: 1;
  min-width: 0;
}

.ranking-map {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 0.25rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.ranking-stats {
  display: flex;
  gap: 1rem;
  font-size: 0.625rem;
}

.ranking-stat {
  display: flex;
  gap: 0.25rem;
}

.ranking-percentile {
  text-align: right;
}

.percentile-badge {
  padding: 0.25rem 0.5rem;
  font-size: 0.625rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  border-radius: 4px;
  text-transform: uppercase;
}

.percentile-elite {
  background: var(--neon-gold);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(255, 215, 0, 0.4);
}

.percentile-master {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(0, 255, 242, 0.4);
}

.percentile-expert {
  background: var(--neon-pink);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(255, 0, 255, 0.4);
}

.percentile-veteran {
  background: var(--bg-panel);
  color: var(--text-primary);
  border: 1px solid var(--neon-cyan);
}

/* Timeline */
.timeline-content {
  padding: 1rem 0;
}

.timeline-chart-container {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 1rem;
  height: 400px;
  position: relative;
}

.timeline-chart {
  height: 100%;
}

/* Select */
.explorer-select {
  padding: 0.5rem 2rem 0.5rem 0.75rem;
  font-size: 0.75rem;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.2s ease;
  appearance: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%2300fff2' d='M6 8.825L1.175 4l1.414-1.415L6 6l3.411-3.415L10.825 4z'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 0.5rem center;
  background-size: 12px;
}

.explorer-select:focus {
  outline: none;
  border-color: var(--neon-cyan);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.2);
}

/* Empty state */
.explorer-empty {
  text-align: center;
  padding: 3rem 1.5rem;
}

.explorer-empty-icon {
  font-size: 2.5rem;
  margin-bottom: 1rem;
  opacity: 0.5;
}

.explorer-empty-title {
  font-size: 0.875rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--text-primary);
  margin-bottom: 0.5rem;
}

.explorer-empty-desc {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

/* Skeleton */
.explorer-skeleton {
  background: linear-gradient(
    90deg,
    var(--bg-card) 0%,
    var(--border-color) 50%,
    var(--bg-card) 100%
  );
  background-size: 200% 100%;
  animation: skeleton-pulse 1.5s ease-in-out infinite;
  border-radius: 4px;
}

@keyframes skeleton-pulse {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

/* Spinner */
.explorer-spinner {
  width: 2rem;
  height: 2rem;
  border: 2px solid var(--border-color);
  border-top-color: var(--neon-cyan);
  border-radius: 50%;
  animation: spinner-rotate 0.8s linear infinite;
}

@keyframes spinner-rotate {
  to { transform: rotate(360deg); }
}

/* Button */
.explorer-btn {
  padding: 0.5rem 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  font-family: 'JetBrains Mono', monospace;
  text-transform: uppercase;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid transparent;
}

.explorer-btn--ghost {
  background: transparent;
  color: var(--text-secondary);
  border-color: var(--border-color);
}

.explorer-btn--ghost:hover {
  color: var(--text-primary);
  border-color: var(--neon-cyan);
  background: rgba(0, 255, 242, 0.1);
}

.explorer-btn--sm {
  padding: 0.375rem 0.75rem;
  font-size: 0.625rem;
}

/* Text utilities */
.text-neon-gold { color: var(--neon-gold); }
.text-neon-cyan { color: var(--neon-cyan); }
.text-neon-pink { color: var(--neon-pink); }
.text-neon-red { color: var(--neon-red); }
.text-neon-green { color: var(--neon-green); }
.text-neutral-500 { color: var(--text-secondary); }
.text-xs { font-size: 0.625rem; }
.font-mono { font-family: 'JetBrains Mono', monospace; }

/* Responsive */
@media (max-width: 640px) {
  .rankings-hero-stats {
    gap: 1rem;
  }
  
  .ranking-stats {
    flex-direction: column;
    gap: 0.25rem;
  }
  
  .ranking-item {
    padding: 0.75rem;
  }
}
</style>