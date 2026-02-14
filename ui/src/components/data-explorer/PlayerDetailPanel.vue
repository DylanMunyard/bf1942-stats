<template>
  <div class="player-detail-panel">
    <!-- Loading State -->
    <div v-if="isLoading" class="detail-loading p-6">
      <div class="detail-skeleton detail-skeleton--title"></div>
      <div class="detail-skeleton detail-skeleton--subtitle"></div>
      <div class="detail-skeleton detail-skeleton--block"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="detail-empty">
      <div class="detail-empty-icon text-neon-red">!</div>
      <p class="detail-empty-title text-neon-red">{{ error }}</p>
      <p class="detail-empty-desc">Try selecting a different time period or slice dimension.</p>
      <div class="flex gap-2 justify-center mt-4">
        <button @click="loadData()" class="detail-retry text-neon-cyan hover:text-white">
          Try again
        </button>
      </div>
    </div>

    <!-- Content -->
    <div v-else-if="slicedData" class="detail-content">
      
      <!-- Header / Controls -->
      <div class="detail-header mb-6">
        <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
          <div>
            <h2 class="detail-title text-neon-cyan">{{ getCurrentSliceName() }}</h2>
            <p class="detail-meta">{{ getCurrentSliceDescription() }}</p>
          </div>
          
          <div class="flex flex-col sm:flex-row gap-4 items-end sm:items-center">
             <!-- Slice Dimension Selector -->
            <div class="relative min-w-[200px]">
              <select
                v-model="selectedSliceType"
                @change="changeSliceType"
                class="search-input appearance-none cursor-pointer focus:border-neon-cyan"
              >
                <option
                  v-for="dimension in availableDimensions"
                  :key="dimension.type"
                  :value="dimension.type"
                >
                  {{ dimension.name }}
                </option>
              </select>
              <div class="absolute inset-y-0 right-0 flex items-center px-2 pointer-events-none text-neon-cyan opacity-70">
                <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"></path></svg>
              </div>
            </div>

            <!-- Time Range Tabs -->
            <div class="rankings-tabs">
              <button
                v-for="option in timeRangeOptions"
                :key="option.value"
                class="rankings-tab"
                :class="{ 'rankings-tab--active': selectedTimeRange === option.value }"
                @click="changeTimeRange(option.value)"
                :disabled="isLoading"
              >
                {{ option.label }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Summary Stats -->
      <div v-if="slicedData.results.length > 0" class="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <!-- Card 1: Count -->
        <div class="detail-card text-center py-4 border-t-2 border-t-transparent hover:border-t-neon-cyan transition-colors">
          <div class="stat-value text-white">{{ slicedData.results.length }}</div>
          <div class="stat-label">{{ getResultTypeLabel() }}</div>
        </div>

        <!-- Card 2: Primary Metric -->
        <div class="detail-card text-center py-4 border-t-2 border-t-transparent hover:border-t-current transition-colors" :class="themeColorClass">
          <div class="stat-value" :class="themeColorClass">{{ getTotalPrimaryValue() }}</div>
          <div class="stat-label">{{ getPrimaryMetricLabel() }}</div>
        </div>

        <!-- Card 3: Secondary Metric -->
        <div class="detail-card text-center py-4 border-t-2 border-t-transparent hover:border-t-neutral-400 transition-colors">
          <div class="stat-value text-neutral-300">{{ getTotalSecondaryValue() }}</div>
          <div class="stat-label">{{ getSecondaryMetricLabel() }}</div>
        </div>

        <!-- Card 4: Percentage -->
        <div class="detail-card text-center py-4 border-t-2 border-t-transparent hover:border-t-current transition-colors" :class="percentageColorClass">
          <div class="stat-value" :class="percentageColorClass">{{ getAveragePercentage() }}<span class="text-sm ml-1 opacity-50">{{ getPercentageUnit() || '' }}</span></div>
          <div class="stat-label">{{ getPercentageLabel() }}</div>
        </div>
      </div>

      <!-- Results Table -->
      <div v-if="slicedData.results.length > 0" class="detail-section">
        <h3 class="detail-section-title mb-2 text-neon-cyan">DETAILED RESULTS</h3>
        
        <div class="detail-card p-0 overflow-hidden">
          <div class="overflow-x-auto">
            <table class="server-table w-full">
              <!-- Table Header -->
              <thead>
                <tr>
                  <th class="w-12 text-center text-neon-cyan">#</th>
                  <th class="text-left pl-4 text-neon-cyan">{{ getTableHeaderLabel() }}</th>
                  <th class="text-right text-neutral-400">{{ getSecondaryMetricLabel() }}</th>
                  <th class="text-right" :class="themeColorClass">{{ getPrimaryMetricLabel() }}</th>
                  <th class="text-right pr-6" :class="percentageColorClass">{{ getPercentageLabel() }}</th>
                  <th v-if="hasAdditionalData()" class="text-left pl-4 text-neutral-400">Additional Stats</th>
                </tr>
              </thead>

              <!-- Table Body -->
              <tbody>
                <tr
                  v-for="(result, index) in slicedData.results"
                  :key="`${result.sliceKey}-${result.subKey || 'global'}`"
                  class="cursor-pointer transition-colors hover:bg-white/5"
                  @click="handleSliceClick(result)"
                >
                  <!-- Rank -->
                  <td class="text-center font-mono">
                    <span :class="getRankClass(result.rank)">{{ result.rank }}</span>
                  </td>

                  <!-- Main Label -->
                  <td class="pl-4">
                    <div class="server-name">
                      <span class="font-bold text-neutral-200 group-hover:text-white transition-colors" :class="{ 'text-neon-cyan': isMapSlice() }">
                        {{ result.sliceLabel }}
                      </span>
                      <span v-if="result.subKey" class="game-tag ml-2 text-neutral-400 border-neutral-700">
                        {{ result.subKeyLabel || getServerName(result.subKey) }}
                      </span>
                    </div>
                  </td>

                  <!-- Secondary Value -->
                  <td class="text-right font-mono text-neutral-400">
                    {{ result.secondaryValue.toLocaleString() }}
                  </td>

                  <!-- Primary Value -->
                  <td class="text-right font-mono font-bold" :class="themeColorClass">
                    {{ result.primaryValue.toLocaleString() }}
                  </td>

                  <!-- Percentage -->
                  <td class="text-right pr-6 font-mono" :class="percentageColorClass">
                    {{ result.percentage.toFixed(1) }}<span class="text-xs ml-0.5 opacity-70">{{ getPercentageUnit() }}</span>
                  </td>

                  <!-- Additional Data -->
                  <td v-if="hasAdditionalData()" class="pl-4 py-2">
                    <div v-if="isTeamWinSlice()" class="w-full max-w-[200px]">
                      <!-- Visual Win Rate Bar -->
                      <div v-if="getTeamLabel(result.additionalData, 'team1Label') || getTeamLabel(result.additionalData, 'team2Label')" class="mb-1">
                        <WinStatsBar :winStats="getTeamWinStats(result)" />
                      </div>
                    </div>
                    <div v-else class="text-xs space-y-1 text-neutral-400">
                      <div v-for="(value, key) in result.additionalData" :key="key" class="flex gap-2">
                        <span class="opacity-70">{{ formatAdditionalKey(key) }}:</span>
                        <span class="font-mono text-neutral-300">{{ formatAdditionalValue(value) }}</span>
                      </div>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Pagination Controls -->
        <div v-if="slicedData.pagination.totalPages > 1" class="flex justify-center items-center gap-2 mt-4">
          <button
            @click="changePage(slicedData.pagination.page - 1)"
            :disabled="!slicedData.pagination.hasPrevious || isLoading"
            class="pagination-btn hover:border-neon-cyan hover:text-neon-cyan"
          >
            &larr;
          </button>

          <span class="text-xs font-mono text-neutral-400">
            PAGE <span class="text-white">{{ slicedData.pagination.page }}</span> OF <span class="text-white">{{ slicedData.pagination.totalPages }}</span>
          </span>

          <button
            @click="changePage(slicedData.pagination.page + 1)"
            :disabled="!slicedData.pagination.hasNext || isLoading"
            class="pagination-btn hover:border-neon-cyan hover:text-neon-cyan"
          >
            &rarr;
          </button>
        </div>
      </div>

      <!-- Empty State -->
      <div v-else class="detail-empty">
        <div class="detail-empty-icon text-neutral-600">{ }</div>
        <p class="detail-empty-title text-neutral-300">NO DATA AVAILABLE</p>
        <p class="detail-empty-desc text-neutral-500">No statistics found for this player with the current filters.</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue';
import { PLAYER_STATS_TIME_RANGE_OPTIONS } from '@/utils/constants';
import WinStatsBar from '@/components/data-explorer/WinStatsBar.vue';
import type { WinStats } from '@/services/dataExplorerService';

const props = defineProps<{
  playerName: string;
  game?: string;
  serverGuid?: string; // Optional: filter to a specific server
}>();

const emit = defineEmits<{
  'navigate-to-server': [serverGuid: string];
  'navigate-to-map': [mapName: string];
}>();

const isMapSlice = () => {
  return selectedSliceType.value.includes('Map');
};

const handleSliceClick = (result: PlayerSliceResultDto) => {
  if (isMapSlice()) {
    emit('navigate-to-map', result.sliceKey);
  }
};

// API Types
interface SliceDimensionOption {
  type: string;
  name: string;
  description: string;
}

interface PlayerSlicedStatsResponse {
  playerName: string;
  game: string;
  sliceDimension: string;
  sliceType: string;
  results: PlayerSliceResultDto[];
  dateRange: { days: number; fromDate: string; toDate: string };
  pagination: {
    page: number;
    pageSize: number;
    totalItems: number;
    totalPages: number;
    hasNext: boolean;
    hasPrevious: boolean;
  };
}

interface PlayerSliceResultDto {
  sliceKey: string;
  subKey: string | null;
  subKeyLabel?: string | null;
  sliceLabel: string;
  primaryValue: number;
  secondaryValue: number;
  percentage: number;
  rank: number;
  totalPlayers: number;
  additionalData: Record<string, any>;
}

const slicedData = ref<PlayerSlicedStatsResponse | null>(null);
const availableDimensions = ref<SliceDimensionOption[]>([]);
const isLoading = ref(false);
const error = ref<string | null>(null);

// Control states
const selectedTimeRange = ref<number>(60);
const selectedSliceType = ref<string>('ScoreByMap');
const currentPage = ref<number>(1);
const pageSize = ref<number>(10);
const allResults = ref<PlayerSliceResultDto[]>([]);

const timeRangeOptions = PLAYER_STATS_TIME_RANGE_OPTIONS;

const gameLabel = computed(() => {
  switch (slicedData.value?.game?.toLowerCase()) {
    case 'bf1942': return 'BF1942';
    case 'fh2': return 'FH2';
    case 'bfvietnam': return 'BFV';
    default: return slicedData.value?.game || 'UNKNOWN';
  }
});

// Theme Logic
const themeColorClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'text-neon-red';
  if (type.includes('Wins')) return 'text-neon-green';
  return 'text-neon-cyan';
});

const percentageColorClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'text-neon-pink'; // K/D
  if (type.includes('Wins')) return 'text-neon-green'; // Win Rate
  return 'text-neon-gold'; // Score/min or similar
});

const getRankClass = (rank: number) => {
  if (rank === 1) return 'text-neon-gold font-bold scale-110 inline-block';
  if (rank === 2) return 'text-neutral-300 font-bold';
  if (rank === 3) return 'text-orange-400 font-bold';
  return 'text-neutral-500';
};

// Load available slice dimensions
const loadSliceDimensions = async () => {
  try {
    const response = await fetch('/stats/data-explorer/slice-dimensions');
    if (!response.ok) throw new Error('Failed to fetch slice dimensions');
    availableDimensions.value = await response.json();
  } catch (err) {
    console.error('Error loading slice dimensions:', err);
    availableDimensions.value = [
      { type: 'ScoreByMap', name: 'Score by Map', description: 'Total player score per map' },
      { type: 'ScoreByMapAndServer', name: 'Score by Map + Server', description: 'Player score per map per server' },
      { type: 'KillsByMap', name: 'Kills by Map', description: 'Total kills per map' },
      { type: 'KillsByMapAndServer', name: 'Kills by Map + Server', description: 'Kills per map per server' },
      { type: 'TeamWinsByMap', name: 'Team Wins by Map', description: 'Team win statistics per map' },
      { type: 'TeamWinsByMapAndServer', name: 'Team Wins by Map + Server', description: 'Team wins per map per server' }
    ];
  }
};

