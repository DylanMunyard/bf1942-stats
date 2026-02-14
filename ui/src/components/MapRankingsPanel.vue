<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { fetchMapPlayerRankings, type MapPlayerRanking, type GameType, type MapRankingSortBy } from '../services/dataExplorerService';
import { getRankClass } from '@/utils/statsUtils';

const props = defineProps<{
  mapName: string;
  game?: GameType;
  serverGuid?: string;
  highlightPlayer?: string;
  days?: number;
}>();

const router = useRouter();

// Tab configuration
const tabs = [
  { id: 'score' as const, label: 'Score', title: 'Top by Score' },
  { id: 'kills' as const, label: 'Kills', title: 'Top by Kills' },
  { id: 'wins' as const, label: 'Wins', title: 'Top by Wins' },
  { id: 'kdRatio' as const, label: 'K/D', title: 'Top by K/D Ratio' },
  { id: 'killRate' as const, label: 'Kill Rate', title: 'Top by Kill Rate' },
];

const activeTab = ref<MapRankingSortBy>('score');
const searchQuery = ref('');
const debouncedSearch = ref('');
let searchTimeout: number | null = null;

const pageSize = 15;
const rankings = ref<MapPlayerRanking[]>([]);
const isLoading = ref(false);
const isRefreshing = ref(false);
const error = ref<string | null>(null);
const currentPage = ref(1);
const totalPages = ref(0);
const totalCount = ref(0);

// Pinned player data (fetched separately)
const pinnedPlayer = ref<MapPlayerRanking | null>(null);
const isPinnedLoading = ref(false);

const selectedDays = ref(props.days || 60);

const loadRankings = async () => {
  if (!props.mapName) return;

  if (rankings.value.length === 0) {
    isLoading.value = true;
  } else {
    isRefreshing.value = true;
  }
  error.value = null;

  try {
    const response = await fetchMapPlayerRankings(
      props.mapName,
      props.game || 'bf1942',
      currentPage.value,
      pageSize,
      debouncedSearch.value || undefined,
      props.serverGuid,
      selectedDays.value,
      activeTab.value
    );

    rankings.value = response.rankings;
    totalPages.value = Math.ceil(response.totalCount / pageSize);
    totalCount.value = response.totalCount;
  } catch (err) {
    console.error('Error loading rankings:', err);
    error.value = 'Failed to load rankings';
  } finally {
    isLoading.value = false;
    isRefreshing.value = false;
  }
};

// Fetch the highlighted player's rank separately so it's always visible
const loadPinnedPlayer = async () => {
  if (!props.highlightPlayer || !props.mapName) return;

  isPinnedLoading.value = true;
  try {
    const response = await fetchMapPlayerRankings(
      props.mapName,
      props.game || 'bf1942',
      1,
      1,
      props.highlightPlayer,
      props.serverGuid,
      selectedDays.value,
      activeTab.value
    );
    pinnedPlayer.value = response.rankings.length > 0 ? response.rankings[0] : null;
  } catch {
    pinnedPlayer.value = null;
  } finally {
    isPinnedLoading.value = false;
  }
};

const handleDaysChange = (days: number) => {
  if (days === selectedDays.value || isRefreshing.value) return;
  selectedDays.value = days;
  currentPage.value = 1;
  loadRankings();
  loadPinnedPlayer();
};

const selectTab = (tabId: MapRankingSortBy) => {
  if (tabId === activeTab.value || isRefreshing.value) return;
  activeTab.value = tabId;
  currentPage.value = 1;
  loadRankings();
  loadPinnedPlayer();
};

const goToPage = (page: number) => {
  if (page < 1 || page > totalPages.value || isRefreshing.value) return;
  currentPage.value = page;
  loadRankings();
};

const handleSearchInput = () => {
  if (searchTimeout) clearTimeout(searchTimeout);
  searchTimeout = setTimeout(() => {
    debouncedSearch.value = searchQuery.value;
    currentPage.value = 1;
    loadRankings();
  }, 300) as unknown as number;
};

const navigateToPlayer = (playerName: string) => {
  router.push({ name: 'player-details', params: { playerName } });
};

const primaryColumnHeader = computed(() => {
  switch (activeTab.value) {
    case 'score': return 'Score';
    case 'kills': return 'Kills';
    case 'wins': return 'Wins';
    case 'kdRatio': return 'K/D';
    case 'killRate': return 'Kills/Min';
    default: return 'Score';
  }
});

