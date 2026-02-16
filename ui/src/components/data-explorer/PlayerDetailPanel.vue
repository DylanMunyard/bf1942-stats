<template>
  <div class="player-detail-panel">
    <!-- Loading State -->
    <div v-if="isLoading" class="flex flex-col gap-4 p-6">
      <div class="explorer-skeleton" style="height: 2rem; width: 33%"></div>
      <div class="explorer-skeleton" style="height: 1rem; width: 25%"></div>
      <div class="explorer-skeleton" style="height: 12rem"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="explorer-empty">
      <div class="explorer-empty-icon text-neon-red">!</div>
      <p class="explorer-empty-title text-neon-red">{{ error }}</p>
      <p class="explorer-empty-desc">Try selecting a different time period or slice dimension.</p>
      <div class="flex gap-2 justify-center mt-4">
        <button @click="loadData()" class="explorer-btn explorer-btn--ghost explorer-btn--sm">
          Try again
        </button>
      </div>
    </div>

    <!-- Content -->
    <div v-else-if="slicedData">

      <!-- Header / Controls -->
      <div class="mb-4 sm:mb-6">
        <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
          <div>
            <h2 class="explorer-section-title" style="font-size: 1.25rem; margin: 0">{{ getCurrentSliceName() }}</h2>
            <p class="text-xs mt-1" style="color: var(--text-secondary)">{{ getCurrentSliceDescription() }}</p>
          </div>

          <div class="flex flex-col sm:flex-row gap-4 items-end sm:items-center">
            <!-- Slice Dimension Selector -->
            <div class="slice-select-wrap relative min-w-[240px]">
              <label class="slice-select-label">SLICE BY</label>
              <select
                v-model="selectedSliceType"
                @change="changeSliceType"
                class="explorer-select appearance-none cursor-pointer w-full"
                style="padding: 0.6rem 2.5rem 0.6rem 1rem"
              >
                <option
                  v-for="dimension in availableDimensions"
                  :key="dimension.type"
                  :value="dimension.type"
                >
                  {{ dimension.name }}
                </option>
              </select>
              <div class="absolute inset-y-0 right-0 flex items-center px-3 pointer-events-none text-neon-cyan">
                <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"></path></svg>
              </div>
            </div>

            <!-- Time Range Pills -->
            <div class="explorer-time-pills">
              <button
                v-for="option in timeRangeOptions"
                :key="option.value"
                class="explorer-time-pill"
                :class="{ 'explorer-time-pill--active': selectedTimeRange === option.value }"
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
      <div v-if="slicedData.results.length > 0" class="explorer-stats-grid mb-4 sm:mb-8">
        <!-- Card 1: Count -->
        <div class="explorer-stat">
          <div class="explorer-stat-value">{{ slicedData.results.length }}</div>
          <div class="explorer-stat-label">{{ getResultTypeLabel() }}</div>
        </div>

        <!-- Card 2: Primary Metric -->
        <div class="explorer-stat">
          <div class="explorer-stat-value" :class="themeStatClass">{{ getTotalPrimaryValue() }}</div>
          <div class="explorer-stat-label">{{ getPrimaryMetricLabel() }}</div>
        </div>

        <!-- Card 3: Secondary Metric -->
        <div class="explorer-stat">
          <div class="explorer-stat-value">{{ getTotalSecondaryValue() }}</div>
          <div class="explorer-stat-label">{{ getSecondaryMetricLabel() }}</div>
        </div>

        <!-- Card 4: Percentage -->
        <div class="explorer-stat">
          <div class="explorer-stat-value" :class="percentageStatClass">{{ getAveragePercentage() }}<span class="text-sm ml-1 opacity-50">{{ getPercentageUnit() || '' }}</span></div>
          <div class="explorer-stat-label">{{ getPercentageLabel() }}</div>
        </div>
      </div>

      <!-- Results Table -->
      <div v-if="slicedData.results.length > 0">
        <h3 class="explorer-section-title mb-2 sm:mb-3">DETAILED RESULTS</h3>

        <div class="explorer-card" style="padding: 0">
          <div class="overflow-x-auto">
            <table class="explorer-table">
              <!-- Table Header -->
              <thead>
                <tr>
                  <th class="w-12 text-center">#</th>
                  <th class="text-left pl-4">{{ getTableHeaderLabel() }}</th>
                  <th class="text-right">{{ getSecondaryMetricLabel() }}</th>
                  <th class="text-right" :class="themeColorClass">{{ getPrimaryMetricLabel() }}</th>
                  <th class="text-right pr-6" :class="percentageColorClass">{{ getPercentageLabel() }}</th>
                  <th v-if="hasAdditionalData()" class="text-left pl-4">Additional Stats</th>
                </tr>
              </thead>

              <!-- Table Body -->
              <tbody>
                <tr
                  v-for="(result, index) in slicedData.results"
                  :key="`${result.sliceKey}-${result.subKey || 'global'}`"
                  class="cursor-pointer"
                  @click="handleSliceClick(result)"
                >
                  <!-- Rank -->
                  <td class="text-center explorer-mono">
                    <span :class="getRankClass(result.rank)">{{ result.rank }}</span>
                  </td>

                  <!-- Main Label -->
                  <td class="pl-4">
                    <div>
                      <span class="font-bold" :class="{ 'text-neon-cyan': isMapSlice() }" style="color: var(--text-primary)">
                        {{ result.sliceLabel }}
                      </span>
                      <span v-if="result.subKey" class="explorer-tag ml-2">
                        {{ result.subKeyLabel || getServerName(result.subKey) }}
                      </span>
                    </div>
                  </td>

                  <!-- Secondary Value -->
                  <td class="text-right explorer-mono explorer-table-muted">
                    {{ result.secondaryValue.toLocaleString() }}
                  </td>

                  <!-- Primary Value -->
                  <td class="text-right explorer-mono font-bold" :class="themeColorClass">
                    {{ result.primaryValue.toLocaleString() }}
                  </td>

                  <!-- Percentage -->
                  <td class="text-right pr-6 explorer-mono" :class="percentageColorClass">
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
                    <div v-else class="text-xs space-y-1" style="color: var(--text-secondary)">
                      <div v-for="(value, key) in result.additionalData" :key="key" class="flex gap-2">
                        <span class="opacity-70">{{ formatAdditionalKey(key) }}:</span>
                        <span class="explorer-mono" style="color: var(--text-primary)">{{ formatAdditionalValue(value) }}</span>
                      </div>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Pagination Controls -->
          <div v-if="slicedData.pagination.totalPages > 1" class="explorer-pagination">
            <span class="text-xs explorer-mono" style="color: var(--text-secondary)">
              PAGE <span style="color: var(--text-primary)">{{ slicedData.pagination.page }}</span> OF <span style="color: var(--text-primary)">{{ slicedData.pagination.totalPages }}</span>
            </span>

            <div class="explorer-pagination-controls">
              <button
                @click="changePage(slicedData.pagination.page - 1)"
                :disabled="!slicedData.pagination.hasPrevious || isLoading"
                class="explorer-pagination-btn"
              >
                &larr;
              </button>

              <button
                @click="changePage(slicedData.pagination.page + 1)"
                :disabled="!slicedData.pagination.hasNext || isLoading"
                class="explorer-pagination-btn"
              >
                &rarr;
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Empty State -->
      <div v-else class="explorer-empty">
        <div class="explorer-empty-icon">{ }</div>
        <p class="explorer-empty-title">NO DATA AVAILABLE</p>
        <p class="explorer-empty-desc">No statistics found for this player with the current filters.</p>
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