const loadData = async (days?: number) => {
  if (!props.playerName) return;

  const timeRange = days || selectedTimeRange.value;

  isLoading.value = true;
  error.value = null;

  try {
    const params = new URLSearchParams({
      sliceType: selectedSliceType.value,
      game: props.game || 'bf1942',
      page: '1',
      pageSize: '1000',
      days: timeRange.toString()
    });

    const response = await fetch(`/stats/data-explorer/players/${encodeURIComponent(props.playerName)}/sliced-stats?${params}`);

    if (!response.ok) {
      if (response.status === 404) {
        throw new Error(`No data available for this player in the last ${timeRange} days`);
      } else {
        throw new Error('Failed to load player statistics');
      }
    }

    const responseData = await response.json();

    allResults.value = responseData.results || [];

    slicedData.value = {
      ...responseData,
      results: getPaginatedResults(),
      pagination: {
        page: currentPage.value,
        pageSize: pageSize.value,
        totalItems: allResults.value.length,
        totalPages: Math.ceil(allResults.value.length / pageSize.value),
        hasNext: currentPage.value < Math.ceil(allResults.value.length / pageSize.value),
        hasPrevious: currentPage.value > 1
      }
    };
  } catch (err: any) {
    console.error(`Error loading sliced player data:`, err);
    error.value = err.message || 'Failed to load player details';
  }

  isLoading.value = false;
};