const formatPrimaryValue = (entry: MapPlayerRanking): string => {
  switch (activeTab.value) {
    case 'score': return entry.totalScore.toLocaleString();
    case 'kills': return entry.totalKills.toLocaleString();
    case 'wins': return (entry.totalWins || 0).toLocaleString();
    case 'kdRatio': return entry.kdRatio.toFixed(2);
    case 'killRate': return entry.killsPerMinute.toFixed(3);
    default: return entry.totalScore.toLocaleString();
  }
};

const isHighlighted = (playerName: string): boolean => {
  return !!props.highlightPlayer && playerName.toLowerCase() === props.highlightPlayer.toLowerCase();
};

const paginationRange = computed(() => {
  const range: number[] = [];
  const maxVisible = 5;
  let start = Math.max(1, currentPage.value - Math.floor(maxVisible / 2));
  const end = Math.min(totalPages.value, start + maxVisible - 1);
  if (end === totalPages.value) start = Math.max(1, end - maxVisible + 1);
  for (let i = start; i <= end; i++) range.push(i);
  return range;
});

onMounted(() => {
  loadRankings();
  loadPinnedPlayer();
});

watch(() => props.mapName, () => {
  currentPage.value = 1;
  searchQuery.value = '';
  debouncedSearch.value = '';
  rankings.value = [];
  pinnedPlayer.value = null;
  loadRankings();
  loadPinnedPlayer();
});

watch(() => props.serverGuid, () => {
  currentPage.value = 1;
  rankings.value = [];
  pinnedPlayer.value = null;
  loadRankings();
  loadPinnedPlayer();
});

watch(() => props.days, (newDays) => {
  if (newDays) {
    selectedDays.value = newDays;
    currentPage.value = 1;
    loadRankings();
    loadPinnedPlayer();
  }
});
</script>

