<template>
  <div class="detail-content p-3 sm:p-6">
    <!-- Loading State -->
    <div v-if="isLoading" class="detail-loading">
      <div class="detail-skeleton detail-skeleton--title"></div>
      <div class="detail-skeleton detail-skeleton--subtitle"></div>
      <div class="detail-skeleton detail-skeleton--block"></div>
      <div class="detail-skeleton detail-skeleton--block-lg"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="detail-error">
      <div class="detail-error-text">{{ error }}</div>
      <div class="flex items-center justify-center gap-3">
        <button @click="loadData" class="detail-retry">
          Try again
        </button>
        <button @click="emit('close')" class="detail-retry">
          Close
        </button>
      </div>
    </div>

    <!-- Content -->
    <div v-else class="detail-body">
      <!-- Header -->
      <div class="detail-header">
        <div class="detail-header-row">
          <button
            @click="emit('close')"
            class="flex items-center justify-center w-8 h-8 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-slate-200 transition-colors mr-3"
            title="Close"
          >
            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
            </svg>
          </button>
          <h2 class="detail-title">{{ playerName }} on {{ mapName }}</h2>
        </div>
        <div class="detail-meta ml-11">
          Your performance statistics on this map
        </div>
      </div>

      <!-- Player Stats Summary -->
      <div v-if="playerStats" class="detail-section">
        <h3 class="detail-section-title">{{ playerName.toUpperCase() }}'S PERFORMANCE</h3>
        <div class="grid grid-cols-2 sm:grid-cols-4 gap-3 mb-4">
          <div class="detail-stat-card">
            <div class="detail-stat-value text-neon-cyan">{{ playerStats?.totalScore.toLocaleString() || 0 }}</div>
            <div class="detail-stat-label">Total Score</div>
          </div>
          <div class="detail-stat-card">
            <div class="detail-stat-value text-neon-green">{{ playerStats?.totalKills.toLocaleString() || 0 }}</div>
            <div class="detail-stat-label">Kills</div>
          </div>
          <div class="detail-stat-card">
            <div class="detail-stat-value text-neon-red">{{ playerStats?.totalDeaths.toLocaleString() || 0 }}</div>
            <div class="detail-stat-label">Deaths</div>
          </div>
          <div class="detail-stat-card">
            <div class="detail-stat-value text-neon-gold">{{ kdRatio }}</div>
            <div class="detail-stat-label">K/D Ratio</div>
          </div>
        </div>
      </div>

      <!-- Rankings Tabs -->
      <div class="detail-section">
        <h3 class="detail-section-title">PLAYER RANKINGS</h3>
        
        <!-- Tab Navigation -->
        <div class="detail-tabs mb-4">
          <button
            v-for="tab in rankingTabs"
            :key="tab.id"
            class="detail-tab"
            :class="{ 'detail-tab--active': activeRankingTab === tab.id }"
            @click="activeRankingTab = tab.id; loadRankings()"
          >
            {{ tab.label }}
          </button>
        </div>

        <!-- Rankings Content -->
        <div class="detail-card">
          <div v-if="isRankingsLoading" class="text-center py-8">
            <div class="detail-spinner mx-auto"></div>
          </div>
          
          <div v-else-if="rankings && rankings.length > 0">


            <!-- Filters row -->
            <div class="flex flex-col sm:flex-row gap-3 mb-4">
              <!-- Server Filter (if player plays on multiple servers) -->
              <div v-if="serverOptions.length > 1" class="flex-1">
                <select
                  v-model="selectedServerGuid"
                  @change="loadRankings"
                  class="detail-select w-full"
                >
                  <option value="">All Servers</option>
                  <option v-for="server in serverOptions" :key="server.guid" :value="server.guid">
                    {{ server.name }}
                  </option>
                </select>
              </div>

              <!-- Min Rounds Filter -->
              <div class="flex items-center gap-0 bg-[var(--bg-panel)] rounded border border-[var(--border-color)] p-0.5 self-start">
                <span class="px-1.5 text-[10px] font-mono text-neutral-500 uppercase tracking-wider">Rnds</span>
                <button
                  v-for="rounds in minRoundsOptions"
                  :key="rounds"
                  class="px-2 py-1 text-[10px] font-mono rounded transition-all font-semibold"
                  :class="selectedMinRounds === rounds
                    ? 'bg-[var(--neon-cyan)] text-[var(--bg-dark)]'
                    : 'text-neutral-500 hover:text-neutral-300 hover:bg-white/5'"
                  @click="selectedMinRounds = rounds; currentPage = 1; loadRankings()"
                >
                  {{ rounds }}+
                </button>
              </div>
            </div>

            <!-- Rankings Table -->
            <div class="overflow-x-auto">
              <table class="detail-table">
                <thead>
                  <tr>
                    <th class="text-center w-12">#</th>
                    <th>Player</th>
                    <th class="text-right">{{ getMetricLabel() }}</th>
                    <th class="text-right">Rounds</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(player, index) in rankings"
                    :key="player.playerName"
                    :data-player="player.playerName"
                    :class="{
                      'player-row-highlight': player.playerName === playerName
                    }"
                  >
                    <td class="text-center">
                      <span class="rank-badge" :class="getRankBadgeClass(player.rank)">
                        {{ player.rank }}
                      </span>
                    </td>
                    <td>
                      <router-link
                        :to="`/players/${encodeURIComponent(player.playerName)}`"
                        class="detail-link font-mono"
                      >
                        {{ player.playerName }}
                        <span v-if="player.playerName === playerName" class="text-neon-cyan ml-2">(YOU)</span>
                      </router-link>
                    </td>
                    <td class="text-right font-mono">
                      {{ formatMetricValue(player) }}
                    </td>
                    <td class="text-right font-mono text-neutral-400">
                      {{ player.totalRounds }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- Pagination -->
            <div v-if="totalPages > 1" class="detail-pagination">
              <button
                @click="currentPage--; loadRankings()"
                :disabled="currentPage === 1"
                class="detail-pagination-btn"
              >
                ←
              </button>
              <span class="detail-pagination-info">
                Page {{ currentPage }} of {{ totalPages }}
              </span>
              <button
                @click="currentPage++; loadRankings()"
                :disabled="currentPage === totalPages"
                class="detail-pagination-btn"
              >
                →
              </button>
            </div>
          </div>
          
          <div v-else class="text-center py-8 text-neutral-400">
            No ranking data available
          </div>
        </div>
      </div>

      <!-- Server Breakdown -->
      <div v-if="serverStats && serverStats.length > 0" class="detail-section">
        <h3 class="detail-section-title">YOUR PERFORMANCE BY SERVER</h3>
        <div class="detail-card">
          <div class="space-y-2">
            <div
              v-for="server in serverStats"
              :key="server.serverGuid"
              class="flex items-center justify-between p-3 rounded hover:bg-white/5 transition-colors cursor-pointer"
              @click="emit('navigateToServer', server.serverGuid)"
            >
              <div>
                <div class="font-medium">{{ server.serverName }}</div>
                <div class="text-xs text-neutral-400 font-mono">
                  {{ server.rounds }} rounds • {{ formatPlayTime(server.playTime) }}
                </div>
              </div>
              <div class="text-right">
                <div class="font-mono">{{ server.score.toLocaleString() }} pts</div>
                <div class="text-xs text-neutral-400">K/D {{ (server.kills / Math.max(1, server.deaths)).toFixed(2) }}</div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { fetchMapPlayerRankings, type MapPlayerRanking, type MapRankingSortBy } from '../../services/dataExplorerService';

const props = defineProps<{
  mapName: string;
  playerName: string;
  game?: string;
}>();

const emit = defineEmits<{
  close: [];
  navigateToServer: [serverGuid: string];
}>();

const router = useRouter();

// Types
interface PlayerMapStats {
  totalScore: number;
  totalKills: number;
  totalDeaths: number;
  totalRounds: number;
  playTimeMinutes: number;
}

interface ServerStats {
  serverGuid: string;
  serverName: string;
  score: number;
  kills: number;
  deaths: number;
  rounds: number;
  playTime: number;
}

// State
const isLoading = ref(true);
const error = ref<string | null>(null);
const playerStats = ref<PlayerMapStats | null>(null);
const serverStats = ref<ServerStats[]>([]);
const rankings = ref<MapPlayerRanking[]>([]);
const playerRanking = ref<MapPlayerRanking | null>(null);
const totalRankedPlayers = ref(0);
const isRankingsLoading = ref(false);

// Ranking tabs
const rankingTabs = [
  { id: 'score' as const, label: 'By Score' },
  { id: 'kills' as const, label: 'By Kills' },
  { id: 'kdRatio' as const, label: 'By K/D' },
  { id: 'killRate' as const, label: 'By Kill Rate' }
];

const activeRankingTab = ref<MapRankingSortBy>('score');
const selectedServerGuid = ref<string>('');
const currentPage = ref(1);
const pageSize = 20;
const totalPages = ref(1);
const selectedMinRounds = ref(3);
const minRoundsOptions = [3, 5, 10, 20, 50];

// Computed
const kdRatio = computed(() => {
  if (!playerStats.value) return '0.00';
  return (playerStats.value.totalKills / Math.max(1, playerStats.value.totalDeaths)).toFixed(2);
});

const serverOptions = computed(() => {
  return serverStats.value.map(s => ({
    guid: s.serverGuid,
    name: s.serverName
  }));
});

// Methods
const loadData = async () => {
  isLoading.value = true;
  error.value = null;

  try {
    // Load all map stats for this player (use days=365 so older maps are included)
    const response = await fetch(
      `/stats/players/${encodeURIComponent(props.playerName)}/map-stats?game=${props.game || 'bf1942'}&days=365`
    );

    if (!response.ok) {
      throw new Error('Failed to load player map statistics');
    }

    const mapsList = await response.json();

    // Find the specific map in the list
    const mapData = mapsList.find((m: any) => m.mapName.toLowerCase() === props.mapName.toLowerCase());

    if (mapData) {
      playerStats.value = {
        totalScore: mapData.totalScore,
        totalKills: mapData.totalKills,
        totalDeaths: mapData.totalDeaths,
        totalRounds: mapData.sessionsPlayed,
        playTimeMinutes: mapData.totalPlayTimeMinutes
      };
    }

    // Load rankings even if player stats weren't found
    await loadRankings();
  } catch (err: any) {
    console.error('Error loading player map data:', err);
    error.value = err.message || 'Failed to load data';
  } finally {
    isLoading.value = false;
  }
};

const loadRankings = async () => {
  isRankingsLoading.value = true;

  try {
    // First, fetch to find player's position
    console.log('Searching for player ranking:', {
      mapName: props.mapName,
      playerName: props.playerName,
      serverGuid: selectedServerGuid.value || 'all servers',
      sortBy: activeRankingTab.value
    });
    
    const playerSearchResponse = await fetchMapPlayerRankings(
      props.mapName,
      props.game as any || 'bf1942',
      1,
      1,
      props.playerName,
      selectedServerGuid.value || undefined,
      60,
      activeRankingTab.value,
      selectedMinRounds.value
    );

    if (playerSearchResponse.rankings.length > 0) {
      playerRanking.value = playerSearchResponse.rankings[0];
      totalRankedPlayers.value = playerSearchResponse.totalCount;
      console.log('Player ranking found:', playerRanking.value);
    } else {
      playerRanking.value = null;
      console.log('No ranking found for player:', props.playerName, 'on map:', props.mapName);
    }

    // Then fetch the current page
    const response = await fetchMapPlayerRankings(
      props.mapName,
      props.game as any || 'bf1942',
      currentPage.value,
      pageSize,
      undefined,
      selectedServerGuid.value || undefined,
      60,
      activeRankingTab.value,
      selectedMinRounds.value
    );

    rankings.value = response.rankings;
    totalPages.value = Math.ceil(response.totalCount / pageSize);
  } catch (err) {
    console.error('Error loading rankings:', err);
  } finally {
    isRankingsLoading.value = false;
  }
};

const formatMetricValue = (player: MapPlayerRanking): string => {
  switch (activeRankingTab.value) {
    case 'score':
      return player.totalScore.toLocaleString();
    case 'kills':
      return player.totalKills.toLocaleString();
    case 'kdRatio':
      return player.kdRatio.toFixed(2);
    case 'killRate':
      return player.killsPerMinute.toFixed(2);
    default:
      return '0';
  }
};

const getMetricLabel = (): string => {
  switch (activeRankingTab.value) {
    case 'score':
      return 'Score';
    case 'kills':
      return 'Kills';
    case 'kdRatio':
      return 'K/D Ratio';
    case 'killRate':
      return 'Kills/Min';
    default:
      return 'Value';
  }
};

const getRankBadgeClass = (rank: number): string => {
  if (rank === 1) return 'rank-gold';
  if (rank === 2) return 'rank-silver';
  if (rank === 3) return 'rank-bronze';
  if (rank <= 10) return 'rank-top10';
  return '';
};



const formatPlayTime = (minutes: number): string => {
  const hours = Math.floor(minutes / 60);
  if (hours > 0) {
    return `${hours}h ${Math.floor(minutes % 60)}m`;
  }
  return `${Math.floor(minutes)}m`;
};

// Lifecycle
onMounted(() => {
  loadData();
});

// Watchers
watch([activeRankingTab, selectedServerGuid], () => {
  currentPage.value = 1;
});
</script>

<style scoped>
/* Reuse styles from MapDetailPanel */
.detail-content {
  background: var(--bg-panel);
  color: var(--text-primary);
  font-family: 'JetBrains Mono', monospace;
  min-height: 100%;
}

.detail-header {
  margin-bottom: 1.5rem;
}

.detail-header-row {
  display: flex;
  align-items: center;
  margin-bottom: 0.5rem;
}

.detail-title {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--neon-cyan);
  text-shadow: 0 0 10px rgba(245, 158, 11, 0.3);
  letter-spacing: 0.05em;
}