const getPaginatedResults = (): PlayerSliceResultDto[] => {
  const startIndex = (currentPage.value - 1) * pageSize.value;
  const endIndex = startIndex + pageSize.value;
  return allResults.value.slice(startIndex, endIndex);
};

const changeTimeRange = (days: number) => {
  selectedTimeRange.value = days;
  currentPage.value = 1;
  loadData(days);
};

const changeSliceType = () => {
  currentPage.value = 1;
  loadData(selectedTimeRange.value);
};

const changePage = (page: number) => {
  if (page < 1 || page > Math.ceil(allResults.value.length / pageSize.value)) return;
  currentPage.value = page;
  if (slicedData.value) {
    slicedData.value = {
      ...slicedData.value,
      results: getPaginatedResults(),
      pagination: {
        page: currentPage.value,
        pageSize: pageSize.value,
        totalItems: allResults.value.length,
        totalPages: Math.ceil(allResults.value.length / pageSize.value),
        hasNext: currentPage.value < Math.ceil(allResults.value.length / pageSize.value),
        hasPrevious: currentPage.value > 1
      }
    };
  }
};

// UI Helper Methods
const getCurrentSliceName = () => {
  const dimension = availableDimensions.value.find(d => d.type === selectedSliceType.value);
  return dimension?.name || selectedSliceType.value;
};

const getCurrentSliceDescription = () => {
  const dimension = availableDimensions.value.find(d => d.type === selectedSliceType.value);
  return dimension?.description || 'Player statistics broken down by selected dimension';
};

const getResultTypeLabel = () => {
  if (!slicedData.value) return 'Results';
  return selectedSliceType.value.includes('Server') ? 'Map-Server Combos' : 'Maps';
};

