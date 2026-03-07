<template>
  <div class="competitive-rankings">
    <!-- Loading State -->
    <div v-if="isLoading" class="flex flex-col gap-4">
      <div class="explorer-skeleton" style="height: 2.5rem"></div>
      <div class="explorer-skeleton" style="height: 10rem"></div>
      <div class="explorer-skeleton" style="height: 15rem"></div>
    </div>

    <!-- Content -->
    <div v-else-if="rankingsData">
      <!-- Time Period Selection -->
      <div class="time-period-selector mb-6">
        <div class="time-period-buttons">
          <button
            :class="['time-period-btn', { active: timePeriod === 'last-month' }]"
            @click="selectTimePeriod('last-month')"
          >
            Last Month
          </button>
          <button
            :class="['time-period-btn', { active: timePeriod === 'all-time' }]"
            @click="selectTimePeriod('all-time')"
          >
            All Time
          </button>
        </div>
        <div class="time-period-picker">
          <select v-model.number="selectedYear" @change="onDateChange" class="explorer-select">
            <option v-for="year in availableYears" :key="year" :value="year">
              {{ year }}
            </option>
          </select>
          <select v-model.number="selectedMonth" @change="onDateChange" class="explorer-select">
            <option v-for="(monthName, idx) in monthNames" :key="idx" :value="idx + 1">
              {{ monthName }}
            </option>
          </select>
          <button @click="loadRankingsForPeriod" class="explorer-btn explorer-btn--ghost explorer-btn--sm">
            View
          </button>
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

      <!-- Error State (shown inline with controls) -->
      <div v-if="error" class="mb-4 p-4 bg-red-500/20 border border-red-500/50 rounded-lg">
        <div class="flex items-center gap-3">
          <div class="text-red-400 text-lg">!</div>
          <div>
            <p class="text-red-400 font-medium">{{ error }}</p>
            <button @click="loadData()" class="text-red-400 text-sm underline mt-1 hover:text-red-300">
              Try again
            </button>
          </div>
        </div>
      </div>

      <!-- Performance Tab -->
      <div v-if="!error && activeTab === 'current'" class="space-y-2">
        <!-- View mode toggle -->
        <div class="view-toggle">
          <button 
            :class="['toggle-btn', { active: viewMode === 'chart' }]"
            @click="viewMode = 'chart'"
          >
            CHART
          </button>
          <button 
            :class="['toggle-btn', { active: viewMode === 'list' }]"
            @click="viewMode = 'list'"
          >
            LIST
          </button>
        </div>

        <!-- Chart view -->
        <div v-if="viewMode === 'chart'">
          <PlayerCompetitiveRankingsChart 
            :rankings="rankingsData.mapRankings"
            :sortBy="'kdRatio'"
          />
        </div>

        <!-- List view -->
        <div v-else>
        <div 
          v-for="ranking in paginatedRankings"
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

        <!-- Pagination Controls (only for list view) -->
        <div v-if="viewMode === 'list' && totalPages > 1" class="pagination-controls">
          <button
            class="pagination-btn"
            :disabled="currentPage === 1"
            @click="goToPage(currentPage - 1)"
          >
            &larr;
          </button>
          
          <button
            v-for="pageNum in paginationRange"
            :key="pageNum"
            class="pagination-btn"
            :class="{ 'pagination-btn--active': pageNum === currentPage }"
            @click="goToPage(pageNum)"
          >
            {{ pageNum }}
          </button>
          
          <button
            class="pagination-btn"
            :disabled="currentPage === totalPages"
            @click="goToPage(currentPage + 1)"
          >
            &rarr;
          </button>
        </div>
      </div>

      <!-- Timeline Tab -->
      <div v-else-if="!error && activeTab === 'timeline'" class="timeline-content">
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
import PlayerCompetitiveRankingsChart from './PlayerCompetitiveRankingsChart.vue';

const props = defineProps<{
  playerName: string;
  game?: string;
}>();

const emit = defineEmits<{
  navigateToMap: [mapName: string];
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
  { id: 'current', label: 'PERFORMANCE' },
  { id: 'timeline', label: 'RANK TIMELINE' }
];

const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

