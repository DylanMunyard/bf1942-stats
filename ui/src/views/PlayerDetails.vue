<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { PlayerTimeStatistics, fetchPlayerStats } from '../services/playerStatsService';
import { TrendDataPoint, PlayerAchievementGroup } from '../types/playerStatsTypes';
import { Line } from 'vue-chartjs';
import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Legend, Filler } from 'chart.js';
import PlayerAchievementSummary from '../components/PlayerAchievementSummary.vue';
import PlayerRecentRoundsCompact from '../components/PlayerRecentRoundsCompact.vue';
import HeroBackButton from '../components/HeroBackButton.vue';
import PlayerAchievementHeroBadges from '../components/PlayerAchievementHeroBadges.vue';
import PlayerServerMapStats from '../components/PlayerServerMapStats.vue';
import MapRankingsPanel from '../components/MapRankingsPanel.vue';
import PlayerDetailPanel from '../components/data-explorer/PlayerDetailPanel.vue';
import MapDetailPanel from '../components/data-explorer/MapDetailPanel.vue';
import ServerMapDetailPanel from '../components/data-explorer/ServerMapDetailPanel.vue';
import { formatRelativeTime } from '@/utils/timeUtils';
import { calculateKDR } from '@/utils/statsUtils';
import { useAIContext } from '@/composables/useAIContext';

import bf1942Icon from '@/assets/bf1942.webp';
import fh2Icon from '@/assets/fh2.webp';
import bfvIcon from '@/assets/bfv.webp';
import defaultIcon from '@/assets/servers.webp';

// Register Chart.js components
ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Legend, Filler);

// Router
const router = useRouter();
const route = useRoute();

// AI Context
const { setContext, clearContext } = useAIContext();

const playerName = ref(route.params.playerName as string);
const playerStats = ref<PlayerTimeStatistics | null>(null);
const isLoading = ref(true);
const error = ref<string | null>(null);
const showTrendCharts = ref(false);
const trendChartHideTimeout = ref<NodeJS.Timeout | null>(null);
const trendChartHoverCount = ref(0);
const showLastOnline = ref(false);
const achievementGroups = ref<PlayerAchievementGroup[]>([]);
const achievementGroupsLoading = ref(false);
const achievementGroupsError = ref<string | null>(null);


// State for server map stats view
const selectedServerGuid = ref<string | null>(null);
const scrollPositionBeforeMapStats = ref(0);

// State for rankings drill-down panel
const rankingsMapName = ref<string | null>(null);
const rankingsServerGuid = ref<string | null>(null);

// State for map detail panel (from data explorer breakdown)
const selectedMapDetailName = ref<string | null>(null);

// State for server map detail panel (drill-down from map detail)
const selectedServerMapDetail = ref<{ serverGuid: string; mapName: string } | null>(null);

// Wide viewport: show slide-out panel side-by-side (lg: 1024px+)
const isWideScreen = ref(false);
const updateWideScreen = () => {
  isWideScreen.value = typeof window !== 'undefined' && window.innerWidth >= 1024;
};

// Best Scores state
const selectedBestScoresTab = ref<'allTime' | 'last30Days' | 'thisWeek'>('thisWeek');
const bestScoresTabOptions = [
  { key: 'allTime' as const, label: 'All Time' },
  { key: 'last30Days' as const, label: '30 Days' },
  { key: 'thisWeek' as const, label: 'This Week' }
] as const;

// Function to handle best scores tab change with scroll reset
const changeBestScoresTab = (tabKey: 'allTime' | 'last30Days' | 'thisWeek') => {
  selectedBestScoresTab.value = tabKey;
  
  // Reset scroll position of horizontal scroll container on mobile
  setTimeout(() => {
    const scrollContainer = document.querySelector('.best-scores-scroll-container');
    if (scrollContainer) {
      scrollContainer.scrollLeft = 0;
    }
  }, 50); // Small delay to ensure DOM has updated
};


// Computed properties for trend charts
const killRateTrendChartData = computed(() => {
  if (!playerStats.value?.recentStats?.killRateTrend) return { labels: [], datasets: [] };

  const trend = playerStats.value.recentStats.killRateTrend;
  const labels = trend.map((point: TrendDataPoint) => new Date(point.timestamp).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }));
  const data = trend.map((point: TrendDataPoint) => point.value);

  return {
    labels,
    datasets: [{
      label: 'Kill Rate',
      data,
      borderColor: '#4CAF50',
      backgroundColor: 'rgba(76, 175, 80, 0.1)',
      borderWidth: 2,
      fill: true,
      tension: 0.4,
      pointRadius: 0,
      pointHoverRadius: 4,
      pointBackgroundColor: '#4CAF50',
      pointBorderColor: '#ffffff',
      pointBorderWidth: 1,
    }]
  };
});

