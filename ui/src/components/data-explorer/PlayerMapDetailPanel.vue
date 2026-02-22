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
      <button @click="loadData" class="detail-retry">
        Try again
      </button>
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
            <!-- Player's Position - Always Visible -->
            <div class="player-position-sticky mb-4">
              <!-- Show player ranking if found -->
              <div v-if="playerRanking" class="player-position-card">
                <div class="player-position-header">
                  <span class="text-xs font-mono text-neutral-400 uppercase tracking-wider">Your Position</span>
                </div>
                <div class="player-position-content">
                  <div class="flex items-center gap-4">
                    <div class="rank-display">
                      <div class="rank-badge-large" :class="getRankBadgeClass(playerRanking.rank)">
                        #{{ playerRanking.rank }}
                      </div>
                      <div class="rank-context">
                        <span class="text-neon-cyan font-bold">of {{ totalRankedPlayers }}</span>
                        <span class="text-neutral-400">players</span>
                      </div>
                    </div>
                    <div class="flex-1">
                      <div class="flex items-center justify-between mb-2">
                        <div class="font-mono text-lg font-bold text-neon-cyan">{{ playerName }}</div>
                        <div class="percentile-badge" :class="getPercentileBadgeClass(playerRanking.rank, totalRankedPlayers)">
                          TOP {{ ((1 - (playerRanking.rank - 1) / totalRankedPlayers) * 100).toFixed(1) }}%
                        </div>
                      </div>
                      <div class="flex items-center justify-between">
                        <div class="text-sm text-neutral-400">{{ getMetricLabel() }}</div>
                        <div class="font-mono text-xl font-bold">{{ formatMetricValue(playerRanking) }}</div>
                      </div>
                    </div>
                  </div>
                </div>
                
                <!-- Navigation buttons to jump to player's position -->
                <div class="flex items-center justify-center gap-2 mt-3">
                <button
                  v-if="!isPlayerVisible"
                  @click="jumpToPlayer"
                  class="jump-to-player-btn"
                >
                  <svg class="w-4 h-4 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/>
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"/>
                  </svg>
                  Jump to Your Position
                </button>
                <div v-else class="text-xs text-neutral-500 font-mono">
                  <svg class="w-4 h-4 inline mr-1 text-neon-green" fill="currentColor" viewBox="0 0 20 20">
                    <path fill-rule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clip-rule="evenodd"/>
                  </svg>
                  You're visible below
                </div>
                </div>
              </div>
              
              <!-- No ranking case -->
              <div v-else class="player-position-card">
                <div class="player-position-header">
                  <span class="text-xs font-mono text-neutral-400 uppercase tracking-wider">Your Position</span>
                </div>
                <div class="player-position-content text-center py-4">
                  <div class="text-neutral-500 mb-2">
                    <svg class="w-12 h-12 mx-auto opacity-50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"/>
                    </svg>
                  </div>
                  <div class="font-mono text-lg font-bold text-neon-cyan mb-1">{{ playerName }}</div>
                  <div class="text-sm text-neutral-400">Not yet ranked on this map</div>
                  <div class="text-xs text-neutral-500 mt-2">Play more rounds to establish a ranking</div>
                </div>
              </div>
            </div>

            <!-- Server Filter (if player plays on multiple servers) -->
            <div v-if="serverOptions.length > 1" class="mb-4">
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

            <!-- Rankings Table -->
            <div class="rankings-table-container">
              <div v-if="showContextPlayers && contextPlayers.length > 0" class="context-players-section">
                <div class="context-header">
                  <span class="text-xs font-mono text-neutral-500">PLAYERS AROUND YOU</span>
                </div>
                <table class="detail-table context-table">
                  <tbody>
                    <tr
                      v-for="player in contextPlayers"
                      :key="`context-${player.playerName}`"
                      :class="{
                        'player-row-highlight': player.playerName === playerName
                      }"
                    >
                      <td class="text-center w-12">
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
                <div class="context-separator">
                  <span class="text-xs text-neutral-500">FULL RANKINGS</span>
                </div>
              </div>
              
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
const isPlayerVisible = ref(false);
const playerPageNumber = ref<number | null>(null);
const contextPlayers = ref<MapPlayerRanking[]>([]);
const showContextPlayers = ref(false);

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
    // Load player's stats for this map
    const response = await fetch(
      `/stats/data-explorer/players/${encodeURIComponent(props.playerName)}/map-stats/${encodeURIComponent(props.mapName)}?game=${props.game || 'bf1942'}`
    );

    if (!response.ok) {
      throw new Error('Failed to load player map statistics');
    }

    const data = await response.json();
    console.log('Player map stats data:', data);
    playerStats.value = data.aggregatedStats;
    serverStats.value = data.serverBreakdown || [];

    // If player only plays on specific servers, default to the first one
    if (serverStats.value.length > 0 && !selectedServerGuid.value) {
      // Don't auto-select a server, let them see global rankings
      console.log('Player plays on servers:', serverStats.value.map(s => s.serverName));
    }

    // Load initial rankings
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
      activeRankingTab.value
    );

    if (playerSearchResponse.rankings.length > 0) {
      playerRanking.value = playerSearchResponse.rankings[0];
      totalRankedPlayers.value = playerSearchResponse.totalCount;
      
      // Calculate which page the player is on
      playerPageNumber.value = Math.ceil(playerRanking.value.rank / pageSize);
      console.log('Player ranking found:', playerRanking.value);
    } else {
      playerRanking.value = null;
      playerPageNumber.value = null;
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
      activeRankingTab.value
    );

    rankings.value = response.rankings;
    totalPages.value = Math.ceil(response.totalCount / pageSize);
    
    // Check if player is visible on current page
    isPlayerVisible.value = rankings.value.some(r => r.playerName === props.playerName);
    
    // Load context players if player is not on current page
    if (!isPlayerVisible.value && playerRanking.value) {
      await loadContextPlayers();
      showContextPlayers.value = true;
    } else {
      showContextPlayers.value = false;
      contextPlayers.value = [];
    }
  } catch (err) {
    console.error('Error loading rankings:', err);
  } finally {
    isRankingsLoading.value = false;
  }
};