const activeTab = ref<'current' | 'timeline'>('current' as 'current' | 'timeline');
const viewMode = ref<'chart' | 'list'>('chart' as 'chart' | 'list'); // Default to chart view
const rankingsData = ref<CompetitiveRankingsResponse | null>(null);
const timelineData = ref<RankingTimelineResponse | null>(null);
const isLoading = ref(false);
const isTimelineLoading = ref(false);
const error = ref<string | null>(null);
const selectedTimelineMap = ref('');
const timelineCanvas = ref<HTMLCanvasElement | null>(null);
let timelineChart: Chart | null = null;

// Time period state
const timePeriod = ref<'last-month' | 'all-time' | 'custom'>('last-month');
const selectedYear = ref(new Date().getFullYear());
const selectedMonth = ref(new Date().getMonth() + 1);

// Pagination state
const currentPage = ref(1);
const itemsPerPage = 10;

// Computed
const sortedRankings = computed(() => {
  if (!rankingsData.value) return [];
  return [...rankingsData.value.mapRankings].sort((a, b) => a.rank - b.rank);
});

const availableMaps = computed(() => {
  if (!rankingsData.value) return [];
  return rankingsData.value.mapRankings.map(r => r.mapName).sort();
});

const availableYears = computed(() => {
  const currentYear = new Date().getFullYear();
  const years = [];
  // Show years from 5 years ago to current year
  for (let i = currentYear - 5; i <= currentYear; i++) {
    years.push(i);
  }
  return years;
});

// Pagination computed properties
const totalPages = computed(() => {
  if (!sortedRankings.value.length) return 0;
  return Math.ceil(sortedRankings.value.length / itemsPerPage);
});

const paginatedRankings = computed(() => {
  const start = (currentPage.value - 1) * itemsPerPage;
  const end = start + itemsPerPage;
  return sortedRankings.value.slice(start, end);
});

const paginationRange = computed(() => {
  const range: number[] = [];
  const maxVisible = 5;
  let start = Math.max(1, currentPage.value - Math.floor(maxVisible / 2));
  const end = Math.min(totalPages.value, start + maxVisible - 1);
  if (end === totalPages.value) start = Math.max(1, end - maxVisible + 1);
  for (let i = start; i <= end; i++) range.push(i);
  return range;
});

// Methods
const selectTimePeriod = (period: 'last-month' | 'all-time') => {
  timePeriod.value = period;
  loadRankingsForPeriod();
};

const onDateChange = () => {
  timePeriod.value = 'custom';
};

const loadRankingsForPeriod = async () => {
  isLoading.value = true;
  error.value = null;

  try {
    let url = `/stats/data-explorer/players/${encodeURIComponent(props.playerName)}/competitive-rankings?game=${props.game || 'bf1942'}`;

    if (timePeriod.value === 'last-month') {
      url += '&days=30';
    } else if (timePeriod.value === 'all-time') {
      url += '&days=999999'; // Effectively all-time
    } else if (timePeriod.value === 'custom') {
      // For custom, send year/month parameters
      url += `&year=${selectedYear.value}&month=${selectedMonth.value}`;
    }

    const response = await fetch(url);

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error('No ranking data found for this player');
      }
      throw new Error('Failed to load competitive rankings');
    }

    rankingsData.value = await response.json();
    currentPage.value = 1; // Reset to first page when data loads
  } catch (err: any) {
    console.error('Error loading competitive rankings:', err);
    error.value = err.message || 'Failed to load rankings';
  } finally {
    isLoading.value = false;
  }
};

const loadData = async () => {
  await loadRankingsForPeriod();
};