// Theme Logic - classes for neon text utilities (defined in DataExplorer.vue.css)
const themeColorClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'text-neon-red';
  if (type.includes('Wins')) return 'text-neon-green';
  return 'text-neon-cyan';
});

const percentageColorClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'text-neon-pink';
  if (type.includes('Wins')) return 'text-neon-green';
  return 'text-neon-gold';
});

// Stat value classes with text-shadow glow effects
const themeStatClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'explorer-stat-value--pink';
  if (type.includes('Wins')) return 'explorer-stat-value--green';
  return 'explorer-stat-value--accent';
});

const percentageStatClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'explorer-stat-value--pink';
  if (type.includes('Wins')) return 'explorer-stat-value--green';
  return 'explorer-stat-value--gold';
});

const getRankClass = (rank: number) => {
  if (rank === 1) return 'explorer-rank-1';
  if (rank === 2) return 'explorer-rank-2';
  if (rank === 3) return 'explorer-rank-3';
  return '';
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
/* ===== Styles mirroring DataExplorer.vue.css explorer-* patterns =====
   The parent CSS is <style scoped>, so child components don't inherit it.
   These replicate the explorer theme using the same variables & effects. */

/* --- Section Title --- */
.explorer-section-title {
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: var(--neon-cyan);
  margin: 0 0 0.75rem;
  font-family: 'JetBrains Mono', monospace;
  text-transform: uppercase;
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.3);
}