const loadContextPlayers = async () => {
  if (!playerRanking.value) return;
  
  try {
    // Calculate the range of players to fetch (2 above and 2 below)
    const contextRange = 2;
    const startRank = Math.max(1, playerRanking.value.rank - contextRange);
    const endRank = Math.min(totalRankedPlayers.value, playerRanking.value.rank + contextRange);
    
    // Fetch the page that contains these ranks
    const contextPage = Math.ceil(startRank / pageSize);
    const contextResponse = await fetchMapPlayerRankings(
      props.mapName,
      props.game as any || 'bf1942',
      contextPage,
      pageSize,
      undefined,
      selectedServerGuid.value || undefined,
      60,
      activeRankingTab.value
    );
    
    // Filter to only show players around the current player
    contextPlayers.value = contextResponse.rankings.filter(
      r => r.rank >= startRank && r.rank <= endRank
    );
  } catch (err) {
    console.error('Error loading context players:', err);
  }
};

const jumpToPlayer = async () => {
  if (playerPageNumber.value && playerPageNumber.value !== currentPage.value) {
    currentPage.value = playerPageNumber.value;
    await loadRankings();
    
    // Scroll to the player's row after page loads
    setTimeout(() => {
      const playerRow = document.querySelector(`[data-player="${props.playerName}"]`);
      if (playerRow) {
        playerRow.scrollIntoView({ behavior: 'smooth', block: 'center' });
        // Add a highlight animation
        playerRow.classList.add('highlight-animation');
      }
    }, 100);
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

const getPercentileBadgeClass = (rank: number, total: number): string => {
  const percentile = (1 - (rank - 1) / total) * 100;
  if (percentile >= 99) return 'percentile-elite';
  if (percentile >= 95) return 'percentile-master';
  if (percentile >= 90) return 'percentile-expert';
  if (percentile >= 75) return 'percentile-veteran';
  return 'percentile-regular';
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
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.3);
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
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.3);
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
  border-color: rgba(0, 255, 242, 0.3);
  box-shadow: 0 0 20px rgba(0, 255, 242, 0.1);
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
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.5);
}

.detail-card {
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 1rem;
  transition: all 0.3s ease;
}

.detail-card:hover {
  border-color: rgba(0, 255, 242, 0.2);
  box-shadow: 0 0 30px rgba(0, 255, 242, 0.05);
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
  background: rgba(0, 255, 242, 0.05);
}

.detail-link {
  color: var(--text-primary);
  text-decoration: none;
  transition: color 0.2s ease;
}

.detail-link:hover {
  color: var(--neon-cyan);
  text-shadow: 0 0 5px rgba(0, 255, 242, 0.5);
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
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.2);
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
  background: rgba(0, 255, 242, 0.1);
}