const goToPage = (page: number) => {
  if (page < 1 || page > totalPages.value) return;
  currentPage.value = page;
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
          borderColor: '#2196F3',
          backgroundColor: 'rgba(33, 150, 243, 0.1)',
          borderWidth: 2,
          fill: true,
          tension: 0.3,
          pointRadius: 4,
          pointBackgroundColor: '#2196F3',
          pointBorderColor: '#ffffff',
          pointBorderWidth: 2,
          pointHoverRadius: 6,
          yAxisID: 'y-rank'
        },
        {
          label: 'Percentile',
          data: validData.map(t => t.percentile).reverse(),
          borderColor: '#E91E63',
          backgroundColor: 'rgba(233, 30, 99, 0.1)',
          borderWidth: 2,
          fill: false,
          tension: 0.3,
          pointRadius: 3,
          pointBackgroundColor: '#E91E63',
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
          borderColor: '#2196F3',
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
  // Emit event to parent component instead of navigating
  emit('navigateToMap', mapName);
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
  } else if (newTab === 'current') {
    currentPage.value = 1; // Reset to first page when switching back to current rankings
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

/* Time Period Selector */
.time-period-selector {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1rem;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}

.time-period-buttons {
  display: flex;
  gap: 0.5rem;
}

.time-period-btn {
  flex: 1;
  padding: 0.65rem 1rem;
  font-size: 0.8rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  border: 1px solid var(--border-color);
  border-radius: 6px;
  background: var(--bg-panel);
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.time-period-btn:hover {
  border-color: var(--neon-cyan);
  color: var(--text-primary);
}

.time-period-btn.active {
  background: var(--neon-cyan);
  border-color: var(--neon-cyan);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(245, 158, 11, 0.5);
}

.time-period-picker {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}

.time-period-picker select {
  flex: 1;
  min-width: 120px;
  padding: 0.5rem;
  font-size: 0.8rem;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.time-period-picker select:hover {
  border-color: var(--neon-cyan);
}

.time-period-picker select:focus {
  outline: none;
  border-color: var(--neon-cyan);
  box-shadow: 0 0 10px rgba(245, 158, 11, 0.3);
}

@media (max-width: 640px) {
  .time-period-selector {
    gap: 0.75rem;
    padding: 0.75rem;
  }

  .time-period-picker {
    flex-direction: column;
  }

  .time-period-picker select,
  .time-period-picker button {
    width: 100%;
  }
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
  background: linear-gradient(135deg, var(--neon-gold) 0%, rgba(251, 191, 36, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(251, 191, 36, 0.5);
}

.badge-master {
  background: linear-gradient(135deg, var(--neon-cyan) 0%, rgba(245, 158, 11, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(245, 158, 11, 0.5);
}

.badge-expert {
  background: linear-gradient(135deg, var(--neon-pink) 0%, rgba(251, 113, 133, 0.2) 100%);
  color: var(--bg-dark);
  box-shadow: 0 0 20px rgba(251, 113, 133, 0.5);
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
  text-shadow: 0 0 10px rgba(245, 158, 11, 0.5);
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
  border-color: rgba(245, 158, 11, 0.3);
  transform: translateX(4px);
  box-shadow: 0 0 20px rgba(245, 158, 11, 0.1);
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
  box-shadow: 0 0 15px rgba(251, 191, 36, 0.3);
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
  box-shadow: 0 0 10px rgba(251, 191, 36, 0.4);
}

.percentile-master {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(245, 158, 11, 0.4);
}

.percentile-expert {
  background: var(--neon-pink);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(251, 113, 133, 0.4);
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
  box-shadow: 0 0 15px rgba(245, 158, 11, 0.2);
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
  background: rgba(245, 158, 11, 0.1);
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

/* View toggle */
.view-toggle {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  justify-content: center;
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

.toggle-btn:hover:not(.active) {
  border-color: var(--neon-cyan);
  color: var(--neon-cyan);
}

/* Pagination */
.pagination-controls {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.25rem;
  padding-top: 1rem;
  margin-top: 1rem;
  border-top: 1px solid var(--border-color);
}

.pagination-btn {
  padding: 0.375rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  color: var(--text-secondary);
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s ease;
  min-width: 2rem;
}

.pagination-btn:hover:not(:disabled) {
  color: var(--text-primary);
  border-color: var(--neon-cyan);
  background: rgba(245, 158, 11, 0.1);
}

.pagination-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.pagination-btn--active {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  border-color: var(--neon-cyan);
  box-shadow: 0 0 10px rgba(245, 158, 11, 0.4);
}

.pagination-btn--active:hover {
  background: var(--neon-cyan);
  color: var(--bg-dark);
}

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