/* --- Stats Grid --- */
.explorer-stats-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
}

@media (min-width: 640px) {
  .explorer-stats-grid {
    grid-template-columns: repeat(4, 1fr);
  }
}

.explorer-stat {
  text-align: center;
  padding: 0.75rem 0.5rem;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  transition: all 0.3s ease;
}

@media (min-width: 640px) {
  .explorer-stat {
    padding: 1rem;
  }
}

.explorer-stat:hover {
  border-color: rgba(0, 255, 242, 0.3);
  box-shadow: 0 0 20px rgba(0, 255, 242, 0.1);
}

.explorer-stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-primary);
  font-family: 'JetBrains Mono', monospace;
}

.explorer-stat-value--accent {
  color: var(--neon-cyan);
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.5);
}

.explorer-stat-value--green {
  color: var(--neon-green);
  text-shadow: 0 0 10px rgba(57, 255, 20, 0.5);
}

.explorer-stat-value--pink {
  color: var(--neon-pink);
  text-shadow: 0 0 10px rgba(255, 0, 255, 0.5);
}

.explorer-stat-value--gold {
  color: var(--neon-gold);
  text-shadow: 0 0 10px rgba(255, 215, 0, 0.5);
}

.explorer-stat-label {
  font-size: 0.7rem;
  color: var(--text-secondary);
  margin-top: 0.25rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

/* --- Card --- */
.explorer-card {
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  overflow: hidden;
  transition: all 0.3s ease;
}

.explorer-card:hover {
  border-color: rgba(0, 255, 242, 0.3);
  box-shadow: 0 0 30px rgba(0, 255, 242, 0.1);
}

/* --- Table --- */
.explorer-table {
  width: 100%;
  font-size: 0.8rem;
  border-collapse: collapse;
}

.explorer-table th {
  text-align: left;
  padding: 0.5rem 0.5rem;
  background: var(--bg-card);
  color: var(--neon-cyan);
  font-weight: 600;
  letter-spacing: 0.06em;
  font-family: 'JetBrains Mono', monospace;
  border-bottom: 1px solid var(--border-color);
  font-size: 0.7rem;
  text-transform: uppercase;
  white-space: nowrap;
}

@media (min-width: 640px) {
  .explorer-table th {
    padding: 0.5rem 0.75rem;
  }
}

.explorer-table th.text-right {
  text-align: right;
}

.explorer-table td {
  padding: 0.5rem 0.5rem;
  border-bottom: 1px solid var(--border-color);
  color: var(--text-primary);
}

@media (min-width: 640px) {
  .explorer-table td {
    padding: 0.5rem 0.75rem;
  }
}

.explorer-table td.text-right {
  text-align: right;
}

.explorer-table tbody tr {
  transition: all 0.2s ease;
}

.explorer-table tbody tr:hover td {
  background: rgba(0, 255, 242, 0.08);
}

.explorer-table-muted {
  color: var(--text-secondary);
}

/* --- Tag --- */
.explorer-tag {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  font-size: 0.65rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
}

/* --- Select --- */
.explorer-select {
  padding: 0.35rem 0.5rem;
  font-size: 0.8rem;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.explorer-select:focus {
  outline: none;
  border-color: var(--neon-cyan);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.2);
}

/* --- Time Pills --- */
.explorer-time-pills {
  display: flex;
  gap: 0.25rem;
}

.explorer-time-pill {
  padding: 0.35rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
  text-transform: uppercase;
}

.explorer-time-pill:hover:not(:disabled) {
  color: var(--text-primary);
  border-color: rgba(0, 255, 242, 0.3);
}

.explorer-time-pill--active {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  border-color: var(--neon-cyan);
  box-shadow: 0 0 10px rgba(0, 255, 242, 0.4);
}

.explorer-time-pill:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* --- Pagination --- */
.explorer-pagination {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding: 0.5rem 0.5rem;
  border-top: 1px solid var(--border-color);
  background: var(--bg-card);
  font-size: 0.75rem;
  color: var(--text-secondary);
}

@media (min-width: 640px) {
  .explorer-pagination {
    gap: 0.75rem;
    padding: 0.6rem 0.75rem;
  }
}

.explorer-pagination-controls {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.explorer-pagination-btn {
  padding: 0.25rem 0.5rem;
  font-size: 0.7rem;
  font-weight: 600;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
  min-width: 1.5rem;
  text-align: center;
}

.explorer-pagination-btn:hover:not(:disabled) {
  background: rgba(0, 255, 242, 0.1);
  color: var(--neon-cyan);
  border-color: rgba(0, 255, 242, 0.3);
}

.explorer-pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* --- Buttons --- */
.explorer-btn {
  padding: 0.5rem 1rem;
  font-size: 0.8rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  font-family: 'JetBrains Mono', monospace;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s ease;
  border: 1px solid transparent;
  text-transform: uppercase;
}

.explorer-btn--ghost {
  background: transparent;
  color: var(--text-secondary);
  border-color: var(--border-color);
}

.explorer-btn--ghost:hover:not(:disabled) {
  color: var(--text-primary);
  border-color: var(--neon-cyan);
  background: rgba(0, 255, 242, 0.1);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.2);
}

.explorer-btn--sm {
  padding: 0.35rem 0.65rem;
  font-size: 0.75rem;
}

/* --- Empty State --- */
.explorer-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 3rem 1.5rem;
  text-align: center;
}