<template>
  <div class="space-y-3">
    <!-- Pinned Player Banner -->
    <div
      v-if="pinnedPlayer && highlightPlayer"
      class="flex items-center gap-3 px-4 py-3 rounded-lg bg-cyan-500/10 border border-cyan-500/30"
    >
      <div class="flex-shrink-0">
        <span :class="getRankClass(pinnedPlayer.rank)" class="scale-125">{{ pinnedPlayer.rank }}</span>
      </div>
      <div class="flex-1 min-w-0">
        <div class="text-sm font-semibold text-cyan-300 truncate">{{ pinnedPlayer.playerName }}</div>
        <div class="text-xs text-neutral-400">Your position on {{ mapName }}</div>
      </div>
      <div class="hidden sm:flex items-center gap-4 text-xs">
        <div class="text-center">
          <div class="font-mono font-semibold text-neutral-200">{{ pinnedPlayer.totalScore.toLocaleString() }}</div>
          <div class="text-neutral-500">Score</div>
        </div>
        <div class="text-center">
          <div class="font-mono font-semibold text-green-400">{{ pinnedPlayer.kdRatio.toFixed(2) }}</div>
          <div class="text-neutral-500">K/D</div>
        </div>
        <div class="text-center">
          <div class="font-mono text-neutral-300">{{ pinnedPlayer.totalRounds }}</div>
          <div class="text-neutral-500">Rounds</div>
        </div>
      </div>
    </div>
    <div v-else-if="isPinnedLoading && highlightPlayer" class="flex items-center gap-2 px-4 py-3 rounded-lg bg-neutral-800/50 border border-neutral-700/50">
      <div class="w-4 h-4 border-2 border-neutral-600 border-t-cyan-400 rounded-full animate-spin" />
      <span class="text-xs text-neutral-400">Finding your rank...</span>
    </div>

    <!-- Header with Search -->
    <div class="flex flex-col gap-3">
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
        <div class="flex items-center gap-2">
          <h3 class="text-sm font-semibold text-neutral-200">Full Rankings</h3>
          <span v-if="totalCount > 0" class="text-xs text-neutral-500">({{ totalCount.toLocaleString() }} players)</span>
          <div v-if="isRefreshing" class="w-3.5 h-3.5 border-2 border-neutral-600 border-t-cyan-400 rounded-full animate-spin" />
        </div>
        
        <!-- Period Selector -->
        <div class="flex items-center gap-2 bg-neutral-800/50 rounded p-0.5 border border-neutral-700/50 self-start sm:self-auto">
          <button
            v-for="days in [30, 60, 90, 365]"
            :key="days"
            class="px-2 py-0.5 text-[10px] font-mono rounded transition-colors"
            :class="selectedDays === days ? 'bg-cyan-500/20 text-cyan-400' : 'text-neutral-400 hover:text-neutral-200 hover:bg-white/5'"
            @click="handleDaysChange(days)"
          >
            {{ days === 365 ? '1Y' : `${days}D` }}
          </button>
        </div>
      </div>

      <div class="relative w-full">
        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-neutral-500"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Search players..."
          class="w-full pl-8 pr-3 py-1.5 text-xs bg-neutral-800 border border-neutral-700 rounded-lg text-neutral-200 placeholder:text-neutral-500 focus:outline-none focus:border-cyan-500/50 focus:ring-1 focus:ring-cyan-500/20"
          @input="handleSearchInput"
        />
      </div>
    </div>

    <!-- Sort Tabs -->
    <div class="flex gap-0 border-b border-neutral-700/50">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        :disabled="isRefreshing"
        :class="[
          'px-3 py-2 text-xs font-medium border-b-2 -mb-px transition-colors',
          activeTab === tab.id
            ? 'border-cyan-400 text-cyan-400'
            : 'border-transparent text-neutral-500 hover:text-neutral-300'
        ]"
        @click="selectTab(tab.id)"
      >
        {{ tab.label }}
      </button>
    </div>

    <!-- Loading -->
    <div v-if="isLoading && rankings.length === 0" class="flex items-center justify-center py-12">
      <div class="w-6 h-6 border-2 border-neutral-600 border-t-cyan-400 rounded-full animate-spin" />
    </div>

    <!-- Error -->
    <div v-else-if="error && rankings.length === 0" class="text-center py-8">
      <div class="text-sm text-red-400 mb-2">{{ error }}</div>
      <button class="text-xs text-cyan-400 hover:text-cyan-300" @click="loadRankings">Try again</button>
    </div>

    <!-- Rankings Table -->
    <div v-else-if="rankings.length > 0" :class="{ 'opacity-50 pointer-events-none': isRefreshing }">
      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-neutral-500 text-left border-b border-neutral-700/50">
              <th class="p-2 text-xs font-medium w-10">#</th>
              <th class="p-2 text-xs font-medium">Player</th>
              <th class="p-2 text-xs font-medium text-right">{{ primaryColumnHeader }}</th>
              <th class="p-2 text-xs font-medium text-right hidden sm:table-cell">K/D</th>
              <th class="p-2 text-xs font-medium text-right hidden sm:table-cell">Rounds</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="entry in rankings"
              :key="entry.playerName"
              :class="[
                'border-b border-neutral-800/50 transition-colors',
                isHighlighted(entry.playerName)
                  ? 'bg-cyan-500/10 border-l-2 border-l-cyan-400'
                  : 'hover:bg-neutral-800/40'
              ]"
            >
              <td class="p-2">
                <span :class="getRankClass(entry.rank)">{{ entry.rank }}</span>
              </td>
              <td class="p-2 max-w-[140px] truncate">
                <button
                  class="text-neutral-200 hover:text-cyan-400 transition-colors font-medium text-left"
                  @click="navigateToPlayer(entry.playerName)"
                >
                  {{ entry.playerName }}
                </button>
              </td>
              <td class="p-2 text-right font-mono text-cyan-400 font-medium">
                {{ formatPrimaryValue(entry) }}
              </td>
              <td class="p-2 text-right font-mono text-green-400 hidden sm:table-cell">
                {{ entry.kdRatio.toFixed(2) }}
              </td>
              <td class="p-2 text-right font-mono text-neutral-400 hidden sm:table-cell">
                {{ entry.totalRounds }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="flex items-center justify-center gap-1 pt-3 mt-3 border-t border-neutral-800/50">
        <button
          class="px-2 py-1 text-xs font-medium text-neutral-400 hover:text-neutral-200 bg-neutral-800 border border-neutral-700 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          :disabled="currentPage === 1 || isRefreshing"
          @click="goToPage(currentPage - 1)"
        >
          Prev
        </button>
        <button
          v-for="pageNum in paginationRange"
          :key="pageNum"
          :class="[
            'px-2 py-1 text-xs font-medium rounded border transition-colors min-w-[1.5rem]',
            pageNum === currentPage
              ? 'bg-cyan-500/20 text-cyan-400 border-cyan-500/30'
              : 'text-neutral-400 hover:text-neutral-200 bg-neutral-800 border-neutral-700'
          ]"
          :disabled="isRefreshing"
          @click="goToPage(pageNum)"
        >
          {{ pageNum }}
        </button>
        <button
          class="px-2 py-1 text-xs font-medium text-neutral-400 hover:text-neutral-200 bg-neutral-800 border border-neutral-700 rounded disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          :disabled="currentPage === totalPages || isRefreshing"
          @click="goToPage(currentPage + 1)"
        >
          Next
        </button>
      </div>
    </div>

    <!-- Empty -->
    <div v-else class="text-center py-8 text-sm text-neutral-400">
      No rankings data available
    </div>
  </div>
</template>