.detail-pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.detail-pagination-info {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.rankings-table-container {
  position: relative;
}

.context-players-section {
  margin-bottom: 1rem;
  background: linear-gradient(135deg, rgba(0, 255, 242, 0.05) 0%, transparent 100%);
  border: 1px solid rgba(0, 255, 242, 0.2);
  border-radius: 6px;
  overflow: hidden;
}

.context-header {
  padding: 0.5rem 1rem;
  background: rgba(0, 255, 242, 0.08);
  border-bottom: 1px solid rgba(0, 255, 242, 0.15);
}

.context-table {
  border: none;
  margin: 0;
}

.context-table td {
  border-bottom-color: rgba(0, 255, 242, 0.1);
}

.context-separator {
  text-align: center;
  padding: 1rem;
  position: relative;
}

.context-separator::before,
.context-separator::after {
  content: '';
  position: absolute;
  top: 50%;
  width: calc(50% - 4rem);
  height: 1px;
  background: var(--border-color);
}

.context-separator::before {
  left: 0;
}

.context-separator::after {
  right: 0;
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
  box-shadow: 0 0 10px rgba(255, 215, 0, 0.4);
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

.player-position-sticky {
  position: sticky;
  top: -1px;
  z-index: 10;
  background: var(--bg-card);
  padding-bottom: 0.5rem;
  margin-bottom: 1rem;
}

.player-position-card {
  background: linear-gradient(135deg, rgba(0, 255, 242, 0.1) 0%, rgba(0, 255, 242, 0.02) 100%);
  border: 1px solid rgba(0, 255, 242, 0.3);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 0 30px rgba(0, 255, 242, 0.1);
}

.player-position-header {
  background: rgba(0, 255, 242, 0.05);
  padding: 0.5rem 1rem;
  border-bottom: 1px solid rgba(0, 255, 242, 0.2);
}

.player-position-content {
  padding: 1rem;
}

.rank-display {
  text-align: center;
  padding-right: 1rem;
  border-right: 1px solid var(--border-color);
}

.rank-badge-large {
  font-size: 2rem;
  font-weight: 700;
  line-height: 1;
  margin-bottom: 0.25rem;
}

.rank-context {
  font-size: 0.625rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.percentile-badge {
  padding: 0.25rem 0.75rem;
  font-size: 0.625rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  border-radius: 20px;
  text-transform: uppercase;
}

.percentile-elite {
  background: var(--neon-gold);
  color: var(--bg-dark);
  box-shadow: 0 0 15px rgba(255, 215, 0, 0.5);
}

.percentile-master {
  background: var(--neon-cyan);
  color: var(--bg-dark);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.5);
}

.percentile-expert {
  background: var(--neon-pink);
  color: var(--bg-dark);
  box-shadow: 0 0 15px rgba(255, 0, 255, 0.5);
}

.percentile-veteran {
  background: var(--bg-panel);
  color: var(--text-primary);
  border: 1px solid var(--neon-cyan);
}

.percentile-regular {
  background: var(--bg-panel);
  color: var(--text-secondary);
  border: 1px solid var(--border-color);
}

.jump-to-player-btn {
  display: flex;
  align-items: center;
  padding: 0.375rem 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--bg-panel);
  border: 1px solid var(--neon-cyan);
  border-radius: 4px;
  color: var(--neon-cyan);
  cursor: pointer;
  transition: all 0.2s ease;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.jump-to-player-btn:hover {
  background: rgba(0, 255, 242, 0.1);
  box-shadow: 0 0 15px rgba(0, 255, 242, 0.3);
  transform: translateY(-1px);
}

.player-row-highlight td {
  background: rgba(0, 255, 242, 0.08) !important;
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
  box-shadow: 0 0 10px rgba(0, 255, 242, 0.5);
}

.highlight-animation {
  animation: highlight-pulse 2s ease-in-out;
}

@keyframes highlight-pulse {
  0% { 
    background-color: rgba(0, 255, 242, 0.08);
  }
  50% { 
    background-color: rgba(0, 255, 242, 0.2);
    box-shadow: 0 0 30px rgba(0, 255, 242, 0.3);
  }
  100% { 
    background-color: rgba(0, 255, 242, 0.08);
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
  background: rgba(0, 255, 242, 0.1);
}

/* Neon color utilities */
.text-neon-cyan { color: var(--neon-cyan); text-shadow: 0 0 10px rgba(0, 255, 242, 0.5); }
.text-neon-green { color: var(--neon-green); text-shadow: 0 0 10px rgba(57, 255, 20, 0.5); }
.text-neon-red { color: var(--neon-red); text-shadow: 0 0 10px rgba(255, 0, 0, 0.5); }
.text-neon-gold { color: var(--neon-gold); text-shadow: 0 0 10px rgba(255, 215, 0, 0.5); }
</style>