const getPrimaryMetricLabel = () => {
  if (selectedSliceType.value.includes('Score')) return 'Total Score';
  if (selectedSliceType.value.includes('Kills')) return 'Total Kills';
  if (selectedSliceType.value.includes('Wins')) return 'Total Wins';
  return 'Total';
};

const getSecondaryMetricLabel = () => {
  return 'Rounds';
};

const getPercentageLabel = () => {
  if (selectedSliceType.value.includes('Score') || selectedSliceType.value.includes('Kills')) return 'Avg K/D';
  if (selectedSliceType.value.includes('Wins')) return 'Win Rate';
  return 'Rate';
};

const getPercentageUnit = () => {
  if (selectedSliceType.value.includes('Wins')) return '%';
  return '';
};

const getTableHeaderLabel = () => {
  if (selectedSliceType.value.includes('Server')) return 'Map & Server';
  return 'Map';
};

const hasAdditionalData = () => {
  return slicedData.value?.results.some(result => Object.keys(result.additionalData).length > 0) || false;
};

const getTotalPrimaryValue = () => {
  if (!slicedData.value) return 0;
  return slicedData.value.results.reduce((sum, result) => sum + result.primaryValue, 0).toLocaleString();
};

const getTotalSecondaryValue = () => {
  if (!slicedData.value) return 0;
  return slicedData.value.results.reduce((sum, result) => sum + result.secondaryValue, 0).toLocaleString();
};

const getAveragePercentage = () => {
  if (!slicedData.value || slicedData.value.results.length === 0) return 0;
  const total = slicedData.value.results.reduce((sum, result) => sum + result.percentage, 0);
  return (total / slicedData.value.results.length).toFixed(1);
};

const formatAdditionalKey = (key: string) => {
  return key
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/([a-zA-Z])(\d+)/g, '$1 $2')
    .replace(/^./, str => str.toUpperCase());
};

const formatAdditionalValue = (value: any) => {
  if (typeof value === 'number') {
    return value.toLocaleString();
  }
  return String(value);
};

const getServerName = (serverGuid: string) => {
  return serverGuid.substring(0, 8) + '...';
};

const isTeamWinSlice = () => selectedSliceType.value.includes('TeamWins');

const getTeamLabel = (additionalData: Record<string, any>, key: 'team1Label' | 'team2Label') => {
  const value = additionalData?.[key];
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
};

const getTeamWinStats = (result: PlayerSliceResultDto): WinStats => {
  const team1Label = getTeamLabel(result.additionalData, 'team1Label') || 'Team 1';
  const team2Label = getTeamLabel(result.additionalData, 'team2Label') || 'Team 2';

  return {
    team1Label,
    team2Label,
    team1Victories: result.primaryValue,
    team2Victories: result.additionalData.team2Victories || 0,
    team1WinPercentage: Math.round(result.percentage),
    team2WinPercentage: Math.round(result.additionalData.team2WinRate || 0),
    totalRounds: result.secondaryValue
  };
};

onMounted(async () => {
  await loadSliceDimensions();
  await loadData();
});

watch(() => props.playerName, () => {
  currentPage.value = 1;
  loadData();
});

watch(() => props.game, () => {
  currentPage.value = 1;
  loadData();
});

watch(() => props.serverGuid, () => {
  currentPage.value = 1;
  loadData();
});
</script>

<style scoped>
.player-detail-panel {
  /* Inherit fonts from parent */
}

/* Header Styles */
.detail-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--portal-text-bright);
  margin: 0;
}

.detail-meta {
  font-size: 0.8rem;
  color: var(--portal-text);
  margin-top: 0.25rem;
}

/* Card Styles */
.detail-card {
  background: var(--portal-surface-elevated);
  border: 1px solid var(--portal-border);
  border-radius: 2px;
  padding: 1rem;
}

/* Section Title */
.detail-section-title {
  font-size: 0.65rem;
  font-weight: 600;
  letter-spacing: 0.12em;
  color: var(--portal-accent);
  margin: 0;
  font-family: ui-monospace, monospace;
}