const kdRatioTrendChartData = computed(() => {
  if (!playerStats.value?.recentStats?.kdRatioTrend) return { labels: [], datasets: [] };

  const trend = playerStats.value.recentStats.kdRatioTrend;
  const labels = trend.map((point: TrendDataPoint) => new Date(point.timestamp).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }));
  const data = trend.map((point: TrendDataPoint) => point.value);

  return {
    labels,
    datasets: [{
      label: 'K/D Ratio',
      data,
      borderColor: '#a855f7',
      backgroundColor: 'rgba(168, 85, 247, 0.1)',
      borderWidth: 2,
      fill: true,
      tension: 0.4,
      pointRadius: 0,
      pointHoverRadius: 4,
      pointBackgroundColor: '#a855f7',
      pointBorderColor: '#ffffff',
      pointBorderWidth: 1,
    }]
  };
});

const microChartOptions = computed(() => {
  const computedStyles = window.getComputedStyle(document.documentElement);
  const isDarkMode = computedStyles.getPropertyValue('--color-background').trim().includes('26, 16, 37') ||
                    document.documentElement.classList.contains('dark-mode') ||
                    (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);

  return {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      intersect: false,
      mode: 'index' as const
    },
    scales: {
      y: {
        display: false,
        grid: {
          display: false
        }
      },
      x: {
        display: false,
        grid: {
          display: false
        }
      }
    },
    plugins: {
      legend: {
        display: false
      },
      tooltip: {
        enabled: true,
        backgroundColor: isDarkMode ? 'rgba(35, 21, 53, 0.95)' : 'rgba(0, 0, 0, 0.8)',
        titleColor: '#ffffff',
        bodyColor: '#ffffff',
        borderColor: isDarkMode ? '#9c27b0' : '#666666',
        borderWidth: 1,
        cornerRadius: 6,
        displayColors: false,
        padding: 8,
        titleFont: { size: 12, weight: 'bold' as const },
        bodyFont: { size: 11 },
        callbacks: {
          title: function(context: any[]) {
            return context[0].label;
          },
          label: function(context: any) {
            const label = context.dataset.label;
            const value = context.parsed.y;
            if (label === 'Kill Rate') {
              return `${value.toFixed(3)} k/min`;
            } else if (label === 'K/D Ratio') {
              return `${value.toFixed(2)}`;
            }
            return `${value.toFixed(2)}`;
          }
        }
      }
    },
    elements: {
      point: {
        radius: 0,
        hoverRadius: 2
      },
      line: {
        borderWidth: 1
      }
    }
  };
});

