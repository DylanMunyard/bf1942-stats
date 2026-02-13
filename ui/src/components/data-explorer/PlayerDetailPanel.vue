<template>
  <div class="p-6">
    <!-- Loading State -->
    <div v-if="isLoading" class="space-y-4">
      <div class="animate-pulse">
        <div class="h-8 bg-slate-700/50 rounded w-1/3 mb-2"></div>
        <div class="h-4 bg-slate-700/30 rounded w-1/4"></div>
      </div>
      <div class="h-32 bg-slate-700/30 rounded-lg animate-pulse"></div>
      <div class="h-48 bg-slate-700/30 rounded-lg animate-pulse"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="text-center py-8">
      <div class="text-slate-400 mb-4">{{ error }}</div>
      <div class="mb-4">
        <p class="text-slate-500 text-sm mb-3">Try selecting a different time period or slice dimension:</p>
        <div class="flex gap-2 justify-center mb-3">
          <button
            v-for="option in timeRangeOptions"
            :key="option.value"
            :class="[
              'px-3 py-1.5 rounded-lg text-sm font-medium transition-all duration-200',
              selectedTimeRange === option.value
                ? 'bg-gradient-to-r from-cyan-500 to-blue-500 text-white shadow-lg'
                : 'bg-slate-700/50 text-slate-300 hover:bg-slate-600/50 border border-slate-600'
            ]"
            @click="changeTimeRange(option.value)"
            :disabled="isLoading"
          >
            {{ option.label }}
          </button>
        </div>
      </div>
      <button @click="loadData()" class="text-cyan-400 hover:text-cyan-300 text-sm">
        Try again
      </button>
    </div>

    <!-- Content -->
    <div v-else-if="slicedData" class="space-y-6">
      <!-- Header -->
      <div>
        <div class="flex items-center gap-3 mb-4">
          <span class="text-3xl">👤</span>
          <h2 class="text-2xl font-bold">
            <RouterLink
              :to="{ name: 'player-details', params: { playerName: slicedData.playerName } }"
              class="text-slate-200 hover:text-cyan-400 transition-colors"
              title="View full player profile"
            >
              {{ slicedData.playerName }}
            </RouterLink>
          </h2>
        </div>

        <!-- Controls Row -->
        <div class="flex flex-col gap-4 mb-6">
          <!-- Slice Dimension Selector -->
          <div class="flex items-center gap-3">
            <label class="text-sm text-slate-400 whitespace-nowrap">Slice by:</label>
            <select
              v-model="selectedSliceType"
              @change="changeSliceType"
              class="px-3 py-2 bg-slate-700 border border-slate-600 rounded-lg text-sm text-slate-200 focus:border-cyan-500 focus:outline-none"
            >
              <option
                v-for="dimension in availableDimensions"
                :key="dimension.type"
                :value="dimension.type"
              >
                {{ dimension.name }}
              </option>
            </select>
          </div>

          <!-- Time Range Selector -->
          <div class="flex items-center justify-between">
            <div class="text-sm text-slate-400">
              {{ gameLabel }} &bull; Last {{ slicedData.dateRange.days }} days &bull; {{ slicedData.sliceDimension }}
            </div>
            <div class="flex gap-2">
              <button
                v-for="option in timeRangeOptions"
                :key="option.value"
                :class="[
                  'px-3 py-1.5 rounded-lg text-sm font-medium transition-all duration-200',
                  selectedTimeRange === option.value
                    ? 'bg-gradient-to-r from-cyan-500 to-blue-500 text-white shadow-lg'
                    : 'bg-slate-700/50 text-slate-300 hover:bg-slate-600/50 border border-slate-600'
                ]"
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
      <div v-if="slicedData.results.length > 0" class="bg-slate-800/30 rounded-lg p-4">
        <h3 class="text-sm font-medium text-slate-300 mb-3">Statistics Summary</h3>
        <div class="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <div class="text-center">
            <div class="text-2xl font-bold text-cyan-400">{{ slicedData.results.length }}</div>
            <div class="text-xs text-slate-400 mt-1">{{ getResultTypeLabel() }}</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold text-slate-200">{{ getTotalPrimaryValue() }}</div>
            <div class="text-xs text-slate-400 mt-1">{{ getPrimaryMetricLabel() }}</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold text-slate-200">{{ getTotalSecondaryValue() }}</div>
            <div class="text-xs text-slate-400 mt-1">{{ getSecondaryMetricLabel() }}</div>
          </div>
          <div class="text-center">
            <div class="text-2xl font-bold text-slate-200">{{ getAveragePercentage() }}%</div>
            <div class="text-xs text-slate-400 mt-1">{{ getPercentageLabel() }}</div>
          </div>
        </div>
      </div>

      <!-- Results List -->
      <div v-if="slicedData.results.length > 0">
        <div class="flex items-center justify-between mb-4">
          <h3 class="text-sm font-medium text-slate-300">Detailed Results</h3>
          <div class="text-sm text-slate-400">
            Page {{ slicedData.pagination.page }} of {{ slicedData.pagination.totalPages }}
            ({{ slicedData.pagination.totalItems }} total)
          </div>
        </div>
        
        <div class="space-y-3">
          <div
            v-for="(result, index) in slicedData.results"
            :key="`${result.sliceKey}-${result.subKey || 'global'}`"
            class="bg-slate-800/30 rounded-lg p-4 hover:bg-slate-700/30 transition-colors"
          >
            <div class="flex items-center justify-between mb-2">
              <div class="flex items-center gap-3">
                <div class="flex-shrink-0 w-8 h-8 bg-slate-700 rounded-full flex items-center justify-center text-sm font-bold text-slate-200">
                  {{ result.rank }}
                </div>
                <div>
                  <div class="text-slate-200 font-medium">{{ result.sliceLabel }}</div>
                  <div class="text-xs text-slate-500 mt-1">
                    {{ result.secondaryValue }} {{ getSecondaryMetricLabel().toLowerCase() }}
                    <span v-if="result.subKey" class="ml-2">
                      • {{ getServerName(result.subKey) }}
                    </span>
                  </div>
                </div>
              </div>
              <div class="text-right">
                <div class="text-lg font-bold text-cyan-400">{{ result.primaryValue.toLocaleString() }}</div>
                <div class="text-sm text-slate-400">{{ result.percentage.toFixed(1) }}{{ getPercentageUnit() }}</div>
              </div>
            </div>
            
            <!-- Additional Stats -->
            <div v-if="Object.keys(result.additionalData).length > 0" class="mt-3 pt-3 border-t border-slate-700/50">
              <div
                v-if="isTeamWinSlice() && (getTeamLabel(result.additionalData, 'team1Label') || getTeamLabel(result.additionalData, 'team2Label'))"
                class="grid grid-cols-2 gap-4 text-sm mb-3"
              >
                <div class="text-center">
                  <div class="font-semibold text-slate-300">{{ getTeamLabel(result.additionalData, 'team1Label') || 'Team 1' }}</div>
                  <div class="text-xs text-slate-500">Team 1 Name</div>
                </div>
                <div class="text-center">
                  <div class="font-semibold text-slate-300">{{ getTeamLabel(result.additionalData, 'team2Label') || 'Team 2' }}</div>
                  <div class="text-xs text-slate-500">Team 2 Name</div>
                </div>
              </div>
              <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
                <div v-for="(value, key) in getRenderableAdditionalData(result.additionalData)" :key="key" class="text-center">
                  <div class="font-semibold text-slate-300">{{ formatAdditionalValue(value) }}</div>
                  <div class="text-xs text-slate-500 capitalize">{{ formatAdditionalKey(key) }}</div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Pagination Controls -->
        <div v-if="slicedData.pagination.totalPages > 1" class="flex items-center justify-center gap-4 mt-6">
          <button
            @click="changePage(slicedData.pagination.page - 1)"
            :disabled="!slicedData.pagination.hasPrevious || isLoading"
            class="px-4 py-2 bg-slate-700/50 text-slate-300 rounded-lg transition-colors hover:bg-slate-600/50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          
          <div class="flex items-center gap-2">
            <template v-for="pageNum in getVisiblePages()" :key="pageNum">
              <button
                v-if="pageNum !== '...'"
                @click="changePage(pageNum)"
                :class="[
                  'w-8 h-8 rounded text-sm font-medium transition-colors',
                  pageNum === slicedData.pagination.page
                    ? 'bg-cyan-500 text-white'
                    : 'bg-slate-700/50 text-slate-300 hover:bg-slate-600/50'
                ]"
                :disabled="isLoading"
              >
                {{ pageNum }}
              </button>
              <span v-else class="text-slate-500 px-2">...</span>
            </template>
          </div>
          
          <button
            @click="changePage(slicedData.pagination.page + 1)"
            :disabled="!slicedData.pagination.hasNext || isLoading"
            class="px-4 py-2 bg-slate-700/50 text-slate-300 rounded-lg transition-colors hover:bg-slate-600/50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Next
          </button>
        </div>
      </div>

      <!-- Empty State -->
      <div v-else class="text-center py-12">
        <div class="text-6xl mb-4">📊</div>
        <h3 class="text-xl font-semibold text-slate-300 mb-2">No Data Available</h3>
        <p class="text-slate-400">No statistics found for this player with the current filters.</p>
        <p class="text-slate-500 text-sm mt-2">Try adjusting the time range or slice dimension.</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue';
