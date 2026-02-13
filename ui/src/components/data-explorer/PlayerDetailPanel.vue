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
    <div v-else-if="error" class="text-center py-12 bg-slate-800/30 rounded-xl border border-slate-700/50">
      <div class="text-slate-400 mb-4 text-lg">{{ error }}</div>
      <div class="mb-6">
        <p class="text-slate-500 text-sm mb-3">Try selecting a different time period or slice dimension:</p>
        <div class="flex gap-2 justify-center mb-3">
          <button
            v-for="option in timeRangeOptions"
            :key="option.value"
            :class="[
              'px-3 py-1.5 rounded-lg text-sm font-medium transition-all duration-200',
              selectedTimeRange === option.value
                ? `${theme.activeButton} shadow-lg`
                : 'bg-slate-700/50 text-slate-300 hover:bg-slate-600/50 border border-slate-600'
            ]"
            @click="changeTimeRange(option.value)"
            :disabled="isLoading"
          >
            {{ option.label }}
          </button>
        </div>
      </div>
      <button @click="loadData()" class="text-sm font-medium hover:underline" :class="theme.text">
        Try again
      </button>
    </div>

    <!-- Content -->
    <div v-else-if="slicedData" class="space-y-6">
      <!-- Header -->
      <div>
        <div class="flex items-center gap-3 mb-4">
          <h2 class="text-2xl font-bold">
            <RouterLink
              :to="{ name: 'player-details', params: { playerName: slicedData.playerName } }"
              class="text-slate-200 hover:text-white transition-colors"
              title="View full player profile"
            >
              {{ slicedData.playerName }}
            </RouterLink>
          </h2>
        </div>

        <!-- Current Data Context Banner - THEMED -->
        <div
          class="relative overflow-hidden rounded-xl border p-6 mb-6 transition-all duration-300 shadow-lg"
          :class="[
            theme.bgGradient,
            theme.borderColor
          ]"
        >
          <div class="relative z-10 flex flex-col md:flex-row md:items-center justify-between gap-4">
            <div>
              <div class="flex items-center gap-3 mb-2">
                <h3 class="text-xl font-bold text-white tracking-tight">{{ getCurrentSliceName() }}</h3>
              </div>
              <p class="text-slate-200/90 max-w-xl text-sm leading-relaxed">{{ getCurrentSliceDescription() }}</p>
            </div>
            <div class="text-right flex flex-col items-end">
               <div class="text-xs uppercase tracking-wider text-white/60 mb-1 font-semibold">Context</div>
               <div class="font-mono text-lg font-medium text-white mb-0.5">{{ gameLabel }}</div>
               <div class="text-sm text-white/80 bg-black/20 px-2 py-0.5 rounded inline-block">Last {{ slicedData.dateRange.days }} days</div>
            </div>
          </div>
        </div>

        <!-- Controls Row -->
        <div class="flex flex-col md:flex-row gap-4 mb-6 justify-between items-end md:items-center bg-slate-900/50 p-4 rounded-xl border border-slate-800/50">
          <!-- Slice Dimension Selector -->
          <div class="flex flex-col gap-1.5 w-full md:w-auto">
            <label class="text-xs font-bold text-slate-500 uppercase tracking-wider">Metric Dimension</label>
            <div class="relative group">
                <select
                  v-model="selectedSliceType"
                  @change="changeSliceType"
                  class="w-full md:w-72 appearance-none pl-4 pr-10 py-2.5 bg-slate-800 border border-slate-700 rounded-lg text-sm text-slate-200 focus:outline-none focus:ring-2 transition-all cursor-pointer hover:border-slate-600 hover:bg-slate-750"
                  :class="theme.focusRing"
                >
                  <option
                    v-for="dimension in availableDimensions"
                    :key="dimension.type"
                    :value="dimension.type"
                  >
                    {{ dimension.name }}
                  </option>
                </select>
                <div class="absolute right-3 top-1/2 -translate-y-1/2 pointer-events-none text-slate-400 group-hover:text-slate-200 transition-colors">
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>
                </div>
            </div>
          </div>

          <!-- Time Range Selector -->
          <div class="flex flex-col gap-1.5 w-full md:w-auto">
             <label class="text-xs font-bold text-slate-500 uppercase tracking-wider md:text-right">Time Period</label>
            <div class="flex bg-slate-800 p-1 rounded-lg border border-slate-700">
              <button
                v-for="option in timeRangeOptions"
                :key="option.value"
                :class="[
                  'px-4 py-1.5 rounded-md text-sm font-medium transition-all duration-200 flex-1 md:flex-none',
                  selectedTimeRange === option.value
                    ? `${theme.activeButton} shadow-sm`
                    : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
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

      <!-- Summary Stats - THEMED -->
      <div v-if="slicedData.results.length > 0" class="grid grid-cols-2 md:grid-cols-4 gap-4">
          <!-- Card 1: Count -->
          <div class="bg-slate-800/40 border border-slate-700/50 rounded-xl p-4 flex flex-col items-center justify-center hover:bg-slate-800/60 transition-colors group">
            <div class="text-3xl font-bold text-slate-200 group-hover:text-white transition-colors">{{ slicedData.results.length }}</div>
            <div class="text-xs font-bold text-slate-500 uppercase tracking-wider mt-1">{{ getResultTypeLabel() }}</div>
          </div>

          <!-- Card 2: Primary Metric -->
          <div class="bg-slate-800/40 border border-slate-700/50 rounded-xl p-4 flex flex-col items-center justify-center hover:bg-slate-800/60 transition-colors relative overflow-hidden group">
             <div class="absolute top-0 left-0 w-full h-1" :class="theme.bg"></div>
             <div class="text-3xl font-bold transition-transform group-hover:scale-110 duration-200" :class="theme.text">{{ getTotalPrimaryValue() }}</div>
             <div class="text-xs font-bold text-slate-500 uppercase tracking-wider mt-1">{{ getPrimaryMetricLabel() }}</div>
          </div>

          <!-- Card 3: Secondary Metric -->
          <div class="bg-slate-800/40 border border-slate-700/50 rounded-xl p-4 flex flex-col items-center justify-center hover:bg-slate-800/60 transition-colors group">
             <div class="text-3xl font-bold text-slate-200 group-hover:text-white transition-colors">{{ getTotalSecondaryValue() }}</div>
             <div class="text-xs font-bold text-slate-500 uppercase tracking-wider mt-1">{{ getSecondaryMetricLabel() }}</div>
          </div>

          <!-- Card 4: Percentage -->
          <div class="bg-slate-800/40 border border-slate-700/50 rounded-xl p-4 flex flex-col items-center justify-center hover:bg-slate-800/60 transition-colors group">
             <div class="text-3xl font-bold text-slate-200 group-hover:text-white transition-colors">{{ getAveragePercentage() }}<span class="text-lg text-slate-500 ml-0.5">%</span></div>
             <div class="text-xs font-bold text-slate-500 uppercase tracking-wider mt-1">{{ getPercentageLabel() }}</div>
          </div>
      </div>

      <!-- Results Table -->
      <div v-if="slicedData.results.length > 0">
        <div class="flex items-center justify-between mb-4 px-1">
          <h3 class="text-sm font-bold text-slate-400 uppercase tracking-wider">Detailed Results</h3>
          <div class="text-sm text-slate-500 font-mono">
            Page {{ slicedData.pagination.page }} / {{ slicedData.pagination.totalPages }}
            <span class="text-slate-600 mx-2">|</span>
            {{ slicedData.pagination.totalItems }} items
          </div>
        </div>

        <div class="bg-slate-900/40 border border-slate-800 rounded-xl overflow-hidden shadow-xl">
          <table class="w-full table-fixed">
            <!-- Table Header -->
            <thead class="bg-slate-900/80 border-b border-slate-800">
              <tr>
                <th class="w-16 px-4 py-4 text-center text-xs font-bold text-slate-500 uppercase tracking-wider">Rank</th>
                <th class="px-4 py-4 text-left text-xs font-bold text-slate-500 uppercase tracking-wider min-w-0">{{ getTableHeaderLabel() }}</th>
                <th class="w-24 px-4 py-4 text-right text-xs font-bold text-slate-500 uppercase tracking-wider">{{ getSecondaryMetricLabel() }}</th>
                <th class="w-28 px-4 py-4 text-right text-xs font-bold uppercase tracking-wider" :class="theme.text">{{ getPrimaryMetricLabel() }}</th>
                <th class="w-24 px-4 py-4 text-right text-xs font-bold text-slate-500 uppercase tracking-wider">{{ getPercentageLabel() }}</th>
                <th v-if="hasAdditionalData()" class="w-48 px-4 py-4 text-left text-xs font-bold text-slate-500 uppercase tracking-wider">Additional Stats</th>
              </tr>
            </thead>

            <!-- Table Body -->
            <tbody class="divide-y divide-slate-800/50">
              <tr
                v-for="(result, index) in slicedData.results"
                :key="`${result.sliceKey}-${result.subKey || 'global'}`"
                class="group hover:bg-slate-800/30 transition-colors duration-150"
              >
                <!-- Rank -->
                <td class="px-4 py-3">
                  <div class="flex items-center justify-center">
                    <div 
                        class="flex-shrink-0 w-8 h-8 rounded-lg flex items-center justify-center text-sm font-bold text-white shadow-sm transition-transform group-hover:scale-110"
                        :class="index < 3 ? theme.bg : 'bg-slate-800 text-slate-400'"
                    >
                      {{ result.rank }}
                    </div>
                  </div>
                </td>

                <!-- Main Label -->
                <td class="px-4 py-3">
                  <div class="text-slate-200 font-medium group-hover:text-white transition-colors text-base">{{ result.sliceLabel }}</div>
                  <div v-if="result.subKey" class="text-xs text-slate-500 mt-1 flex items-center gap-1.5">
                    <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="opacity-70"><rect x="2" y="2" width="20" height="8" rx="2" ry="2"/><rect x="2" y="14" width="20" height="8" rx="2" ry="2"/><line x1="6" y1="6" x2="6.01" y2="6"/><line x1="6" y1="18" x2="6.01" y2="18"/></svg>
                    {{ result.subKeyLabel || getServerName(result.subKey) }}
                  </div>
                </td>

                <!-- Secondary Value -->
                <td class="px-4 py-3 text-right text-slate-400 font-mono text-sm group-hover:text-slate-300">
                  {{ result.secondaryValue.toLocaleString() }}
                </td>

                <!-- Primary Value -->
                <td class="px-4 py-3 text-right">
                  <div class="text-lg font-bold font-mono" :class="theme.text">{{ result.primaryValue.toLocaleString() }}</div>
                </td>

                <!-- Percentage -->
                <td class="px-4 py-3 text-right text-slate-300 font-mono text-sm">
                  {{ result.percentage.toFixed(1) }}<span class="text-slate-500 text-xs ml-0.5">{{ getPercentageUnit() }}</span>
                </td>

                <!-- Additional Data -->
                <td v-if="hasAdditionalData()" class="px-4 py-3">
                  <div v-if="isTeamWinSlice()" class="space-y-2">
                    <!-- Visual Win Rate Bar -->
                    <div v-if="getTeamLabel(result.additionalData, 'team1Label') || getTeamLabel(result.additionalData, 'team2Label')" class="px-2">
                      <WinStatsBar :winStats="getTeamWinStats(result)" />
                    </div>
                    <!-- Other additional data for team wins -->
                    <div v-if="Object.keys(getTeamWinAdditionalData(result.additionalData, result.percentage)).length > 0" class="text-xs text-slate-400">
                      <div v-for="(value, key) in getTeamWinAdditionalData(result.additionalData, result.percentage)" :key="key" class="flex justify-between">
                        <span>{{ formatTeamWinKey(key, result.additionalData) }}:</span>
                        <span class="text-slate-300 font-mono">{{ formatAdditionalValue(value) }}</span>
                      </div>
                    </div>
                  </div>
                  <div v-else class="text-xs text-slate-400 space-y-1">
                    <div v-for="(value, key) in result.additionalData" :key="key" class="flex justify-between border-b border-slate-800/50 pb-0.5 last:border-0">
                      <span>{{ formatAdditionalKey(key) }}:</span>
                      <span class="text-slate-300 font-mono">{{ formatAdditionalValue(value) }}</span>
                    </div>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Pagination Controls -->
        <div v-if="slicedData.pagination.totalPages > 1" class="flex items-center justify-center gap-4 mt-8">
          <button
            @click="changePage(slicedData.pagination.page - 1)"
            :disabled="!slicedData.pagination.hasPrevious || isLoading"
            class="px-4 py-2 bg-slate-800 border border-slate-700 text-slate-300 rounded-lg transition-colors hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed hover:text-white"
          >
            Previous
          </button>
          
          <div class="flex items-center gap-2">
            <template v-for="pageNum in getVisiblePages()" :key="pageNum">
              <button
                v-if="pageNum !== '...'"
                @click="changePage(pageNum)"
                :class="[
                  'w-9 h-9 rounded-lg text-sm font-bold transition-all',
                  pageNum === slicedData.pagination.page
                    ? `${theme.activeButton} shadow-md`
                    : 'bg-slate-800 border border-slate-700 text-slate-400 hover:bg-slate-700 hover:text-white'
                ]"
                :disabled="isLoading"
              >
                {{ pageNum }}
              </button>
              <span v-else class="text-slate-600 px-2 font-bold">...</span>
            </template>
          </div>
          
          <button
            @click="changePage(slicedData.pagination.page + 1)"
            :disabled="!slicedData.pagination.hasNext || isLoading"
            class="px-4 py-2 bg-slate-800 border border-slate-700 text-slate-300 rounded-lg transition-colors hover:bg-slate-700 disabled:opacity-50 disabled:cursor-not-allowed hover:text-white"
          >
            Next
          </button>
        </div>
      </div>

      <!-- Empty State -->
      <div v-else class="text-center py-16 bg-slate-800/20 rounded-xl border border-dashed border-slate-700">
        <div class="text-6xl mb-6 opacity-50 grayscale hover:grayscale-0 transition-all duration-500 cursor-default">📊</div>
        <h3 class="text-xl font-bold text-slate-300 mb-2">No Data Available</h3>
        <p class="text-slate-400 max-w-md mx-auto">No statistics found for this player with the current filters.</p>
        <p class="text-slate-500 text-sm mt-4">Try adjusting the time range or slice dimension.</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, computed } from 'vue';