/* Stats */
.stat-value {
  font-size: 1.5rem;
  font-weight: 800;
  color: var(--portal-text-bright);
  line-height: 1.2;
  font-family: ui-monospace, monospace;
}

.stat-label {
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  color: var(--portal-text);
  text-transform: uppercase;
  margin-top: 0.25rem;
}

/* Table Styles (Matching ServerRotationTable) */
.server-table {
  width: 100%;
  font-size: 0.8rem;
  border-collapse: collapse;
  table-layout: auto;
}

.server-table th {
  text-align: left;
  padding: 0.5rem 0.5rem;
  font-size: 0.65rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--portal-accent);
  font-family: ui-monospace, monospace;
  border-bottom: 1px solid var(--portal-border);
  white-space: nowrap;
}

.server-table td {
  padding: 0.5rem;
  border-bottom: 1px solid var(--portal-border);
  color: var(--portal-text-bright);
}

.server-table tbody tr:last-child td {
  border-bottom: none;
}

.game-tag {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  font-size: 0.55rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  font-family: ui-monospace, monospace;
  background: var(--portal-surface);
  border: 1px solid var(--portal-border);
  border-radius: 2px;
  color: var(--portal-text);
}

/* Search Input Style for Select */
.search-input {
  width: 100%;
  padding: 0.35rem 0.5rem 0.35rem 0.75rem;
  font-size: 0.8rem;
  background: var(--portal-surface);
  border: 1px solid var(--portal-border);
  border-radius: 2px;
  color: var(--portal-text-bright);
  transition: border-color 0.2s, box-shadow 0.2s;
}

.search-input:focus {
  outline: none;
  border-color: var(--portal-accent);
  box-shadow: 0 0 0 3px var(--portal-accent-dim);
}

/* Tabs */
.rankings-tabs {
  display: flex;
  gap: 0;
  border-bottom: 1px solid var(--portal-border);
}

.rankings-tab {
  padding: 0.5rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.04em;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  color: var(--portal-text);
  cursor: pointer;
  transition: color 0.2s, border-color 0.2s;
}

.rankings-tab:hover:not(:disabled) {
  color: var(--portal-text-bright);
}

.rankings-tab--active {
  color: var(--portal-accent);
  border-bottom-color: var(--portal-accent);
}

.rankings-tab:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Pagination */
.pagination-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border-radius: 2px;
  background: var(--portal-surface);
  border: 1px solid var(--portal-border);
  color: var(--portal-text);
  cursor: pointer;
  transition: all 0.2s;
}

.pagination-btn:hover:not(:disabled) {
  border-color: var(--portal-accent);
  color: var(--portal-accent);
}

.pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Empty State */
.detail-empty {
  text-align: center;
  padding: 2rem;
}

.detail-empty-icon {
  font-size: 1.5rem;
  color: var(--portal-accent);
  opacity: 0.5;
  margin-bottom: 0.5rem;
  font-family: ui-monospace, monospace;
}

.detail-empty-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--portal-text-bright);
  margin: 0;
}

.detail-empty-desc {
  font-size: 0.8rem;
  color: var(--portal-text);
  margin-top: 0.35rem;
}

.detail-retry {
  font-size: 0.8rem;
  color: var(--portal-accent);
  background: none;
  border: none;
  cursor: pointer;
  text-decoration: underline;
}

/* Loading Skeleton */
.detail-loading {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.detail-skeleton {
  background: linear-gradient(
    90deg,
    var(--portal-surface-elevated) 0%,
    var(--portal-border) 50%,
    var(--portal-surface-elevated) 100%
  );
  background-size: 200% 100%;
  animation: skeleton-pulse 1.5s ease-in-out infinite;
  border-radius: 2px;
}

.detail-skeleton--title { height: 2rem; width: 33%; }
.detail-skeleton--subtitle { height: 1rem; width: 25%; }
.detail-skeleton--block { height: 12rem; }

@keyframes skeleton-pulse {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}
</style>
