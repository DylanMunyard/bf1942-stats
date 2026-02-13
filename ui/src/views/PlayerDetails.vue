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
    return `${remainingMinutes} minutes`;
  } else if (hours === 1) {
    return `${hours} hour ${remainingMinutes} minutes`;
  } else {
    return `${hours} hours ${remainingMinutes} minutes`;
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
  if (rank === 1) return 'text-amber-400 font-bold';
  if (rank === 2) return 'text-neutral-300 font-bold';
  if (rank === 3) return 'text-orange-400 font-bold';
  if (rank <= 10) return 'text-cyan-400 font-semibold';
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
  <!-- Full-width Hero Section (slim) -->
  <div class="w-full rounded-lg border border-[var(--portal-border)] bg-[var(--portal-surface)] mb-3">
    <div class="w-full px-2 sm:px-4 lg:px-6 py-2.5">
      <div class="flex flex-wrap items-center gap-2 lg:gap-3">
        <HeroBackButton fallback-route="/players" />

        <!-- Player Avatar (compact) -->
        <div class="relative group cursor-pointer flex-shrink-0" @click="showLastOnline = !showLastOnline">
          <div class="w-8 h-8 rounded-full bg-neutral-800 border border-neutral-700 flex items-center justify-center text-sm font-bold text-neutral-200"
               :class="playerStats?.isActive ? 'ring-2 ring-green-500/50' : ''">
            {{ playerName?.charAt(0)?.toUpperCase() || '?' }}
          </div>
          <div class="absolute -bottom-0.5 -right-0.5 w-3 h-3 rounded-full border-2 border-[var(--portal-surface)]"
               :class="playerStats?.isActive ? 'bg-green-500' : 'bg-neutral-600'" />
          <!-- Last Online Tooltip -->
          <div
            v-if="showLastOnline"
            class="absolute top-full left-1/2 transform -translate-x-1/2 mt-2 bg-neutral-950 border border-neutral-700 rounded-lg px-3 py-2 text-xs text-neutral-300 whitespace-nowrap z-50 shadow-lg"
          >
            <div class="flex items-center gap-2">
              <div class="w-2 h-2 rounded-full" :class="playerStats?.isActive ? 'bg-green-400' : 'bg-neutral-500'" />
              <span>
                {{ playerStats?.isActive ? 'Currently Online' : `Last online: ${formatRelativeTime(playerStats?.lastPlayed || '')}` }}
              </span>
            </div>
            <div class="absolute -top-1 left-1/2 transform -translate-x-1/2 w-2 h-2 bg-neutral-950 border-l border-t border-neutral-700 rotate-45" />
          </div>
        </div>

        <h1 class="text-base md:text-lg font-semibold text-neutral-200 truncate max-w-full lg:max-w-[34rem]">
          {{ playerName }}
        </h1>

        <PlayerAchievementHeroBadges
          :player-name="playerName"
        />

        <!-- K/D badge with hover trends -->
        <div class="relative" @mouseenter="enterTrendChartArea" @mouseleave="leaveTrendChartArea">
          <div class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded border border-neutral-700 bg-neutral-900 text-[11px] text-neutral-300 cursor-pointer hover:border-neutral-600 transition-colors">
            <span class="font-semibold text-neutral-200">{{ calculateKDR(playerStats?.totalKills || 0, playerStats?.totalDeaths || 0) }}</span>
            <span class="text-neutral-500">K/D</span>
            <span class="text-neutral-600 mx-0.5">|</span>
            <span class="text-green-400 font-medium">{{ playerStats?.totalKills?.toLocaleString() }}</span>
            <span class="text-neutral-600">/</span>
            <span class="text-red-400 font-medium">{{ playerStats?.totalDeaths?.toLocaleString() }}</span>
          </div>

          <!-- Trend Charts - Show on Hover -->
          <div
            v-if="showTrendCharts"
            class="absolute left-0 top-full mt-2 bg-neutral-950 border border-neutral-700 rounded-lg p-3 w-80 z-50 shadow-2xl transition-all duration-200"
            @mouseenter="enterTrendChartArea"
            @mouseleave="leaveTrendChartArea"
          >
            <div class="space-y-2">
              <div class="space-y-1">
                <div class="text-xs font-semibold text-neutral-300">Kill Rate Trend</div>
                <div class="h-10 -mx-1 trend-chart-container">
                  <Line
                    :data="killRateTrendChartData"
                    :options="microChartOptions"
                  />
                </div>
              </div>
              <div class="space-y-1">
                <div class="text-xs font-semibold text-neutral-300">K/D Trend</div>
                <div class="h-10 -mx-1 trend-chart-container">
                  <Line
                    :data="kdRatioTrendChartData"
                    :options="microChartOptions"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Playtime badge -->
        <div class="inline-flex items-center gap-1 px-2 py-0.5 rounded border border-neutral-700 bg-neutral-900 text-[11px] text-neutral-300">
          <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-neutral-400"><circle cx="12" cy="12" r="10"/><polyline points="12,6 12,12 16,14"/></svg>
          <span>{{ formatPlayTime(playerStats?.totalPlayTimeMinutes || 0) }}</span>
        </div>

        <!-- Last played badge -->
        <div class="inline-flex items-center gap-1 px-2 py-0.5 rounded border border-neutral-700 bg-neutral-900 text-[11px] text-neutral-300">
          <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-green-400"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22,4 12,14.01 9,11.01"/></svg>
          <span>{{ formatRelativeTime(playerStats?.lastPlayed || '') }}</span>
        </div>

        <!-- Compare Player button -->
        <router-link
          :to="{ path: '/players/compare', query: { player1: playerName } }"
          class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded border border-neutral-600 bg-neutral-200 text-neutral-900 text-[11px] font-semibold hover:bg-neutral-100 transition-colors"
          title="Compare this player with another"
        >
          <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 3h12l4 6-10 13L2 9l4-6z"/><path d="M11 3 8 9l4 13 4-13-3-6"/></svg>
          Compare
        </router-link>

        <div
          v-if="playerStats?.recentSessions && playerStats.recentSessions.length > 0"
          class="ml-auto max-w-full"
        >
          <PlayerRecentRoundsCompact
            :sessions="playerStats.recentSessions"
            :player-name="playerName"
          />
        </div>
      </div>
    </div>
  </div>

  <!-- Main Content Area: flex row on lg when map stats panel is open for side-by-side layout -->
  <div class="min-h-screen bg-neutral-950">
    <div
      class="relative flex flex-col min-h-0"
      :class="{ 'lg:flex-row': isMapStatsPanelOpen && playerStats?.servers }"
    >
      <div class="flex-1 min-w-0">
        <div class="relative">
          <div class="w-full px-2 sm:px-6 lg:px-12 py-6">
        <!-- Loading State -->
        <div
          v-if="isLoading"
          class="flex flex-col items-center justify-center min-h-[60vh] space-y-6"
        >
          <div class="relative">
            <div class="w-20 h-20 border-4 border-neutral-700 rounded-full animate-spin">
              <div class="absolute top-0 left-0 w-20 h-20 border-4 border-neutral-400 rounded-full border-t-transparent animate-spin" />
            </div>
            <div class="absolute inset-0 flex items-center justify-center">
              <div class="w-8 h-8 border-2 border-neutral-500 border-t-neutral-300 rounded-full animate-spin" />
            </div>
          </div>
          <div class="text-center space-y-2">
            <p class="text-xl font-semibold text-neutral-300">
              Loading Player Statistics...
            </p>
            <p class="text-neutral-500">
              Analyzing battlefield performance data
            </p>
          </div>
        </div>

        <!-- Error State -->
        <div
          v-else-if="error"
          class="flex flex-col items-center justify-center min-h-[60vh] space-y-6"
        >
          <div class="w-20 h-20 bg-red-500/20 rounded-full flex items-center justify-center">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="32"
              height="32"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              class="text-red-400"
            >
              <circle
                cx="12"
                cy="12"
                r="10"
              />
              <line
                x1="15"
                y1="9"
                x2="9"
                y2="15"
              />
              <line
                x1="9"
                y1="9"
                x2="15"
                y2="15"
              />
            </svg>
          </div>
          <div class="text-center space-y-2">
            <p class="text-xl font-semibold text-red-400">
              {{ error }}
            </p>
            <p class="text-neutral-500">
              Unable to load player data
            </p>
          </div>
        </div>

        <!-- Main Content -->
        <div
          v-else-if="playerStats"
          class="w-full px-1 sm:px-4 pb-6 sm:pb-12 space-y-4 sm:space-y-8"
        >

          <!-- Data Explorer Player Breakdown -->
          <div class="bg-neutral-900/80 border border-neutral-700/50 rounded-xl overflow-hidden">
            <div class="px-3 sm:px-6 py-4 border-b border-neutral-700/50">
              <h3 class="text-xl font-semibold text-neutral-200">
                Data Explorer Breakdown
              </h3>
              <p class="text-sm text-neutral-400 mt-1">
                Expanded map and server slicing for this player.
              </p>
            </div>
            <PlayerDetailPanel
              :player-name="playerName"
              :game="playerPanelGame"
            />
          </div>

          <!-- Servers – Unified list sorted by ranking -->
          <div
            v-if="unifiedServerList.length > 0"
            class="bg-neutral-900/80 border border-neutral-700/50 rounded-xl overflow-hidden"
          >
            <!-- Header -->
            <div class="px-3 sm:px-6 py-4 border-b border-neutral-700/50 flex items-center justify-between">
              <h3 class="text-xl font-semibold text-neutral-200">
                Servers
              </h3>
              <div class="flex items-center gap-3">
                <div class="hidden sm:flex items-center gap-2 text-xs text-neutral-500">
                  <span v-if="rankingsSummary?.numOnes" class="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-amber-500/10 border border-amber-500/20 text-amber-400 font-semibold">
                    #1 on {{ rankingsSummary.numOnes }}
                  </span>
                  <span v-if="rankingsSummary?.numTop10 && rankingsSummary.numTop10 !== rankingsSummary?.numOnes" class="text-neutral-400">
                    Top 10 on {{ rankingsSummary.numTop10 }}
                  </span>
                </div>
                <button
                  type="button"
                  class="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg border border-neutral-600 text-neutral-300 hover:bg-neutral-700 hover:text-neutral-100 hover:border-neutral-500 transition-colors"
                  @click="showAllServerMaps"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/></svg>
                  All Map Rankings
                </button>
              </div>
            </div>

            <!-- Server List -->
            <div class="divide-y divide-neutral-800/60">
              <div
                v-for="server in unifiedServerList"
                :key="server.serverGuid"
                class="group flex items-center gap-3 sm:gap-4 px-3 sm:px-6 py-3 hover:bg-neutral-800/40 transition-colors cursor-pointer"
                @click="showServerMapStats(server.serverGuid)"
              >
                <!-- Rank Badge -->
                <div class="flex-shrink-0 w-10 text-center">
                  <div
                    v-if="server.ranking"
                    class="text-sm"
                    :class="getRankBadgeClass(rankNum(server.ranking))"
                  >
                    #{{ server.ranking.rankDisplay ?? server.ranking.rank }}
                  </div>
                  <div v-else class="text-xs text-neutral-600">—</div>
                </div>

                <!-- Game Icon -->
                <div v-if="server.gameId" class="flex-shrink-0 w-8 h-8 rounded bg-neutral-700/80 flex items-center justify-center p-1">
                  <img :src="getGameIcon(server.gameId)" alt="" class="w-full h-full rounded object-cover" />
                </div>

                <!-- Server Name + Rank Details -->
                <div class="flex-1 min-w-0">
                  <div class="text-sm font-medium text-neutral-200 truncate group-hover:text-cyan-400 transition-colors">
                    {{ server.serverName }}
                  </div>
                  <div class="flex items-center gap-2 text-xs text-neutral-500 mt-0.5">
                    <span v-if="server.ranking">of {{ server.ranking.totalRankedPlayers }} players</span>
                    <span
                      v-if="server.ranking?.averagePing > 0"
                      class="font-mono"
                      :class="{
                        'ping-good': server.ranking.averagePing < 50,
                        'ping-warning': server.ranking.averagePing >= 50 && server.ranking.averagePing < 100,
                        'ping-bad': server.ranking.averagePing >= 100
                      }"
                    >
                      {{ server.ranking.averagePing }}ms
                    </span>
                    <span v-if="server.hasStats && server.totalMinutes > 0" class="text-neutral-600">{{ formatPlayTime(server.totalMinutes) }}</span>
                    <span v-if="!server.hasStats && server.ranking" class="text-neutral-500">{{ server.ranking.scoreDisplay || server.ranking.totalScore.toLocaleString() }} score</span>
                  </div>
                </div>

                <!-- Compact Stats (desktop) - only when we have full stats -->
                <div v-if="server.hasStats" class="hidden sm:flex items-center gap-5 text-xs flex-shrink-0">
                  <div class="text-center min-w-[3rem]">
                    <div class="font-mono font-semibold text-neutral-200">{{ Number(server.kdRatio).toFixed(2) }}</div>
                    <div class="text-neutral-500">K/D</div>
                  </div>
                  <div class="text-center min-w-[4.5rem]">
                    <div>
                      <span class="text-green-400 font-semibold">{{ server.totalKills.toLocaleString() }}</span>
                      <span class="text-neutral-600 mx-0.5">/</span>
                      <span class="text-red-400 font-semibold">{{ server.totalDeaths.toLocaleString() }}</span>
                    </div>
                    <div class="text-neutral-500">K / D</div>
                  </div>
                  <div class="text-center min-w-[2.5rem]">
                    <div class="font-mono text-neutral-300">{{ getPlaytimePercentage(server.totalMinutes).toFixed(0) }}%</div>
                    <div class="text-neutral-500">time</div>
                  </div>
                </div>
                <!-- Score display for ranking-only entries -->
                <div v-else-if="server.ranking" class="hidden sm:flex items-center gap-3 text-xs flex-shrink-0">
                  <div class="text-center">
                    <div class="font-mono font-semibold text-amber-400">{{ server.ranking.totalScore.toLocaleString() }}</div>
                    <div class="text-neutral-500">score</div>
                  </div>
                </div>

                <!-- Drill-in Arrow -->
                <div class="flex-shrink-0 text-neutral-600 group-hover:text-cyan-400 transition-colors">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>
                </div>
              </div>
            </div>
          </div>

          <!-- Player Achievements Section -->
          <div class="relative overflow-hidden bg-neutral-900/80 rounded-2xl border border-neutral-700/50">
            <!-- Background Effects (subtle dark theme accent) -->
            <div class="absolute inset-0 bg-gradient-to-r from-cyan-500/5 via-transparent to-transparent pointer-events-none" aria-hidden="true" />
            <div class="relative z-10 p-2 sm:p-6 lg:p-8 space-y-6">
              <!-- Section Header -->
              <div class="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
                <div class="space-y-2">
                  <h3 class="text-3xl font-bold text-neutral-200">
                    🏆 Achievements & Streaks
                  </h3>
                  <p class="text-neutral-400">
                    Unlock your battlefield legacy
                  </p>
                </div>
                <router-link
                  :to="`/players/${encodeURIComponent(playerName)}/achievements`"
                  class="group inline-flex items-center gap-3 px-6 py-3 text-sm font-bold text-neutral-200 hover:text-cyan-400 bg-neutral-800 border border-neutral-600 hover:border-cyan-500/50 hover:bg-neutral-700/80 rounded-xl transition-all duration-300 transform hover:scale-[1.02] shadow-lg hover:shadow-cyan-500/10"
                >
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    width="18"
                    height="18"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    class="group-hover:rotate-12 transition-transform duration-300"
                  >
                    <path d="M6 9H4.5a2.5 2.5 0 0 1 0-5H6" />
                    <path d="M18 9h1.5a2.5 2.5 0 0 0 0-5H18" />
                    <path d="M4 22h16" />
                    <path d="M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22" />
                    <path d="M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22" />
                    <path d="M18 2H6v7a6 6 0 0 0 12 0V2Z" />
                  </svg>
                  View All Achievements
                </router-link>
              </div>

              <PlayerAchievementSummary
                :player-name="playerName"
                :achievement-groups="achievementGroups"
                :loading="achievementGroupsLoading"
                :error="achievementGroupsError"
              />

              <!-- Best Scores integrated into achievements -->
              <div
                v-if="playerStats?.bestScores && (playerStats.bestScores.allTime?.length > 0 || playerStats.bestScores.last30Days?.length > 0 || playerStats.bestScores.thisWeek?.length > 0)"
                class="rounded-xl border border-neutral-700/60 bg-neutral-950/60 p-3 sm:p-4 space-y-3"
              >
                <div class="flex flex-wrap items-center justify-between gap-3">
                  <h4 class="text-lg font-semibold text-neutral-200">Best Scores</h4>
                  <div class="flex gap-2 bg-neutral-900/60 rounded-lg p-1 border border-neutral-700/50">
                    <button
                      v-for="tab in bestScoresTabOptions"
                      :key="tab.key"
                      class="px-3 py-1 text-xs font-medium rounded transition-colors duration-200"
                      :class="{
                        'bg-neutral-700 text-neutral-100': selectedBestScoresTab === tab.key,
                        'text-neutral-400 hover:text-neutral-300': selectedBestScoresTab !== tab.key
                      }"
                      @click="changeBestScoresTab(tab.key)"
                    >
                      {{ tab.label }}
                    </button>
                  </div>
                </div>

                <div v-if="currentBestScores.length === 0" class="py-3 text-sm text-center text-neutral-400">
                  No scores recorded for this period
                </div>

                <div v-else class="best-scores-scroll-container space-y-2">
                  <div
                    v-for="(score, index) in currentBestScores.slice(0, 5)"
                    :key="`${score.roundId}-${index}`"
                    class="p-3 bg-neutral-800/40 hover:bg-neutral-800/60 rounded-lg border border-neutral-700/30 hover:border-neutral-700/60 transition-colors duration-200 cursor-pointer group"
                    @click="navigateToRoundReport(score.roundId)"
                  >
                    <div class="flex items-center gap-3">
                      <div class="flex-shrink-0 w-6 h-6 bg-neutral-700 rounded-full flex items-center justify-center font-bold text-sm text-neutral-200">
                        {{ index + 1 }}
                      </div>
                      <div class="min-w-0 flex-1">
                        <div class="text-sm text-neutral-200 font-medium truncate">
                          {{ score.score.toLocaleString() }} pts - {{ score.mapName }}
                        </div>
                        <div class="text-xs text-neutral-500 truncate">
                          {{ score.serverName }} • {{ score.kills }}/{{ score.deaths }} • K/D {{ calculateKDR(score.kills, score.deaths) }}
                        </div>
                      </div>
                      <div class="flex-shrink-0 text-xs text-neutral-500 text-right">
                        {{ formatRelativeTime(score.timestamp) }}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>

        </div>
      </div>
    </div>
    </div>

      <!-- Server Map Statistics Panel: overlay on mobile, side-by-side on lg -->
      <template v-if="isMapStatsPanelOpen && playerStats?.servers">
      <div
        class="fixed inset-0 bg-black/20 backdrop-blur-sm z-[100] lg:hidden"
        aria-hidden="true"
        @click="closeServerMapStats"
      ></div>
      <div
        class="fixed inset-y-0 left-0 right-0 md:right-20 z-[100] flex items-stretch lg:relative lg:inset-auto lg:z-auto lg:w-[560px] xl:w-[620px] 2xl:w-[700px] lg:flex-shrink-0 lg:min-h-0 lg:border-l lg:border-neutral-800"
        @click.stop
      >
        <div
          class="bg-neutral-950 w-full max-w-6xl lg:max-w-none shadow-2xl animate-slide-in-left overflow-hidden flex flex-col border-r border-neutral-800 lg:border-r-0"
          :class="{ 'h-[calc(100vh-4rem)]': true, 'md:h-full': true, 'mt-16': true, 'md:mt-0': true }"
        >
          <!-- Header -->
          <div class="sticky top-0 z-20 bg-neutral-950/95 border-b border-neutral-800 p-2 sm:p-4 flex justify-between items-center">
            <div class="flex flex-col min-w-0 flex-1 mr-4">
              <h2 class="text-xl font-bold text-neutral-200 truncate">
                {{ rankingsMapName ? `Rankings: ${rankingsMapName}` : 'Map Rankings' }}
              </h2>
              <p class="text-sm text-neutral-400 mt-1 truncate">
                {{ selectedServerName || 'Selected Server' }}
              </p>
            </div>
            <button 
              class="group p-2 text-neutral-400 hover:text-white hover:bg-red-500/20 border border-neutral-600 hover:border-red-500/50 rounded-lg transition-all duration-300 flex items-center justify-center w-10 h-10 flex-shrink-0"
              title="Close panel"
              @click="closeServerMapStats"
            >
              <svg
                xmlns="http://www.w3.org/2000/svg"
                width="20"
                height="20"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
                class="group-hover:text-red-400"
              >
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
              </svg>
            </button>
          </div>

          <!-- Content -->
          <div class="flex-1 min-h-0 overflow-y-auto">
            <!-- Rankings Drill-Down View -->
            <div v-if="rankingsMapName" class="p-2 sm:p-4">
              <button
                class="flex items-center gap-1.5 mb-3 px-2 py-1 text-xs font-medium text-neutral-400 hover:text-neutral-200 hover:bg-neutral-800 rounded transition-colors"
                @click="closeRankingsPanel"
              >
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m15 18-6-6 6-6"/></svg>
                Back to Map Stats
              </button>
              <MapRankingsPanel
                :map-name="rankingsMapName"
                :server-guid="rankingsServerGuid ?? undefined"
                :highlight-player="playerName"
                :game="(effectiveServerGuid ? playerStats?.servers?.find(s => s.serverGuid === effectiveServerGuid)?.gameId as any : undefined) || 'bf1942'"
              />
            </div>
            <!-- Map Stats View -->
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
    </template>
    </div>
  </div>
    </div>
  </div>
</template>

<style src="./portal-layout.css"></style>
<style scoped src="./PlayerDetails.vue.css"></style>