.explorer-empty-icon {
  font-size: 2.5rem;
  color: var(--neon-cyan);
  opacity: 0.5;
  margin-bottom: 0.75rem;
  font-family: 'JetBrains Mono', monospace;
  text-shadow: 0 0 20px rgba(0, 255, 242, 0.3);
}

.explorer-empty-title {
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: 0.04em;
}

.explorer-empty-desc {
  font-size: 0.8rem;
  color: var(--text-secondary);
  margin-top: 0.35rem;
  max-width: 20rem;
}

/* --- Skeleton Loading --- */
.explorer-skeleton {
  background: linear-gradient(
    90deg,
    var(--bg-card) 0%,
    var(--border-color) 50%,
    var(--bg-card) 100%
  );
  background-size: 200% 100%;
  animation: explorer-skeleton 1.5s ease-in-out infinite;
  border-radius: 4px;
}

@keyframes explorer-skeleton {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

/* --- Rank Colors --- */
.explorer-rank-1 {
  color: var(--neon-gold);
  font-weight: 700;
  text-shadow: 0 0 10px rgba(255, 215, 0, 0.5);
}

.explorer-rank-2 {
  color: #c0c0c0;
  font-weight: 600;
  text-shadow: 0 0 10px rgba(192, 192, 192, 0.3);
}

.explorer-rank-3 {
  color: #cd7f32;
  font-weight: 600;
  text-shadow: 0 0 10px rgba(205, 127, 50, 0.3);
}

/* --- Monospace --- */
.explorer-mono {
  font-family: 'JetBrains Mono', monospace;
}

/* --- Neon text utilities --- */
.text-neon-cyan { color: var(--neon-cyan); }
.text-neon-green { color: var(--neon-green); }
.text-neon-pink { color: var(--neon-pink); }
.text-neon-gold { color: var(--neon-gold); }
.text-neon-red { color: var(--neon-red); }

/* --- Floating label for slice selector --- */
.slice-select-wrap .slice-select-label {
  position: absolute;
  top: -0.625rem;
  left: 0.75rem;
  padding: 0 0.25rem;
  background: var(--bg-card);
  font-size: 0.625rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: var(--neon-cyan);
  font-family: 'JetBrains Mono', monospace;
  z-index: 10;
}
</style>
