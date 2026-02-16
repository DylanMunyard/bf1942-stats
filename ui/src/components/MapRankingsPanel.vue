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
  <div class="map-rankings-panel space-y-3">
    <!-- Pinned Player Banner -->
    <div
      v-if="pinnedPlayer && highlightPlayer"
      class="flex items-center gap-3 px-3 sm:px-4 py-3 rounded bg-[var(--bg-card)] border border-[var(--neon-cyan)] shadow-[0_0_20px_rgba(0,255,242,0.2)]"
    >
      <div class="flex-shrink-0">
        <span :class="getRankClass(pinnedPlayer.rank)" class="scale-125">{{ pinnedPlayer.rank }}</span>
      </div>
      <div class="flex-1 min-w-0">
        <div class="text-sm font-bold text-[var(--neon-cyan)] truncate font-mono">{{ pinnedPlayer.playerName }}</div>
        <div class="text-xs text-[var(--text-secondary)] font-mono">Your position on {{ mapName }}</div>
      </div>
      <div class="hidden sm:flex items-center gap-4 text-xs">
        <div class="text-center">
          <div class="font-mono font-bold text-[var(--text-primary)]">{{ pinnedPlayer.totalScore.toLocaleString() }}</div>
          <div class="text-[var(--text-secondary)] text-[10px] uppercase tracking-wider">Score</div>
        </div>
        <div class="text-center">
          <div class="font-mono font-bold text-[var(--neon-green)]">{{ pinnedPlayer.kdRatio.toFixed(2) }}</div>
          <div class="text-[var(--text-secondary)] text-[10px] uppercase tracking-wider">K/D</div>
        </div>
        <div class="text-center">
          <div class="font-mono text-[var(--text-primary)]">{{ pinnedPlayer.totalRounds }}</div>
          <div class="text-[var(--text-secondary)] text-[10px] uppercase tracking-wider">Rounds</div>
        </div>
      </div>
    </div>
    <div v-else-if="isPinnedLoading && highlightPlayer" class="flex items-center gap-2 px-3 sm:px-4 py-3 rounded bg-[var(--bg-card)] border border-[var(--border-color)]">
      <div class="w-4 h-4 border-2 border-[var(--border-color)] border-t-[var(--neon-cyan)] rounded-full animate-spin" />
      <span class="text-xs text-[var(--text-secondary)] font-mono">Finding your rank...</span>
    </div>

    <!-- Header with Search -->
    <div class="flex flex-col gap-3">
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
        <div class="flex items-center gap-2">
          <h3 class="text-sm font-bold text-[var(--neon-cyan)] uppercase tracking-wider font-mono">Full Rankings</h3>
          <span v-if="totalCount > 0" class="text-xs text-[var(--text-secondary)] font-mono">({{ totalCount.toLocaleString() }} players)</span>
          <div v-if="isRefreshing" class="w-3.5 h-3.5 border-2 border-[var(--border-color)] border-t-[var(--neon-cyan)] rounded-full animate-spin" />
        </div>
        
        <!-- Period Selector -->
        <div class="flex items-center gap-0 bg-[var(--bg-panel)] rounded border border-[var(--border-color)] p-0.5 self-start sm:self-auto">
          <button
            v-for="days in [30, 60, 90, 365]"
            :key="days"
            class="px-2.5 py-1 text-[10px] font-mono rounded transition-all font-semibold uppercase tracking-wider"
            :class="selectedDays === days 
              ? 'bg-[var(--neon-cyan)] text-[var(--bg-dark)] shadow-[0_0_10px_rgba(0,255,242,0.4)]' 
              : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] hover:bg-white/5'"
            @click="handleDaysChange(days)"
          >
            {{ days === 365 ? '1Y' : `${days}D` }}
          </button>
        </div>
      </div>

      <div class="relative w-full">
        <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--neon-green)] opacity-80"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
        <input
          v-model="searchQuery"
          type="text"
          placeholder="Search players..."
          class="w-full pl-8 pr-3 py-1.5 text-xs font-mono rounded transition-all"
          @input="handleSearchInput"
        />
      </div>
    </div>

    <!-- Sort Tabs -->
    <div class="flex gap-0 border-b border-[var(--border-color)]">
      <button
        v-for="tab in tabs"
        :key="tab.id"
        :disabled="isRefreshing"
        :class="[
          'px-3 py-2 text-xs font-semibold uppercase tracking-wider font-mono border-b-2 -mb-px transition-all',
          activeTab === tab.id
            ? 'border-[var(--neon-cyan)] text-[var(--neon-cyan)] shadow-[0_0_10px_rgba(0,255,242,0.3)]'
            : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-primary)]'
        ]"
        @click="selectTab(tab.id)"
      >
        {{ tab.label }}
      </button>
    </div>

    <!-- Loading -->
    <div v-if="isLoading && rankings.length === 0" class="flex flex-col items-center justify-center py-12 gap-3">
      <div class="w-6 h-6 border-2 border-[var(--border-color)] border-t-[var(--neon-cyan)] rounded-full animate-spin" />
      <span class="text-xs text-[var(--text-secondary)] font-mono uppercase tracking-wider">Loading rankings...</span>
    </div>

    <!-- Error -->
    <div v-else-if="error && rankings.length === 0" class="text-center py-8 bg-[var(--bg-card)] rounded border border-[var(--neon-red)] shadow-[0_0_20px_rgba(255,49,49,0.2)]">
      <div class="text-sm text-[var(--neon-red)] mb-2 font-mono">{{ error }}</div>
      <button class="text-xs text-[var(--neon-cyan)] hover:text-[var(--neon-cyan)]/80 font-mono uppercase tracking-wider font-semibold" @click="loadRankings">Try again</button>
    </div>

    <!-- Rankings Table -->
    <div v-else-if="rankings.length > 0" :class="{ 'opacity-50 pointer-events-none': isRefreshing }">
      <div class="overflow-x-auto bg-[var(--bg-card)] rounded border border-[var(--border-color)]">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left border-b border-[var(--border-color)]">
              <th class="p-2 sm:p-3 text-xs w-10">#</th>
              <th class="p-2 sm:p-3 text-xs">Player</th>
              <th class="p-2 sm:p-3 text-xs text-right">{{ primaryColumnHeader }}</th>
              <th class="p-2 sm:p-3 text-xs text-right hidden sm:table-cell">K/D</th>
              <th class="p-2 sm:p-3 text-xs text-right hidden sm:table-cell">Rounds</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="entry in rankings"
              :key="entry.playerName"
              :class="[
                'border-b border-[var(--border-color)] transition-colors cursor-pointer',
                isHighlighted(entry.playerName)
                  ? 'bg-[var(--neon-cyan)]/10 border-l-2 border-l-[var(--neon-cyan)]'
                  : ''
              ]"
            >
              <td class="p-2 sm:p-3">
                <span :class="getRankClass(entry.rank)">{{ entry.rank }}</span>
              </td>
              <td class="p-2 sm:p-3 max-w-[140px] truncate">
                <button
                  class="text-[var(--text-primary)] hover:text-[var(--neon-cyan)] transition-colors font-medium text-left"
                  @click="navigateToPlayer(entry.playerName)"
                >
                  {{ entry.playerName }}
                </button>
              </td>
              <td class="p-2 sm:p-3 text-right font-mono text-[var(--neon-cyan)] font-bold">
                {{ formatPrimaryValue(entry) }}
              </td>
              <td class="p-2 sm:p-3 text-right font-mono text-[var(--neon-green)] hidden sm:table-cell">
                {{ entry.kdRatio.toFixed(2) }}
              </td>
              <td class="p-2 sm:p-3 text-right font-mono text-[var(--text-secondary)] hidden sm:table-cell">
                {{ entry.totalRounds }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="totalPages > 1" class="flex items-center justify-center gap-1 pt-3 mt-3 border-t border-[var(--border-color)]">
        <button
          class="px-2.5 py-1 text-xs font-semibold font-mono uppercase bg-[var(--bg-panel)] border border-[var(--border-color)] rounded disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:bg-white/5 hover:border-[var(--neon-cyan)] hover:text-[var(--neon-cyan)]"
          :class="currentPage === 1 || isRefreshing ? 'text-[var(--text-secondary)]' : 'text-[var(--text-primary)]'"
          :disabled="currentPage === 1 || isRefreshing"
          @click="goToPage(currentPage - 1)"
        >
          &larr;
        </button>
        <button
          v-for="pageNum in paginationRange"
          :key="pageNum"
          :class="[
            'px-2.5 py-1 text-xs font-semibold font-mono rounded border transition-all min-w-[1.75rem]',
            pageNum === currentPage
              ? 'bg-[var(--neon-cyan)] text-[var(--bg-dark)] border-[var(--neon-cyan)] shadow-[0_0_10px_rgba(0,255,242,0.4)]'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-primary)] bg-[var(--bg-panel)] border-[var(--border-color)] hover:border-[var(--neon-cyan)]'
          ]"
          :disabled="isRefreshing"
          @click="goToPage(pageNum)"
        >
          {{ pageNum }}
        </button>
        <button
          class="px-2.5 py-1 text-xs font-semibold font-mono uppercase bg-[var(--bg-panel)] border border-[var(--border-color)] rounded disabled:opacity-40 disabled:cursor-not-allowed transition-all hover:bg-white/5 hover:border-[var(--neon-cyan)] hover:text-[var(--neon-cyan)]"
          :class="currentPage === totalPages || isRefreshing ? 'text-[var(--text-secondary)]' : 'text-[var(--text-primary)]'"
          :disabled="currentPage === totalPages || isRefreshing"
          @click="goToPage(currentPage + 1)"
        >
          &rarr;
        </button>
      </div>
    </div>

    <!-- Empty -->
    <div v-else class="text-center py-8 bg-[var(--bg-card)] rounded border border-[var(--border-color)]">
      <div class="text-2xl text-[var(--neon-cyan)] opacity-50 mb-2 font-mono">{ }</div>
      <div class="text-sm text-[var(--text-secondary)] font-mono">No rankings data available</div>
    </div>
  </div>
