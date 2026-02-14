<template>
  <div class="player-detail-panel">
    <!-- Loading State -->
    <div v-if="isLoading" class="space-y-4 p-6">
      <div class="explorer-skeleton h-8 w-1/3 mb-2"></div>
      <div class="explorer-skeleton h-4 w-1/4"></div>
      <div class="explorer-skeleton h-32 mt-4"></div>
      <div class="explorer-skeleton h-48 mt-4"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="explorer-empty">
      <div class="explorer-empty-icon">
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>
      </div>
      <p class="explorer-empty-title">{{ error }}</p>
      <p class="explorer-empty-desc mb-4">Try selecting a different time period or slice dimension.</p>
      <div class="flex gap-2 justify-center mb-4">
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
      <button @click="loadData()" class="explorer-btn explorer-btn--ghost explorer-btn--sm">
        TRY AGAIN
      </button>
    </div>

    <!-- Content -->
    <div v-else-if="slicedData" class="space-y-6 p-4 md:p-6">

      <!-- Context Banner -->
      <div class="context-banner" :class="themeClass">
        <div class="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div>
            <h3 class="text-sm font-bold tracking-wider uppercase text-white">{{ getCurrentSliceName() }}</h3>
            <p class="text-xs mt-1 text-neutral-400">{{ getCurrentSliceDescription() }}</p>
          </div>
          <div class="text-right flex flex-col items-end gap-1">
            <span class="explorer-tag">{{ gameLabel }}</span>
            <span class="text-xs font-mono text-neutral-500">LAST {{ slicedData.dateRange.days }} DAYS</span>
          </div>
        </div>
      </div>

      <!-- Controls Row -->
      <div class="controls-row">
        <!-- Slice Dimension Selector -->
        <div class="flex flex-col gap-2 w-full md:w-auto flex-1">
          <label class="control-label">METRIC DIMENSION</label>
          <div class="relative">
            <select
              v-model="selectedSliceType"
              @change="changeSliceType"
              class="explorer-select w-full md:max-w-md h-10"
            >
              <option
                v-for="dimension in availableDimensions"
                :key="dimension.type"
                :value="dimension.type"
              >
                {{ dimension.name }}
              </option>
            </select>
            <div class="absolute inset-y-0 right-0 flex items-center px-2 pointer-events-none text-neutral-500">
              <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"></path></svg>
            </div>
          </div>
        </div>

        <!-- Time Range Selector -->
        <div class="flex flex-col gap-2 w-full md:w-auto">
          <label class="control-label md:text-right">TIME PERIOD</label>
          <div class="explorer-toggle-group h-10">
            <button
              v-for="option in timeRangeOptions"
              :key="option.value"
              class="explorer-toggle-btn flex-1 md:flex-none px-4"
              :class="{ 'explorer-toggle-btn--active': selectedTimeRange === option.value }"
              @click="changeTimeRange(option.value)"
              :disabled="isLoading"
            >
              {{ option.label }}
            </button>
          </div>
        </div>
      </div>

      <!-- Summary Stats -->
      <div v-if="slicedData.results.length > 0" class="explorer-stats-grid">
        <!-- Card 1: Count -->
        <div class="explorer-stat">
          <div class="explorer-stat-value">{{ slicedData.results.length }}</div>
          <div class="explorer-stat-label">{{ getResultTypeLabel() }}</div>
        </div>

        <!-- Card 2: Primary Metric (themed) -->
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
          <div class="explorer-stat-value">{{ getAveragePercentage() }}<span class="text-sm ml-1 text-neutral-500">{{ getPercentageUnit() || '' }}</span></div>
          <div class="explorer-stat-label">{{ getPercentageLabel() }}</div>
        </div>
      </div>

      <!-- Results Table -->
      <div v-if="slicedData.results.length > 0" class="flex flex-col h-full">
        <div class="flex items-center justify-between mb-4 px-1">
          <h3 class="explorer-section-title mb-0 text-lg">DETAILED RESULTS</h3>
          <div class="text-xs font-mono text-neutral-500">
            PAGE {{ slicedData.pagination.page }}/{{ slicedData.pagination.totalPages }}
            <span class="mx-2 text-neutral-700">|</span>
            {{ slicedData.pagination.totalItems }} ITEMS
          </div>
        </div>

        <div class="explorer-details w-full overflow-hidden rounded-lg border border-[var(--border-color)]">
          <div class="overflow-x-auto">
            <table class="explorer-table w-full">
              <!-- Table Header -->
              <thead>
                <tr class="bg-black/20">
                  <th class="w-16 text-center py-4">RANK</th>
                  <th class="py-4 text-left pl-4">{{ getTableHeaderLabel() }}</th>
                  <th class="w-32 text-right py-4">{{ getSecondaryMetricLabel() }}</th>
                  <th class="w-36 text-right py-4" :class="themeTextClass">{{ getPrimaryMetricLabel() }}</th>
                  <th class="w-32 text-right py-4 pr-6">{{ getPercentageLabel() }}</th>
                  <th v-if="hasAdditionalData()" class="w-64 py-4 pr-4">ADDITIONAL STATS</th>
                </tr>
              </thead>

              <!-- Table Body -->
              <tbody class="divide-y divide-[var(--border-color)]">
                <tr
                  v-for="(result, index) in slicedData.results"
                  :key="`${result.sliceKey}-${result.subKey || 'global'}`"
                  class="hover:bg-white/5 transition-colors"
                >
                  <!-- Rank -->
                  <td class="text-center py-4">
                    <div class="rank-badge mx-auto" :class="index < 3 ? `rank-badge--top rank-badge--${index + 1}` : ''">
                      {{ result.rank }}
                    </div>
                  </td>

                  <!-- Main Label -->
                  <td class="py-4 pl-4">
                    <div
                      class="font-medium text-base truncate max-w-[200px] md:max-w-xs lg:max-w-md"
                      :class="{ 'explorer-link cursor-pointer hover:underline': isMapSlice() }"
                      @click="handleSliceClick(result)"
                    >
                      {{ result.sliceLabel }}
                    </div>
                    <div v-if="result.subKey" class="text-xs mt-1.5 flex items-center gap-1.5 text-neutral-400">
                      <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="opacity-70"><rect x="2" y="2" width="20" height="8" rx="2" ry="2"/><rect x="2" y="14" width="20" height="8" rx="2" ry="2"/><line x1="6" y1="6" x2="6.01" y2="6"/><line x1="6" y1="18" x2="6.01" y2="18"/></svg>
                      {{ result.subKeyLabel || getServerName(result.subKey) }}
                    </div>
                  </td>

                  <!-- Secondary Value -->
                  <td class="text-right py-4 font-mono text-neutral-400">
                    {{ result.secondaryValue.toLocaleString() }}
                  </td>

                  <!-- Primary Value -->
                  <td class="text-right py-4">
                    <div class="text-lg font-bold font-mono" :class="themeTextClass">{{ result.primaryValue.toLocaleString() }}</div>
                  </td>

                  <!-- Percentage -->
                  <td class="text-right py-4 pr-6 font-mono text-white">
                    {{ result.percentage.toFixed(1) }}<span class="text-xs ml-0.5 text-neutral-500">{{ getPercentageUnit() }}</span>
                  </td>

                  <!-- Additional Data -->
                  <td v-if="hasAdditionalData()" class="py-4 pr-4">
                    <div v-if="isTeamWinSlice()" class="space-y-2">
                      <!-- Visual Win Rate Bar -->
                      <div v-if="getTeamLabel(result.additionalData, 'team1Label') || getTeamLabel(result.additionalData, 'team2Label')" class="px-2">
                        <WinStatsBar :winStats="getTeamWinStats(result)" />
                      </div>
                      <!-- Other additional data for team wins -->
                      <div v-if="Object.keys(getTeamWinAdditionalData(result.additionalData, result.percentage)).length > 0" class="text-xs text-neutral-400">
                        <div v-for="(value, key) in getTeamWinAdditionalData(result.additionalData, result.percentage)" :key="key" class="flex justify-between">
                          <span>{{ formatTeamWinKey(key, result.additionalData) }}:</span>
                          <span class="font-mono text-neutral-300">{{ formatAdditionalValue(value) }}</span>
                        </div>
                      </div>
                    </div>
                    <div v-else class="text-xs space-y-1.5 text-neutral-400">
                      <div v-for="(value, key) in result.additionalData" :key="key" class="flex justify-between additional-row">
                        <span>{{ formatAdditionalKey(key) }}:</span>
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
        <div v-if="slicedData.pagination.totalPages > 1" class="explorer-pagination mt-6 justify-center">
          <div class="text-xs font-mono" style="color: var(--text-secondary)">
            {{ slicedData.pagination.totalItems }} RESULTS
          </div>
          <div class="explorer-pagination-controls">
            <button
              @click="changePage(slicedData.pagination.page - 1)"
              :disabled="!slicedData.pagination.hasPrevious || isLoading"
              class="explorer-pagination-btn"
            >
              &larr;
            </button>

            <template v-for="pageNum in getVisiblePages()" :key="pageNum">
              <button
                v-if="pageNum !== '...'"
                @click="changePage(pageNum)"
                class="explorer-pagination-btn"
                :class="{ 'explorer-pagination-btn--active': pageNum === slicedData.pagination.page }"
                :disabled="isLoading"
              >
                {{ pageNum }}
              </button>
              <span v-else class="text-xs font-mono px-1" style="color: var(--text-secondary)">...</span>
            </template>

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

      <!-- Empty State -->
      <div v-else class="explorer-empty">
        <div class="explorer-empty-icon">
          <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg>
        </div>
        <p class="explorer-empty-title">NO DATA AVAILABLE</p>
        <p class="explorer-empty-desc">No statistics found for this player with the current filters. Try adjusting the time range or slice dimension.</p>
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