import { RouterLink } from 'vue-router';
import { PLAYER_STATS_TIME_RANGE_OPTIONS } from '@/utils/constants';

const props = defineProps<{
  playerName: string;
  game?: string;
  serverGuid?: string; // Optional: filter to a specific server
}>();

const emit = defineEmits<{
  'navigate-to-server': [serverGuid: string];
  'navigate-to-map': [mapName: string];
}>();

// API Types (duplicated for now, should be moved to shared types later)
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
const selectedTimeRange = ref<number>(60); // Default to 60 days
const selectedSliceType = ref<string>('ScoreByMap'); // Default slice type
const currentPage = ref<number>(1);

const timeRangeOptions = PLAYER_STATS_TIME_RANGE_OPTIONS;

const gameLabel = computed(() => {
  switch (slicedData.value?.game?.toLowerCase()) {
    case 'bf1942': return 'Battlefield 1942';
    case 'fh2': return 'Forgotten Hope 2';
    case 'bfvietnam': return 'Battlefield Vietnam';
    default: return slicedData.value?.game || 'Unknown';
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
    // Provide fallback dimensions
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

const loadData = async (days?: number, page?: number) => {
  if (!props.playerName) return;

  const timeRange = days || selectedTimeRange.value;
  const pageNum = page || currentPage.value;
  
  isLoading.value = true;
  error.value = null;

  try {
    console.log(`Loading sliced player data for ${props.playerName} with ${timeRange} days, slice: ${selectedSliceType.value}, page: ${pageNum}`);
    
    const params = new URLSearchParams({
      sliceType: selectedSliceType.value,
      game: props.game || 'bf1942',
      page: pageNum.toString(),
      pageSize: '20',
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

    slicedData.value = await response.json();
    currentPage.value = pageNum;

    // Update document title
    if (slicedData.value?.playerName) {
      document.title = `${slicedData.value.playerName} - Enhanced Data Explorer | BF Stats`;
    }
  } catch (err: any) {
    console.error(`Error loading sliced player data:`, err);
    error.value = err.message || 'Failed to load player details';
  }

  isLoading.value = false;
};

const changeTimeRange = (days: number) => {
  selectedTimeRange.value = days;
  currentPage.value = 1;
  loadData(days, 1);
};

const changeSliceType = () => {
  currentPage.value = 1;
  loadData(selectedTimeRange.value, 1);
};

const changePage = (page: number) => {
  if (page < 1 || (slicedData.value && page > slicedData.value.pagination.totalPages)) return;
  currentPage.value = page;
  loadData(selectedTimeRange.value, page);
};

// UI Helper Methods
const getResultTypeLabel = () => {
  if (!slicedData.value) return 'Results';
  return selectedSliceType.value.includes('Server') ? 'Map-Server Combinations' : 'Maps';
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
  // This would need to be populated from server data
  return serverGuid.substring(0, 8) + '...';
};

const isTeamWinSlice = () => selectedSliceType.value.includes('TeamWins');

const getTeamLabel = (additionalData: Record<string, any>, key: 'team1Label' | 'team2Label') => {
  const value = additionalData?.[key];
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
};

const getRenderableAdditionalData = (additionalData: Record<string, any>) => {
  if (!isTeamWinSlice()) {
    return additionalData;
  }

  return Object.fromEntries(
    Object.entries(additionalData).filter(([key]) => key !== 'team1Label' && key !== 'team2Label')
  );
};

const getVisiblePages = () => {
  if (!slicedData.value) return [];
  
  const { page, totalPages } = slicedData.value.pagination;
  const pages = [];
  
  if (totalPages <= 7) {
    // Show all pages if total is small
    for (let i = 1; i <= totalPages; i++) {
      pages.push(i);
    }
  } else {
    // Show smart pagination with ellipsis
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