.detail-meta {
  font-size: 0.75rem;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.detail-section {
  margin-bottom: 2rem;
}

.detail-section-title {
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: var(--neon-cyan);
  margin-bottom: 0.75rem;
  text-transform: uppercase;
  text-shadow: 0 0 10px rgba(245, 158, 11, 0.3);
}

.detail-stat-card {
  text-align: center;
  padding: 1rem;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  transition: all 0.3s ease;
}

.detail-stat-card:hover {
  border-color: rgba(245, 158, 11, 0.3);
  box-shadow: 0 0 20px rgba(245, 158, 11, 0.1);
}

.detail-stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.detail-stat-label {
  font-size: 0.625rem;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.detail-tabs {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid var(--border-color);
}

.detail-tab {
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

.detail-tab:hover {
  color: var(--text-primary);
}

.detail-tab--active {
  color: var(--neon-cyan);
  border-bottom-color: var(--neon-cyan);
  text-shadow: 0 0 10px rgba(245, 158, 11, 0.5);
}

.detail-card {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 1rem;
  transition: all 0.3s ease;
}

.detail-card:hover {
  border-color: rgba(245, 158, 11, 0.2);
  box-shadow: 0 0 30px rgba(245, 158, 11, 0.05);
}

.detail-table {
  width: 100%;
  font-size: 0.8rem;
  border-collapse: collapse;
}

.detail-table th {
  text-align: left;
  padding: 0.5rem;
  background: var(--bg-panel);
  color: var(--neon-cyan);
  font-weight: 600;
  letter-spacing: 0.06em;
  border-bottom: 1px solid var(--border-color);
  font-size: 0.7rem;
  text-transform: uppercase;
}

.detail-table td {
  padding: 0.5rem;
  border-bottom: 1px solid var(--border-color);
}

.detail-table tr:hover td {
  background: rgba(245, 158, 11, 0.05);
}

.detail-link {
  color: var(--text-primary);
  text-decoration: none;
  transition: color 0.2s ease;
}

.detail-link:hover {
  color: var(--neon-cyan);
  text-shadow: 0 0 5px rgba(245, 158, 11, 0.5);
}

.detail-select {
  width: 100%;
  padding: 0.5rem;
  font-size: 0.75rem;
  font-family: 'JetBrains Mono', monospace;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-primary);
  cursor: pointer;
}

.detail-select:focus {
  outline: none;
  border-color: var(--neon-cyan);
  box-shadow: 0 0 15px rgba(245, 158, 11, 0.2);
}

.detail-pagination {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 1rem;
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--border-color);
}

.detail-pagination-btn {
  padding: 0.25rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--bg-panel);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.detail-pagination-btn:hover:not(:disabled) {
  color: var(--neon-cyan);
  border-color: var(--neon-cyan);
  background: rgba(245, 158, 11, 0.1);
}

.detail-pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.detail-pagination-info {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.rank-badge {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  font-size: 0.75rem;
  font-weight: 700;
  border-radius: 4px;
  min-width: 2rem;
  text-align: center;
}

.rank-gold {
  background: var(--neon-gold);
  color: var(--bg-dark);
  box-shadow: 0 0 10px rgba(251, 191, 36, 0.4);
}

.rank-silver {
  background: #c0c0c0;
  color: var(--bg-dark);
}

.rank-bronze {
  background: #cd7f32;
  color: var(--bg-dark);
}

.rank-top10 {
  background: var(--neon-cyan);
  color: var(--bg-dark);
}



.player-row-highlight td {
  background: rgba(245, 158, 11, 0.08) !important;
  position: relative;
}

.player-row-highlight td:first-child::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 3px;
  background: var(--neon-cyan);
  box-shadow: 0 0 10px rgba(245, 158, 11, 0.5);
}

.highlight-animation {
  animation: highlight-pulse 2s ease-in-out;
}

@keyframes highlight-pulse {
  0% { 
    background-color: rgba(245, 158, 11, 0.08);
  }
  50% { 
    background-color: rgba(245, 158, 11, 0.2);
    box-shadow: 0 0 30px rgba(245, 158, 11, 0.3);
  }
  100% { 
    background-color: rgba(245, 158, 11, 0.08);
  }
}

/* Loading states */
.detail-loading {
  padding: 2rem;
}

.detail-skeleton {
  background: linear-gradient(
    90deg,
    var(--bg-card) 0%,
    var(--border-color) 50%,
    var(--bg-card) 100%
  );
  background-size: 200% 100%;
  animation: skeleton-pulse 1.5s ease-in-out infinite;
  border-radius: 4px;
  margin-bottom: 1rem;
}

.detail-skeleton--title { height: 2rem; width: 60%; }
.detail-skeleton--subtitle { height: 1rem; width: 40%; }
.detail-skeleton--block { height: 8rem; }
.detail-skeleton--block-lg { height: 12rem; }

@keyframes skeleton-pulse {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

.detail-spinner {
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

/* Error state */
.detail-error {
  text-align: center;
  padding: 3rem;
}

.detail-error-text {
  color: var(--neon-red);
  margin-bottom: 1rem;
}

.detail-retry {
  padding: 0.5rem 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  background: transparent;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
  text-transform: uppercase;
}

.detail-retry:hover {
  color: var(--text-primary);
  border-color: var(--neon-cyan);
  background: rgba(245, 158, 11, 0.1);
}

/* Neon color utilities */
.text-neon-cyan { color: var(--neon-cyan); text-shadow: 0 0 10px rgba(245, 158, 11, 0.5); }
.text-neon-green { color: var(--neon-green); text-shadow: 0 0 10px rgba(52, 211, 153, 0.5); }
.text-neon-red { color: var(--neon-red); text-shadow: 0 0 10px rgba(255, 0, 0, 0.5); }
.text-neon-gold { color: var(--neon-gold); text-shadow: 0 0 10px rgba(251, 191, 36, 0.5); }
</style>