</template>

<style scoped>
/* Match DataExplorer.vue.css theme */
.map-rankings-panel {
  --neon-cyan: #00fff2;
  --neon-green: #39ff14;
  --neon-pink: #ff00ff;
  --neon-gold: #ffd700;
  --neon-red: #ff3131;
  --bg-dark: #0a0a0f;
  --bg-panel: #0d1117;
  --bg-card: #161b22;
  --border-color: #30363d;
  --text-primary: #e6edf3;
  --text-secondary: #8b949e;
  
  font-family: 'JetBrains Mono', 'Fira Code', 'SF Mono', Consolas, monospace;
}

.map-rankings-panel input {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  color: var(--text-primary);
}

.map-rankings-panel input::placeholder {
  color: var(--text-secondary);
  opacity: 0.5;
}

.map-rankings-panel input:focus {
  outline: none;
  border-color: var(--neon-cyan);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.2);
}

.map-rankings-panel table {
  font-family: 'JetBrains Mono', monospace;
}

.map-rankings-panel th {
  background: var(--bg-card);
  color: var(--neon-cyan);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 700;
  font-size: 0.7rem;
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.3);
}

.map-rankings-panel tbody tr {
  border-color: var(--border-color);
}

.map-rankings-panel tbody tr:hover {
  background: rgba(0, 255, 242, 0.08);
}

/* Rank badge styling */
.map-rankings-panel tbody tr td:first-child span {
  font-family: 'JetBrains Mono', monospace;
  font-weight: 700;
  font-size: 0.8rem;
}

/* Add glow effect to active tab */
.map-rankings-panel button:not(:disabled):hover {
  cursor: pointer;
}

.map-rankings-panel button:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
</style>