// Theme classes mapped to neon design system
const themeClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'theme--kills';
  if (type.includes('Wins')) return 'theme--wins';
  return 'theme--score';
});

const themeTextClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'text-neon-red';
  if (type.includes('Wins')) return 'text-neon-green';
  return 'text-neon-cyan';
});

const themeStatClass = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) return 'explorer-stat-value--pink';
  if (type.includes('Wins')) return 'explorer-stat-value--green';
  return 'explorer-stat-value--accent';
});

const gameLabel = computed(() => {
  switch (slicedData.value?.game?.toLowerCase()) {
    case 'bf1942': return 'BF1942';
    case 'fh2': return 'FH2';
    case 'bfvietnam': return 'BFV';
    default: return slicedData.value?.game || 'UNKNOWN';
  }
});

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

const formatTeamWinKey = (key: string, additionalData: Record<string, any>) => {
  if (key === 'team1WinRate') {
    const teamName = getTeamLabel(additionalData, 'team1Label') || 'Team 1';
    return `${teamName} Win Rate`;
  }
  if (key === 'team2WinRate') {
    const teamName = getTeamLabel(additionalData, 'team2Label') || 'Team 2';
    return `${teamName} Win Rate`;
  }
  return formatAdditionalKey(key);
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

const getTeamWinAdditionalData = (additionalData: Record<string, any>, team1WinRate: number) => {
  if (!isTeamWinSlice()) {
    return additionalData;
  }

  const displayData = { ...additionalData };
  displayData.team1WinRate = team1WinRate;

  return Object.fromEntries(
    Object.entries(displayData).filter(([key]) =>
      key !== 'team1Label' &&
      key !== 'team2Label' &&
      key !== 'team1Victories' &&
      key !== 'team2Victories'
    )
  );
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

const getVisiblePages = () => {
  if (!slicedData.value) return [];

  const { page, totalPages } = slicedData.value.pagination;
  const pages = [];

  if (totalPages <= 7) {
    for (let i = 1; i <= totalPages; i++) {
      pages.push(i);
    }
  } else {
    if (page <= 4) {
      pages.push(1, 2, 3, 4, 5, '...', totalPages);
    } else if (page >= totalPages - 3) {
      pages.push(1, '...', totalPages - 4, totalPages - 3, totalPages - 2, totalPages - 1, totalPages);
    } else {
      pages.push(1, '...', page - 1, page, page + 1, '...', totalPages);
    }
  }

  return pages;
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
/* Context Banner - terminal style with themed left border */
.context-banner {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  padding: 1rem 1.25rem;
  border-left: 3px solid var(--neon-cyan);
  transition: border-color 0.3s ease;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
}

.context-banner.theme--kills {
  border-left-color: var(--neon-red);
}

.context-banner.theme--wins {
  border-left-color: var(--neon-green);
}

.context-banner.theme--score {
  border-left-color: var(--neon-cyan);
}

/* Controls row */
.controls-row {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  justify-content: space-between;
  align-items: flex-end;
  background: rgba(255, 255, 255, 0.02);
  padding: 1.25rem;
  border-radius: 6px;
  border: 1px solid var(--border-color);
}

@media (min-width: 768px) {
  .controls-row {
    flex-direction: row;
    align-items: center;
  }
}

.control-label {
  font-size: 0.65rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: var(--text-secondary);
  font-family: 'JetBrains Mono', monospace;
  text-transform: uppercase;
  margin-bottom: 0.25rem;
  display: block;
}

/* Rank badges */
.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 2rem;
  height: 2rem;
  border-radius: 4px;
  font-size: 0.85rem;
  font-weight: 700;
  font-family: 'JetBrains Mono', monospace;
  background: rgba(255, 255, 255, 0.05);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
  transition: transform 0.2s ease;
}

.rank-badge--top {
  border: none;
  color: var(--bg-dark);
}

.rank-badge--1 {
  background: var(--neon-gold);
  box-shadow: 0 0 15px rgba(255, 215, 0, 0.4);
}

.rank-badge--2 {
  background: #c0c0c0;
  box-shadow: 0 0 15px rgba(192, 192, 192, 0.3);
}

.rank-badge--3 {
  background: #cd7f32;
  box-shadow: 0 0 15px rgba(205, 127, 50, 0.3);
}

tr:hover .rank-badge {
  transform: scale(1.1);
}

/* Additional data rows */
.additional-row {
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  padding-bottom: 0.25rem;
  margin-bottom: 0.25rem;
}

.additional-row:last-child {
  border-bottom: none;
  padding-bottom: 0;
  margin-bottom: 0;
}

/* Neon text colors (matching the data-explorer theme utilities) */
.text-neon-cyan { color: var(--neon-cyan); text-shadow: 0 0 10px rgba(0, 255, 242, 0.3); }
.text-neon-green { color: var(--neon-green); text-shadow: 0 0 10px rgba(57, 255, 20, 0.3); }
.text-neon-red { color: var(--neon-red); text-shadow: 0 0 10px rgba(255, 49, 49, 0.3); }

/* Pagination spacing override - since we're not inside an explorer-card footer */
.explorer-pagination {
  border-radius: 4px;
  border: 1px solid var(--border-color);
  background: var(--bg-card);
  padding: 0.75rem;
}

/* Stats Grid Override */
.explorer-stats-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
}

@media (min-width: 768px) {
  .explorer-stats-grid {
    grid-template-columns: repeat(4, 1fr);
    gap: 1.5rem;
  }
}

.explorer-stat {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  padding: 1.5rem 1rem;
  text-align: center;
  transition: all 0.2s ease;
  position: relative;
  overflow: hidden;
}

.explorer-stat::before {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 2px;
  background: linear-gradient(90deg, transparent, var(--border-color), transparent);
  opacity: 0.5;
}

.explorer-stat:hover {
  border-color: rgba(255, 255, 255, 0.2);
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
}

.explorer-stat-value {
  font-size: 1.75rem;
  font-weight: 800;
  margin-bottom: 0.25rem;
  line-height: 1.2;
}

.explorer-stat-label {
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.08em;
  color: var(--text-secondary);
  text-transform: uppercase;
}

/* Table overrides */
.explorer-table th {
  font-size: 0.7rem;
  letter-spacing: 0.08em;
  color: var(--text-secondary);
  text-transform: uppercase;
  font-weight: 600;
  border-bottom: 1px solid var(--border-color);
}

.explorer-table td {
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.explorer-table tr:last-child td {
  border-bottom: none;
}

/* Custom scrollbar for table */
.explorer-details::-webkit-scrollbar {
  height: 8px;
}

.explorer-details::-webkit-scrollbar-track {
  background: var(--bg-panel);
}

.explorer-details::-webkit-scrollbar-thumb {
  background: var(--border-color);
  border-radius: 4px;
}

.explorer-details::-webkit-scrollbar-thumb:hover {
  background: var(--text-secondary);
}
</style>