// Function to show map stats for a server
const showServerMapStats = (serverGuid: string) => {
  scrollPositionBeforeMapStats.value = window.scrollY;
  selectedServerGuid.value = serverGuid;
  // On wide screens the panel is side-by-side; scroll to top so the panel is visible. On mobile the panel is a fixed overlay, so don't scroll.
  if (isWideScreen.value) {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
};

// Function to close server map stats view
const closeServerMapStats = () => {
  selectedServerGuid.value = null;
  rankingsMapName.value = null;
  rankingsServerGuid.value = null;
  window.scrollTo({ top: scrollPositionBeforeMapStats.value, behavior: 'auto' });
};

// Function to open rankings drill-down from map stats
const openRankingsPanel = (mapName: string) => {
  rankingsMapName.value = mapName;
  rankingsServerGuid.value = effectiveServerGuid.value ?? null;
};

// Function to close rankings and go back to map stats
const closeRankingsPanel = () => {
  rankingsMapName.value = null;
  rankingsServerGuid.value = null;
};

// Function to open map detail from data explorer breakdown
const openMapDetail = (mapName: string) => {
  scrollPositionBeforeMapStats.value = window.scrollY;
  selectedMapDetailName.value = mapName;
};

// Function to close map detail
const closeMapDetail = () => {
  selectedMapDetailName.value = null;
  window.scrollTo({ top: scrollPositionBeforeMapStats.value, behavior: 'auto' });
};

// Function to open server map detail (drill-down from map detail)
const openServerMapDetail = (serverGuid: string) => {
  if (selectedMapDetailName.value) {
    selectedServerMapDetail.value = {
      serverGuid,
      mapName: selectedMapDetailName.value
    };
  }
};

// Function to close server map detail
const closeServerMapDetail = () => {
  selectedServerMapDetail.value = null;
};

const fetchData = async () => {
  isLoading.value = true;
  error.value = null;
  fetchAchievementGroups();
  try {
    playerStats.value = await fetchPlayerStats(playerName.value);
  } catch (err) {
    error.value = `Failed to fetch player stats for ${playerName.value}.`;
    console.error(err);
  } finally {
    isLoading.value = false;
  }
};

const fetchAchievementGroups = async () => {
  achievementGroupsLoading.value = true;
  achievementGroupsError.value = null;
  try {
    const response = await fetch(`/stats/gamification/player/${encodeURIComponent(playerName.value)}/achievement-groups`);
    if (!response.ok) throw new Error('Failed to fetch achievement groups');
    achievementGroups.value = await response.json();
  } catch (err) {
    console.error('Error fetching achievement groups:', err);
    achievementGroupsError.value = 'Failed to load achievements.';
    achievementGroups.value = [];
  } finally {
    achievementGroupsLoading.value = false;
  }
};


// Format minutes to hours and minutes
const formatPlayTime = (minutes: number): string => {
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = Math.floor(minutes % 60);

  if (hours === 0) {
    return `${remainingMinutes}m`;
  } else {
    return `${hours}h ${remainingMinutes}m`;
  }
};

// Function to navigate to round report using best score data
const navigateToRoundReport = (roundId: string) => {
  router.push({
    name: 'round-report',
    params: {
      roundId: roundId,
    },
    query: {
      players: playerName.value // Include the player name to pin them
    }
  });
};


// Functions to handle sticky trend chart hover behavior using hover counter
const enterTrendChartArea = () => {
  trendChartHoverCount.value++;
  showTrendCharts.value = true;
  // Clear any pending hide timeout
  if (trendChartHideTimeout.value) {
    clearTimeout(trendChartHideTimeout.value);
    trendChartHideTimeout.value = null;
  }
};

const leaveTrendChartArea = () => {
  trendChartHoverCount.value = Math.max(0, trendChartHoverCount.value - 1);
  // Only hide if no areas are being hovered
  if (trendChartHoverCount.value === 0) {
    trendChartHideTimeout.value = setTimeout(() => {
      showTrendCharts.value = false;
      trendChartHideTimeout.value = null;
    }, 800); // Longer delay for moving between charts
  }
};




// Computed property to get the current expanded server's name removed - was unused

// Computed property to get the selected server's name for panel header
const selectedServerName = computed(() => {
  if (!selectedServerGuid.value) return null;
  if (selectedServerGuid.value === '__all__') return 'All Servers';

  // Check server list first
  const server = playerStats.value?.servers?.find(s => s.serverGuid === selectedServerGuid.value);
  if (server) return server.serverName;

  // Fallback to rankings
  const ranking = playerStats.value?.insights?.serverRankings?.find(
    r => r.serverGuid === selectedServerGuid.value
  );
  return ranking?.serverName || null;
});

// Computed property for current best scores
const currentBestScores = computed(() => {
  if (!playerStats.value?.bestScores) return [];
  return playerStats.value.bestScores[selectedBestScoresTab.value] || [];
});

const playerPanelGame = computed(() => {
  return playerStats.value?.servers?.[0]?.gameId || 'bf1942';
});



onMounted(() => {
  fetchData();
  document.addEventListener('click', closeTooltipOnClickOutside);
  updateWideScreen();
  window.addEventListener('resize', updateWideScreen);

  // Set AI context for player page
  setContext({
    pageType: 'player',
    playerName: playerName.value,
    game: 'bf1942'
  });
});

onUnmounted(() => {
  clearContext();
});

const gameIcons: { [key: string]: string } = {
  bf1942: bf1942Icon,
  fh2: fh2Icon,
  bfv: bfvIcon,
};

const getGameIcon = (gameId: string): string => {
  if (!gameId) return defaultIcon;
  return gameIcons[gameId.toLowerCase()] || defaultIcon;
};

// --- Server List ---

// Unified server entry type: stats fields are optional because rankings-only entries won't have them
interface UnifiedServer {
  serverGuid: string;
  serverName: string;
  gameId: string;
  totalMinutes: number;
  totalKills: number;
  totalDeaths: number;
  kdRatio: number;
  killsPerMinute: number;
  totalRounds: number;
  highestScore: number;
  ranking: import('../types/playerStatsTypes').ServerRanking | null;
  hasStats: boolean; // true when playtime/K-D data is available
}

// Unified server list: merges server playtime data with ranking data, sorted by rank
const unifiedServerList = computed<UnifiedServer[]>(() => {
  const servers = playerStats.value?.servers ?? [];
  const rankings = playerStats.value?.insights?.serverRankings ?? [];

  if (servers.length === 0 && rankings.length === 0) return [];

  const rankMap = new Map(rankings.map(r => [r.serverGuid, r]));
  const seenGuids = new Set<string>();

  // Start with servers array (has full stats)
  const unified: UnifiedServer[] = servers.map(server => {
    seenGuids.add(server.serverGuid);
    return {
      ...server,
      ranking: rankMap.get(server.serverGuid) || null,
      hasStats: true,
    };
  });

  // Add any ranked servers that aren't in the servers array
  for (const ranking of rankings) {
    if (!seenGuids.has(ranking.serverGuid)) {
      unified.push({
        serverGuid: ranking.serverGuid,
        serverName: ranking.serverName,
        gameId: '',
        totalMinutes: 0,
        totalKills: 0,
        totalDeaths: 0,
        kdRatio: 0,
        killsPerMinute: 0,
        totalRounds: 0,
        highestScore: 0,
        ranking,
        hasStats: false,
      });
    }
  }

  // Sort: ranked servers first by rank ascending, then unranked by playtime descending
  unified.sort((a, b) => {
    const aRank = a.ranking ? rankNum(a.ranking) : Infinity;
    const bRank = b.ranking ? rankNum(b.ranking) : Infinity;
    if (aRank !== bRank) return aRank - bRank;
    return b.totalMinutes - a.totalMinutes;
  });

  return unified;
});

// Helper: rank badge color based on position
const getRankBadgeClass = (rank: number): string => {
  if (rank === 1) return 'text-neon-gold font-bold';
  if (rank === 2) return 'text-neutral-300 font-bold';
  if (rank === 3) return 'text-orange-400 font-bold';
  if (rank <= 10) return 'text-neon-cyan font-semibold';
  return 'text-neutral-400 font-medium';
};

// Helper to get playtime percentage
const getPlaytimePercentage = (serverMinutes: number) => {
  if (!playerStats.value?.servers) return 0;
  const total = playerStats.value.servers.reduce((sum, s) => sum + s.totalMinutes, 0);
  return total > 0 ? (serverMinutes / total) * 100 : 0;
};

// Show all-servers map rankings
const showAllServerMaps = () => {
  scrollPositionBeforeMapStats.value = window.scrollY;
  selectedServerGuid.value = '__all__';
  if (isWideScreen.value) {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
};

// Whether the map stats panel is open (specific server or all servers)
const isMapStatsPanelOpen = computed(() => selectedServerGuid.value !== null);
const effectiveServerGuid = computed(() => selectedServerGuid.value === '__all__' ? undefined : selectedServerGuid.value);

// Numeric rank for ServerRanking (API may send rankDisplay instead of rank)
const rankNum = (ranking: { rank?: number; rankDisplay?: string }): number => {
  if (typeof ranking.rank === 'number' && !Number.isNaN(ranking.rank)) return ranking.rank;
  const parsed = parseInt(ranking.rankDisplay ?? '', 10);
  return Number.isNaN(parsed) ? 99 : parsed;
};

// High-level rankings summary for hero strip
const rankingsSummary = computed(() => {
  const rankings = playerStats.value?.insights?.serverRankings ?? [];
  if (rankings.length === 0) return null;
  const numOnes = rankings.filter(r => rankNum(r) === 1).length;
  const numTop10 = rankings.filter(r => rankNum(r) <= 10).length;
  const best = [...rankings].sort((a, b) => rankNum(a) - rankNum(b))[0];
  return { totalRanked: rankings.length, numOnes, numTop10, best };
});



// Add watcher for route changes to update playerName and refetch data
watch(
  () => route.params.playerName,
  (newName, oldName) => {
    if (newName !== oldName) {
      playerName.value = newName as string;
      fetchData();
    }
  }
);

// Close tooltip when clicking outside
const closeTooltipOnClickOutside = (event: MouseEvent) => {
  const target = event.target as HTMLElement;
  if (!target.closest('.group.cursor-pointer')) {
    showLastOnline.value = false;
  }
};

// Cleanup function to restore body scroll and remove event listener when component unmounts
onUnmounted(() => {
  document.body.style.overflow = 'unset';
  document.removeEventListener('click', closeTooltipOnClickOutside);
  window.removeEventListener('resize', updateWideScreen);

  // Clear any pending trend chart hide timeout
  if (trendChartHideTimeout.value) {
    clearTimeout(trendChartHideTimeout.value);
  }
});

</script>

<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <div class="data-explorer">
        <div class="explorer-inner">
          
          <!-- Loading State -->
          <div v-if="isLoading" class="flex flex-col items-center justify-center py-20 text-neutral-400" role="status" aria-label="Loading player statistics">
            <div class="explorer-spinner mb-4" />
            <p class="text-lg text-neutral-300">Loading Player Statistics...</p>
          </div>

          <!-- Error State -->
          <div v-else-if="error" class="explorer-card p-8 text-center" role="alert">
            <div class="flex items-center justify-center mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-red-400"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>
            </div>
            <p class="text-neon-red text-lg font-medium">{{ error }}</p>
          </div>

          <!-- Content -->
          <div v-else-if="playerStats" class="space-y-6">
            
            <!-- Player Header Card -->
            <div class="explorer-card">
              <div class="explorer-card-body">
                <div class="flex flex-wrap items-center gap-3">
                  
                  <!-- Avatar -->
                  <div class="relative group cursor-pointer flex-shrink-0" @click="showLastOnline = !showLastOnline">
                    <div class="w-10 h-10 rounded-full bg-[var(--bg-panel)] border border-[var(--border-color)] flex items-center justify-center text-lg font-bold text-neon-cyan font-mono"
                         :class="playerStats?.isActive ? 'ring-2 ring-neon-green/50' : ''">
                      {{ playerName?.charAt(0)?.toUpperCase() || '?' }}
                    </div>
                    <div class="absolute -bottom-0.5 -right-0.5 w-3 h-3 rounded-full border-2 border-[var(--bg-panel)]"
                         :class="playerStats?.isActive ? 'bg-neon-green' : 'bg-neutral-600'" />
                    <!-- Last Online Tooltip -->
                    <div
                      v-if="showLastOnline"
                      class="absolute top-full left-1/2 transform -translate-x-1/2 mt-2 bg-[var(--bg-card)] border border-[var(--border-color)] rounded px-3 py-2 text-xs text-neutral-300 whitespace-nowrap z-50 shadow-xl"
                    >
                      <div class="flex items-center gap-2">
                        <div class="w-2 h-2 rounded-full" :class="playerStats?.isActive ? 'bg-neon-green' : 'bg-neutral-500'" />
                        <span class="font-mono">
                          {{ playerStats?.isActive ? 'CURRENTLY ONLINE' : `LAST ONLINE: ${formatRelativeTime(playerStats?.lastPlayed || '')}`.toUpperCase() }}
                        </span>
                      </div>
                    </div>
                  </div>

                  <h1 class="text-xl md:text-2xl font-bold text-neon-cyan truncate max-w-full lg:max-w-[34rem] font-mono">
                    {{ playerName }}
                  </h1>

                  <PlayerAchievementHeroBadges :player-name="playerName" />

                  <div class="flex flex-wrap gap-2 items-center ml-auto">
                    <!-- K/D Badge -->
                    <div class="relative" @mouseenter="enterTrendChartArea" @mouseleave="leaveTrendChartArea">
                      <div class="explorer-tag explorer-tag--accent flex items-center gap-2 cursor-pointer">
                        <span class="font-bold">{{ calculateKDR(playerStats?.totalKills || 0, playerStats?.totalDeaths || 0) }}</span>
                        <span class="text-neutral-500">K/D</span>
                      </div>

                      <!-- Trend Charts -->
                      <div
                        v-if="showTrendCharts"
                        class="absolute right-0 top-full mt-2 bg-[var(--bg-card)] border border-[var(--border-color)] rounded p-3 w-80 z-50 shadow-2xl"
                        @mouseenter="enterTrendChartArea"
                        @mouseleave="leaveTrendChartArea"
                      >
                        <div class="space-y-4">
                          <div class="space-y-1">
                            <div class="text-xs font-mono text-neon-cyan">KILL RATE TREND</div>
                            <div class="h-16 -mx-1">
                              <Line :data="killRateTrendChartData" :options="microChartOptions" />
                            </div>
                          </div>
                          <div class="space-y-1">
                            <div class="text-xs font-mono text-neon-pink">K/D TREND</div>
                            <div class="h-16 -mx-1">
                              <Line :data="kdRatioTrendChartData" :options="microChartOptions" />
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>

                    <!-- Playtime -->
                    <div class="explorer-tag flex items-center gap-2">
                      <span class="text-neutral-400">TIME</span>
                      <span>{{ formatPlayTime(playerStats?.totalPlayTimeMinutes || 0) }}</span>
                    </div>

                    <!-- Last Played -->
                    <div class="explorer-tag flex items-center gap-2">
                      <span class="text-neutral-400">SEEN</span>
                      <span>{{ formatRelativeTime(playerStats?.lastPlayed || '') }}</span>
                    </div>

                    <!-- Compare Button -->
                    <router-link
                      :to="{ path: '/players/compare', query: { player1: playerName } }"
                      class="explorer-btn explorer-btn--ghost explorer-btn--sm"
                      title="Compare this player"
                    >
                      COMPARE
                    </router-link>
                  </div>
                </div>

                <!-- Recent Rounds Compact -->
                <div v-if="playerStats?.recentSessions && playerStats.recentSessions.length > 0" class="mt-4 pt-4 border-t border-[var(--border-color)]">
                  <PlayerRecentRoundsCompact
                    :sessions="playerStats.recentSessions"
                    :player-name="playerName"
                  />
                </div>
              </div>
            </div>

            <!-- Main Grid Layout -->
            <div class="grid grid-cols-1 xl:grid-cols-12 gap-6">
              
              <!-- Left Column: Data Explorer Breakdown -->
              <div class="xl:col-span-7 space-y-6">
                <div class="explorer-card">
                  <div class="explorer-card-header">
                    <h3 class="explorer-card-title">DATA EXPLORER BREAKDOWN</h3>
                    <p class="text-[10px] text-neutral-500 font-mono mt-1">EXPANDED MAP AND SERVER SLICING</p>
                  </div>
                  <div class="explorer-card-body p-0">
                    <PlayerDetailPanel
                      :player-name="playerName"
                      :game="playerPanelGame"
                      @navigate-to-map="openMapDetail"
                    />
                  </div>
                </div>
              </div>

              <!-- Right Column: Achievements, Best Scores, Servers -->
              <div class="xl:col-span-5 space-y-6">
                
                <!-- Achievements -->
                <div class="explorer-card">
                  <div class="explorer-card-header flex items-center justify-between">
                    <h3 class="explorer-card-title">ACHIEVEMENTS</h3>
                    <router-link
                      :to="`/players/${encodeURIComponent(playerName)}/achievements`"
                      class="explorer-link text-xs font-mono uppercase"
                    >
                      View All &rarr;
                    </router-link>
                  </div>
                  <div class="explorer-card-body">
                    <PlayerAchievementSummary
                      :player-name="playerName"
                      :achievement-groups="achievementGroups"
                      :loading="achievementGroupsLoading"
                      :error="achievementGroupsError"
                    />
                  </div>
                </div>

                <!-- Best Scores -->
                <div class="explorer-card">
                  <div class="explorer-card-header flex items-center justify-between">
                    <h3 class="explorer-card-title">BEST SCORES</h3>
                    <div class="explorer-toggle-group">
                      <button
                        v-for="tab in bestScoresTabOptions"
                        :key="tab.key"
                        class="explorer-toggle-btn"
                        :class="{ 'explorer-toggle-btn--active': selectedBestScoresTab === tab.key }"
                        @click="changeBestScoresTab(tab.key)"
                      >
                        {{ tab.label === 'All Time' ? 'ALL' : tab.label === '30 Days' ? '30D' : 'WK' }}
                      </button>
                    </div>
                  </div>
                  <div class="explorer-card-body p-0">
                    <div v-if="currentBestScores.length === 0" class="p-6 text-center text-neutral-500 text-sm font-mono">
                      NO SCORES RECORDED
                    </div>
                    <div v-else class="divide-y divide-[var(--border-color)]">
                      <div
                        v-for="(score, index) in currentBestScores.slice(0, 5)"
                        :key="`${score.roundId}-${index}`"
                        class="p-3 hover:bg-white/5 transition-colors cursor-pointer flex items-center gap-3"
                        @click="navigateToRoundReport(score.roundId)"
                      >
                        <div class="w-6 h-6 rounded bg-[var(--bg-panel)] border border-[var(--border-color)] flex items-center justify-center text-xs font-bold text-neon-gold font-mono">
                          {{ index + 1 }}
                        </div>
                        <div class="flex-1 min-w-0">
                          <div class="text-sm font-bold text-neon-cyan font-mono truncate">
                            {{ score.score.toLocaleString() }} PTS
                          </div>
                          <div class="text-xs text-neutral-400 truncate font-mono">
                            {{ score.mapName }} • {{ score.serverName }}
                          </div>
                        </div>
                        <div class="text-[10px] text-neutral-500 font-mono text-right">
                          <div>{{ formatRelativeTime(score.timestamp) }}</div>
                          <div>K/D {{ calculateKDR(score.kills, score.deaths) }}</div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

                <!-- Servers List -->
                <div class="explorer-card">
                  <div class="explorer-card-header flex items-center justify-between">
                    <h3 class="explorer-card-title">SERVERS</h3>
                    <button
                      type="button"
                      class="explorer-btn explorer-btn--ghost explorer-btn--sm"
                      @click="showAllServerMaps"
                    >
                      ALL MAP RANKINGS
                    </button>
                  </div>
                  <div class="explorer-card-body p-0">
                    <div class="divide-y divide-[var(--border-color)]">
                      <div
                        v-for="server in unifiedServerList"
                        :key="server.serverGuid"
                        class="group p-3 hover:bg-white/5 transition-colors cursor-pointer flex items-center gap-3"
                        @click="showServerMapStats(server.serverGuid)"
                      >
                        <!-- Rank -->
                        <div class="w-8 text-center font-mono text-sm" :class="getRankBadgeClass(rankNum(server.ranking))">
                          <span v-if="server.ranking">#{{ server.ranking.rankDisplay ?? server.ranking.rank }}</span>
                          <span v-else class="text-neutral-600">-</span>
                        </div>

                        <!-- Icon -->
                        <div v-if="server.gameId" class="w-8 h-8 rounded bg-black/20 p-0.5">
                          <img :src="getGameIcon(server.gameId)" alt="" class="w-full h-full object-cover rounded-sm" />
                        </div>

                        <!-- Details -->
                        <div class="flex-1 min-w-0">
                          <div class="text-sm font-medium text-neutral-200 truncate group-hover:text-neon-cyan transition-colors font-mono">
                            {{ server.serverName }}
                          </div>
                          <div class="flex items-center gap-2 text-[10px] text-neutral-500 font-mono mt-0.5">
                            <span v-if="server.hasStats">{{ formatPlayTime(server.totalMinutes) }}</span>
                            <span v-if="server.hasStats">|</span>
                            <span v-if="server.hasStats">K/D {{ Number(server.kdRatio).toFixed(2) }}</span>
                            <span v-else-if="server.ranking">{{ server.ranking.scoreDisplay || server.ranking.totalScore.toLocaleString() }} score</span>
                          </div>
                        </div>

                        <!-- Arrow -->
                        <div class="text-neutral-600 group-hover:text-neon-cyan transition-colors">
                          &rarr;
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

              </div>
            </div>

          </div>

          <!-- Empty State -->
          <div v-else class="explorer-empty">
            <div class="explorer-empty-icon"><svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" class="text-neutral-500"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg></div>
            <p class="explorer-empty-title">No player data available</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Map Stats Panel (Overlay) -->
    <div v-if="isMapStatsPanelOpen && playerStats?.servers" class="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-0 sm:p-4" @click="closeServerMapStats">
      <div class="w-full max-w-5xl h-full sm:h-auto sm:max-h-[90vh] overflow-hidden flex flex-col bg-[var(--bg-panel)] border-x-0 sm:border border-[var(--border-color)] rounded-none sm:rounded-lg shadow-2xl" @click.stop>
        <!-- Header -->
        <div class="p-3 sm:p-4 border-b border-[var(--border-color)] flex justify-between items-center bg-[var(--bg-panel)]">
          <div>
            <h2 class="text-base sm:text-lg font-bold text-neon-cyan font-mono">
              {{ rankingsMapName ? `RANKINGS: ${rankingsMapName}` : 'MAP RANKINGS' }}
            </h2>
            <p class="text-[10px] sm:text-xs text-neutral-400 font-mono mt-1">
              {{ selectedServerName || 'SELECTED SERVER' }}
            </p>
          </div>
          <button class="explorer-btn explorer-btn--ghost explorer-btn--sm" aria-label="Close map rankings panel" @click="closeServerMapStats">CLOSE</button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-2 sm:p-4">
          <div v-if="rankingsMapName">
            <button
              class="explorer-btn explorer-btn--ghost explorer-btn--sm mb-4 flex items-center gap-2"
              @click="closeRankingsPanel"
            >
              &larr; BACK TO MAP STATS
            </button>
            <MapRankingsPanel
              :map-name="rankingsMapName"
              :server-guid="rankingsServerGuid ?? undefined"
              :highlight-player="playerName"
              :game="(effectiveServerGuid ? playerStats?.servers?.find(s => s.serverGuid === effectiveServerGuid)?.gameId as any : undefined) || 'bf1942'"
            />
          </div>
          <PlayerServerMapStats
            v-else
            :player-name="playerName"
            :server-guid="effectiveServerGuid"
            :game="(effectiveServerGuid ? playerStats?.servers?.find(s => s.serverGuid === effectiveServerGuid)?.gameId as any : undefined) || 'bf1942'"
            @open-rankings="openRankingsPanel"
          />
        </div>
      </div>
    </div>

    <!-- Map Detail Panel (Overlay from Data Explorer Breakdown) -->
    <div v-if="selectedMapDetailName" class="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-0 sm:p-4" @click="closeMapDetail">
      <div class="w-full max-w-5xl h-full sm:h-auto sm:max-h-[90vh] overflow-hidden flex flex-col bg-[var(--bg-panel)] border-x-0 sm:border border-[var(--border-color)] rounded-none sm:rounded-lg shadow-2xl" @click.stop>
        <!-- Header -->
        <div class="p-3 sm:p-4 border-b border-[var(--border-color)] flex justify-between items-center bg-[var(--bg-panel)]">
          <div>
            <h2 class="text-base sm:text-lg font-bold text-neon-cyan font-mono">
              MAP DETAILS
            </h2>
            <p class="text-[10px] sm:text-xs text-neutral-400 font-mono mt-1">
              {{ selectedMapDetailName }}
            </p>
          </div>
          <button class="explorer-btn explorer-btn--ghost explorer-btn--sm" aria-label="Close map detail panel" @click="closeMapDetail">CLOSE</button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-2 sm:p-4 bg-[var(--bg-panel)]">
          <MapDetailPanel 
            :map-name="selectedMapDetailName" 
            @navigate-to-server="openServerMapDetail"
          />
        </div>
      </div>
    </div>

    <!-- Server Map Detail Panel (Overlay from Map Detail Panel) -->
    <div v-if="selectedServerMapDetail" class="fixed inset-0 z-[110] flex items-center justify-center bg-black/80 backdrop-blur-sm p-0 sm:p-4" @click="closeServerMapDetail">
      <div class="w-full max-w-5xl h-full sm:h-auto sm:max-h-[90vh] overflow-hidden flex flex-col bg-[var(--bg-panel)] border-x-0 sm:border border-[var(--border-color)] rounded-none sm:rounded-lg shadow-2xl" @click.stop>
        <!-- Header -->
        <div class="p-3 sm:p-4 border-b border-[var(--border-color)] flex justify-between items-center bg-[var(--bg-panel)]">
          <div>
            <h2 class="text-base sm:text-lg font-bold text-neon-cyan font-mono">
              SERVER MAP DETAILS
            </h2>
            <p class="text-[10px] sm:text-xs text-neutral-400 font-mono mt-1">
              {{ selectedServerMapDetail.mapName }}
            </p>
          </div>
          <button class="explorer-btn explorer-btn--ghost explorer-btn--sm" aria-label="Close server map detail panel" @click="closeServerMapDetail">CLOSE</button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-0 bg-[var(--bg-panel)]">
          <ServerMapDetailPanel
            :server-guid="selectedServerMapDetail.serverGuid"
            :map-name="selectedServerMapDetail.mapName"
            @close="closeServerMapDetail"
          />
        </div>
      </div>
    </div>

  </div>
</template>

<style src="./portal-layout.css"></style>
<style scoped src="./DataExplorer.vue.css"></style>