import { RouterLink } from 'vue-router';
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
const selectedTimeRange = ref<number>(60); // Default to 60 days
const selectedSliceType = ref<string>('ScoreByMap'); // Default slice type
const currentPage = ref<number>(1);
const pageSize = ref<number>(10); // Client-side pagination with 10 rows per page
const allResults = ref<PlayerSliceResultDto[]>([]); // Store all results for client-side pagination

const timeRangeOptions = PLAYER_STATS_TIME_RANGE_OPTIONS;

// Computed Theme
const theme = computed(() => {
  const type = selectedSliceType.value;
  if (type.includes('Kills')) {
    return {
      name: 'kills',
      icon: '⚔️',
      text: 'text-rose-400',
      bg: 'bg-rose-500',
      border: 'border-rose-500',
      borderColor: 'border-rose-500/30',
      bgGradient: 'bg-gradient-to-br from-rose-900/90 to-slate-900',
      activeButton: 'bg-rose-600 text-white',
      focusRing: 'focus:border-rose-500 focus:ring-rose-500/20'
    };
  } else if (type.includes('Wins')) {
    return {
      name: 'wins',
      icon: '🏆',
      text: 'text-emerald-400',
      bg: 'bg-emerald-500',
      border: 'border-emerald-500',
      borderColor: 'border-emerald-500/30',
      bgGradient: 'bg-gradient-to-br from-emerald-900/90 to-slate-900',
      activeButton: 'bg-emerald-600 text-white',
      focusRing: 'focus:border-emerald-500 focus:ring-emerald-500/20'
    };
  } else {
    // Score (Default)
    return {
      name: 'score',
      icon: '⭐',
      text: 'text-cyan-400',
      bg: 'bg-cyan-500',
      border: 'border-cyan-500',
      borderColor: 'border-cyan-500/30',
      bgGradient: 'bg-gradient-to-br from-cyan-900/90 to-slate-900',
      activeButton: 'bg-cyan-600 text-white',
      focusRing: 'focus:border-cyan-500 focus:ring-cyan-500/20'
    };
  }
});

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

const loadData = async (days?: number) => {
  if (!props.playerName) return;

  const timeRange = days || selectedTimeRange.value;

  isLoading.value = true;
  error.value = null;

  try {
    console.log(`Loading sliced player data for ${props.playerName} with ${timeRange} days, slice: ${selectedSliceType.value}`);

    // Fetch all data for client-side pagination by using a large page size
    const params = new URLSearchParams({
      sliceType: selectedSliceType.value,
      game: props.game || 'bf1942',
      page: '1',
      pageSize: '1000', // Large page size to get all records for client-side pagination
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

    // Store all results for client-side pagination
    allResults.value = responseData.results || [];

    // Create paginated response structure for compatibility
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

// Client-side pagination helper
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
  // Update slicedData with new page results
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
  // For any other keys, use the regular formatter
  return formatAdditionalKey(key);
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
    Object.entries(additionalData).filter(([key]) =>
      key !== 'team1Label' &&
      key !== 'team2Label' &&
      key !== 'team1Victories' &&
      key !== 'team2Victories'
    )
  );
};

const getTeamWinAdditionalData = (additionalData: Record<string, any>, team1WinRate: number) => {
  if (!isTeamWinSlice()) {
    return additionalData;
  }

  // Create a copy and add team1WinRate for display
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