<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, computed, nextTick } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ServerDetails, ServerInsights, LeaderboardsData, fetchServerDetails, fetchServerInsights, fetchServerLeaderboards, fetchLiveServerData, ServerBusyIndicator, ServerHourlyTimelineEntry, fetchServerBusyIndicators } from '../services/serverDetailsService';
import { fetchServerMapRotation, type MapRotationItem } from '../services/dataExplorerService';
import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, BarElement, Title, Tooltip, Legend, Filler } from 'chart.js';
import { countryCodeToName } from '../types/countryCodes';
import { ServerSummary } from '../types/server';
import PlayersPanel from '../components/PlayersPanel.vue';
import PlayerHistoryChart from '../components/PlayerHistoryChart.vue';
import ServerLeaderboards from '../components/ServerLeaderboards.vue';
import RecentSessionsList from '../components/data-explorer/RecentSessionsList.vue';
import MapRotationTable from '../components/data-explorer/MapRotationTable.vue';
import ServerMapDetailPanel from '../components/data-explorer/ServerMapDetailPanel.vue';
import MapRankingsPanel from '../components/MapRankingsPanel.vue';
import { formatDate } from '../utils/date';
import HeroBackButton from '../components/HeroBackButton.vue';
import ForecastModal from '../components/ForecastModal.vue';
import discordIcon from '@/assets/discord.webp';
import { useAIContext } from '@/composables/useAIContext';

// Register Chart.js components
ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, BarElement, Title, Tooltip, Legend, Filler);

const route = useRoute();
const router = useRouter();

// AI Context
const { setContext, clearContext } = useAIContext();

// State
const serverName = ref(route.params.serverName as string);
const serverDetails = ref<ServerDetails | null>(null);
const serverInsights = ref<ServerInsights | null>(null);
const leaderboardsData = ref<LeaderboardsData | null>(null);
const liveServerInfo = ref<ServerSummary | null>(null);
const isLoading = ref(true);
const isInsightsLoading = ref(true);
const isLeaderboardsLoading = ref(true);
const isLiveServerLoading = ref(false);
const error = ref<string | null>(null);
const insightsError = ref<string | null>(null);
const leaderboardsError = ref<string | null>(null);
const liveServerError = ref<string | null>(null);
const showPlayersModal = ref(false);
const currentLeaderboardPeriod = ref<'week' | 'month' | 'alltime'>('week');
const minPlayersForWeighting = ref(15);
const minRoundsForKillBoards = ref(20);
const historyRollingWindow = ref('7d');
const historyPeriod = ref<'1d' | '3d' | '7d' | 'longer'>('7d');
const longerPeriod = ref<'1month' | '3months' | 'thisyear' | 'alltime'>('1month');
const showLongerDropdown = ref(false);
const showPlayerHistory = ref(true);
const hasLoadedPlayerHistory = ref(false);

// Maps state
const mapRotation = ref<MapRotationItem[]>([]);
const mapRotationPage = ref(1);
const mapRotationPageSize = ref(10);
const mapRotationTotalCount = ref(0);
const mapRotationTotalPages = computed(() => Math.max(1, Math.ceil(mapRotationTotalCount.value / mapRotationPageSize.value)));
const isMapsLoading = ref(false);
const mapsError = ref<string | null>(null);
const hasLoadedMaps = ref(false);
const showMapRotation = ref(true);

// Server-map detail panel state
const selectedMapName = ref<string | null>(null);
const showMapDetailPanel = ref(false);

// Rankings drill-down panel state (nested inside map detail panel)
const showRankingsInPanel = ref(false);
const rankingsMapNameForPanel = ref<string | null>(null);

// Busy indicator state
const serverBusyIndicator = ref<ServerBusyIndicator | null>(null);
const serverHourlyTimeline = ref<ServerHourlyTimelineEntry[]>([]);
const isBusyIndicatorLoading = ref(false);
const busyIndicatorError = ref<string | null>(null);
const showForecastOverlay = ref(false);

// Wide viewport: show slide-out panels side-by-side (lg: 1024px+)
const isWideScreen = ref(false);
const updateWideScreen = () => {
  isWideScreen.value = typeof window !== 'undefined' && window.innerWidth >= 1024;
};

// Fetch live server data asynchronously (non-blocking)
const fetchLiveServerDataAsync = async () => {
  if (!serverDetails.value?.serverIp || !serverDetails.value?.serverPort) return;

  isLiveServerLoading.value = true;
  liveServerError.value = null;

  try {
    // Use gameId from server details API response, fallback to guessing from server name
    const gameId = serverDetails.value.gameId || 
      (serverName.value.toLowerCase().includes('fh2') ? 'fh2' : 
       serverName.value.toLowerCase().includes('vietnam') || serverName.value.toLowerCase().includes('bfv') ? 'bfvietnam' : 'bf1942');
    
    liveServerInfo.value = await fetchLiveServerData(
      gameId,
      serverDetails.value.serverIp,
      serverDetails.value.serverPort
    );
  } catch (err) {
    console.error('Error fetching live server data:', err);
    liveServerError.value = 'Failed to load current server info.';
  } finally {
    isLiveServerLoading.value = false;
  }
};

// Fetch busy indicator data for the server
const fetchBusyIndicatorData = async () => {
  if (!serverDetails.value?.serverGuid) return;

  isBusyIndicatorLoading.value = true;
  busyIndicatorError.value = null;

  try {
    const response = await fetchServerBusyIndicators([serverDetails.value.serverGuid]);
    if (response.serverResults.length > 0) {
      const result = response.serverResults[0];
      serverBusyIndicator.value = result.busyIndicator;
      serverHourlyTimeline.value = result.hourlyTimeline;
    }
  } catch (err) {
    console.error('Error fetching busy indicator data:', err);
    busyIndicatorError.value = 'Failed to load server activity forecast.';
  } finally {
    isBusyIndicatorLoading.value = false;
  }
};

// Fetch server details, insights, and leaderboards in parallel
const fetchData = async () => {
  if (!serverName.value) return;

  // Set AI context immediately with server name from route so chat shows correct context before fetch
  setContext({
    pageType: 'server',
    serverName: serverName.value,
    game: 'bf1942'
  });

  isLoading.value = true;
  error.value = null;

  try {
    // Fetch server details first (blocks UI)
    serverDetails.value = await fetchServerDetails(serverName.value);

    // Update AI context with serverGuid once we have it (API uses serverGuid)
    setContext({
      pageType: 'server',
      serverGuid: serverDetails.value?.serverGuid,
      serverName: serverName.value,
      game: serverDetails.value?.gameId || 'bf1942'
    });

    // Fetch live server data and busy indicator data asynchronously after server details are loaded
    fetchLiveServerDataAsync();
    fetchBusyIndicatorData();

    // Now fetch leaderboards (non-blocking)
    // Player history and maps will be loaded when user expands those sections
    fetchLeaderboardsAsync();

    // Fetch insights if player history is shown by default
    if (showPlayerHistory.value) {
      hasLoadedPlayerHistory.value = true;
      fetchInsightsAsync();
    }
  } catch (err) {
    console.error('Error fetching server details:', err);
    error.value = 'Failed to load server details. Please try again later.';
  } finally {
    isLoading.value = false;
  }
};

// Fetch insights asynchronously (non-blocking)
const fetchInsightsAsync = async () => {
  isInsightsLoading.value = true;
  insightsError.value = null;

  try {
    const days = getPeriodInDays();
    serverInsights.value = await fetchServerInsights(serverName.value, days, historyRollingWindow.value);
  } catch (err) {
    console.error('Error fetching server insights:', err);
    insightsError.value = 'Failed to load server insights.';
  } finally {
    isInsightsLoading.value = false;
  }
};

// Fetch leaderboards asynchronously (non-blocking)
const fetchLeaderboardsAsync = async () => {
  isLeaderboardsLoading.value = true;
  leaderboardsError.value = null;

  try {
    leaderboardsData.value = await fetchServerLeaderboards(
      serverName.value,
      currentLeaderboardPeriod.value,
      minPlayersForWeighting.value,
      minRoundsForKillBoards.value
    );
  } catch (err) {
    console.error('Error fetching server leaderboards:', err);
    leaderboardsError.value = 'Failed to load server leaderboards.';
  } finally {
    isLeaderboardsLoading.value = false;
  }
};

// Fetch map rotation data asynchronously (non-blocking)
const fetchMapRotationAsync = async (page: number = 1) => {
  if (!serverDetails.value?.serverGuid) return;

  isMapsLoading.value = true;
  mapsError.value = null;

  try {
    const response = await fetchServerMapRotation(
      serverDetails.value.serverGuid,
      page,
      mapRotationPageSize.value
    );
    mapRotation.value = response.maps;
    mapRotationPage.value = response.page;
    mapRotationTotalCount.value = response.totalCount;
  } catch (err) {
    console.error('Error fetching server map rotation:', err);
    mapsError.value = 'Failed to load map rotation.';
  } finally {
    isMapsLoading.value = false;
  }
};

// Handle map rotation page change
const handleMapRotationPageChange = (page: number) => {
  if (page >= 1 && page <= mapRotationTotalPages.value) {
    fetchMapRotationAsync(page);
  }
};

watch(
  () => route.params.serverName,
  (newName, oldName) => {
    if (newName !== oldName) {
      serverName.value = newName as string;
      fetchData();
    }
  }
);

watch(
  () => serverDetails.value?.serverGuid,
  (guid) => {
    if (guid && !hasLoadedMaps.value) {
      hasLoadedMaps.value = true;
      fetchMapRotationAsync();
    }
  },
  { immediate: true }
);

onMounted(() => {
  fetchData();
  updateWideScreen();
  window.addEventListener('resize', updateWideScreen);
});

onUnmounted(() => {
  window.removeEventListener('resize', updateWideScreen);
  clearContext();
});


// Helper to get current time and UTC offset for a timezone string
function getTimezoneDisplay(timezone: string | undefined): string | null {
  if (!timezone) return null;
  try {
    const now = new Date();
    // Get current time in the timezone
    const time = new Intl.DateTimeFormat(undefined, {
      hour: '2-digit', minute: '2-digit', timeZone: timezone
    }).format(now);
    // Get UTC offset in hours
    const tzDate = new Date(now.toLocaleString('en-US', { timeZone: timezone }));
    const offsetMinutes = (tzDate.getTime() - now.getTime()) / 60000;
    const offsetHours = Math.round(offsetMinutes / 60);
    const sign = offsetHours >= 0 ? '+' : '-';
    return `${time} (${sign}${Math.abs(offsetHours)})`;
  } catch {
    return timezone;
  }
}

// Helper to get full country name from code
function getCountryName(code: string | undefined, fallback: string | undefined): string | undefined {
  if (!code) return fallback;
  const name = countryCodeToName[code.toUpperCase()];
  return name || fallback;
}

// Helper to get the correct servers route based on gameId
const getServersRoute = (gameId?: string): string => {
  if (!gameId) return '/servers';
  
  const normalizedGameId = gameId.toLowerCase();
  switch (normalizedGameId) {
    case 'fh2':
      return '/servers/fh2';
    case 'bfv':
    case 'bfvietnam':
      return '/servers/bfv';
    case 'bf1942':
    case '42':
    default:
      return '/servers/bf1942';
  }
};

// Join server function
const joinServer = () => {
  if (!liveServerInfo.value?.joinLink) return;
  
  const newWindow = window.open(liveServerInfo.value.joinLink, '_blank', 'noopener,noreferrer');
  if (newWindow) {
    newWindow.blur();
    window.focus();
  }
};

// Players modal functions
const openPlayersModal = () => {
  if (!liveServerInfo.value) return;
  showMapDetailPanel.value = false;
  showPlayersModal.value = true;
};

const closePlayersModal = () => {
  showPlayersModal.value = false;
};

// Handle leaderboard period change
const handleLeaderboardPeriodChange = async (period: 'week' | 'month' | 'alltime') => {
  if (period === currentLeaderboardPeriod.value) return;

  currentLeaderboardPeriod.value = period;
  isLeaderboardsLoading.value = true;
  leaderboardsError.value = null;

  try {
    leaderboardsData.value = await fetchServerLeaderboards(
      serverName.value,
      period,
      minPlayersForWeighting.value,
      minRoundsForKillBoards.value
    );
  } catch (err) {
    console.error('Error fetching leaderboards for period:', period, err);
    leaderboardsError.value = 'Failed to load leaderboards for selected period.';
  } finally {
    isLeaderboardsLoading.value = false;
  }
};

// Handle min players for weighting update
const handleMinPlayersUpdate = async (value: number) => {
  minPlayersForWeighting.value = value;

  // Refetch leaderboards with new min players value
  isLeaderboardsLoading.value = true;
  leaderboardsError.value = null;

  try {
    leaderboardsData.value = await fetchServerLeaderboards(
      serverName.value,
      currentLeaderboardPeriod.value,
      value,
      minRoundsForKillBoards.value
    );
  } catch (err) {
    console.error('Error refreshing leaderboards with new min players:', err);
    leaderboardsError.value = 'Failed to refresh leaderboards.';
  } finally {
    isLeaderboardsLoading.value = false;
  }
};

// Handle min rounds for kill boards update
const handleMinRoundsUpdate = async (value: number) => {
  minRoundsForKillBoards.value = value;

  // Refetch leaderboards with new min rounds value
  isLeaderboardsLoading.value = true;
  leaderboardsError.value = null;

  try {
    leaderboardsData.value = await fetchServerLeaderboards(
      serverName.value,
      currentLeaderboardPeriod.value,
      minPlayersForWeighting.value,
      value
    );
  } catch (err) {
    console.error('Error refreshing leaderboards with new min rounds:', err);
    leaderboardsError.value = 'Failed to refresh leaderboards.';
  } finally {
    isLeaderboardsLoading.value = false;
  }
};

// Handle rolling window change for player history chart
const handleRollingWindowChange = async (rollingWindow: string) => {
  historyRollingWindow.value = rollingWindow;
  // Refetch insights with new rolling window - the API will recalculate rolling average
  await fetchInsightsAsync();
};

// Convert period string to days for API
const getPeriodInDays = (): number => {
  if (historyPeriod.value === 'longer') {
    switch (longerPeriod.value) {
      case '1month': return 30;
      case '3months': return 90;
      case 'thisyear': {
        const now = new Date();
        const startOfYear = new Date(now.getFullYear(), 0, 1);
        return Math.floor((now.getTime() - startOfYear.getTime()) / (1000 * 60 * 60 * 24));
      }
      case 'alltime': return 36500; // ~100 years
    }
  }

  switch (historyPeriod.value) {
    case '1d': return 1;
    case '3d': return 3;
    case '7d': return 7;
    default: return 7;
  }
};

// Handle period change
const handleHistoryPeriodChange = async (period: '1d' | '3d' | '7d') => {
  historyPeriod.value = period;
  showLongerDropdown.value = false;
  await fetchInsightsAsync();
};

// Handle longer period selection
const selectLongerPeriod = async (period: '1month' | '3months' | 'thisyear' | 'alltime') => {
  longerPeriod.value = period;
  historyPeriod.value = 'longer';
  showLongerDropdown.value = false;
  await fetchInsightsAsync();
};

// Toggle longer period dropdown
const toggleLongerDropdown = () => {
  showLongerDropdown.value = !showLongerDropdown.value;
};

// Get label for longer period button
const getLongerPeriodLabel = () => {
  if (historyPeriod.value !== 'longer') return 'More';
  const labels = {
    '1month': '1 Month',
    '3months': '3 Months',
    'thisyear': 'This Year',
    'alltime': 'All Time'
  };
  return labels[longerPeriod.value];
};

// Toggle player history visibility and fetch data on first expand
const togglePlayerHistory = () => {
  showPlayerHistory.value = !showPlayerHistory.value;

  // Fetch data on first expand
  if (showPlayerHistory.value && !hasLoadedPlayerHistory.value) {
    hasLoadedPlayerHistory.value = true;
    fetchInsightsAsync();
  }
};

// Toggle map rotation section (expand to show maps inline)
const toggleMapRotation = () => {
  showMapRotation.value = !showMapRotation.value;
  if (showMapRotation.value && !hasLoadedMaps.value) {
    hasLoadedMaps.value = true;
    fetchMapRotationAsync();
  }
};

// Handle map navigation from MapRotationTable - open same server-map detail panel as DataExplorer
const handleMapNavigate = (mapName: string) => {
  if (!serverDetails.value?.serverGuid) return;
  showPlayersModal.value = false;
  selectedMapName.value = mapName;
  showMapDetailPanel.value = true;
};

// Handle close map detail panel
const handleCloseMapDetailPanel = () => {
  showMapDetailPanel.value = false;
  selectedMapName.value = null;
  showRankingsInPanel.value = false;
  rankingsMapNameForPanel.value = null;
};

// Handle opening rankings from map detail panel
const handleOpenRankingsFromMap = (mapName: string) => {
  rankingsMapNameForPanel.value = mapName;
  showRankingsInPanel.value = true;
};

// Handle closing rankings back to map detail
const handleCloseRankingsInPanel = () => {
  showRankingsInPanel.value = false;
  rankingsMapNameForPanel.value = null;
};

// Handle navigation from map detail panel
const handleNavigateToServerFromMap = () => {
  // Already on server details page, just close the panel
  handleCloseMapDetailPanel();
};

const handleNavigateToMapFromMap = (mapName: string) => {
  // Navigate to map detail in Data Explorer
  router.push({
    name: 'explore-map-detail',
    params: { mapName }
  });
};

// Helper functions for mini forecast bars
const getMiniTimelineBarHeight = (entry: ServerHourlyTimelineEntry): number => {
  const timeline = serverHourlyTimeline.value || [];
  const maxTypical = Math.max(1, ...timeline.map(e => Math.max(0, e.typicalPlayers || 0)));
  const pct = Math.max(0, Math.min(1, (entry.typicalPlayers || 0) / maxTypical));
  const maxHeight = 20; // px for mini bars (h-6 = 24px container)
  const minHeight = 2;
  return Math.max(minHeight, Math.round(pct * maxHeight));
};

const formatTimelineTooltip = (entry: ServerHourlyTimelineEntry): string => {
  // Convert UTC hour to local "HH:00" display
  const now = new Date();
  const d = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), entry.hour, 0, 0));
  const local = d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  const levelLabel = getBusyLevelLabel(entry.busyLevel);
  return `${local} • Typical ${Math.round(entry.typicalPlayers)} • ${levelLabel}`;
};

const getBusyLevelLabel = (level: string): string => {
  switch (level) {
    case 'very_busy': return 'Very busy';
    case 'busy': return 'Busy';
    case 'moderate': return 'Moderate';
    case 'quiet': return 'Quiet';
    case 'very_quiet': return 'Very quiet';
    default: return 'Unknown';
  }
};

// Toggle forecast overlay for mobile
const toggleForecastOverlay = () => {
  showForecastOverlay.value = !showForecastOverlay.value;
};

// Close forecast overlay when clicking outside
const closeForecastOverlay = () => {
  showForecastOverlay.value = false;
};
</script>

<template>
  <div class="portal-page">
    <div class="portal-grid" aria-hidden="true" />
    <div class="portal-inner">
      <div class="data-explorer">
        <div class="explorer-inner">
          
          <!-- Loading State -->
          <div v-if="isLoading" class="flex flex-col items-center justify-center py-20 text-neutral-400" role="status" aria-label="Loading server profile">
            <div class="explorer-spinner mb-4" />
            <p class="text-lg text-neutral-300">Loading server profile...</p>
          </div>

          <!-- Error State -->
          <div v-else-if="error" class="explorer-card p-8 text-center" role="alert">
            <div class="flex items-center justify-center mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-red-400"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>
            </div>
            <p class="text-neon-red text-lg font-medium">{{ error }}</p>
          </div>

          <!-- Content -->
          <div v-else-if="serverDetails" class="space-y-6">
            
            <!-- Server Header Card -->
            <div class="explorer-card">
              <div class="explorer-card-body">
                <div class="flex flex-wrap items-center gap-3">
                  
                  <h1 class="text-xl md:text-2xl font-bold text-neon-cyan truncate max-w-full lg:max-w-[34rem] font-mono">
                    {{ serverName }}
                  </h1>

                  <div class="flex flex-wrap gap-2 items-center ml-auto">
                    <div v-if="serverDetails?.region" class="explorer-tag">{{ serverDetails.region }}</div>
                    <div v-if="serverDetails?.country || serverDetails?.countryCode" class="explorer-tag">
                      {{ getCountryName(serverDetails?.countryCode, serverDetails?.country) }}
                    </div>
                    
                    <!-- Discord & Forum Links -->
                    <a v-if="liveServerInfo?.discordUrl" :href="liveServerInfo.discordUrl" target="_blank" rel="noopener noreferrer" class="explorer-btn explorer-btn--ghost explorer-btn--sm" title="Join Discord">
                      <img :src="discordIcon" alt="Discord" class="w-3.5 h-3.5">
                    </a>
                    <a v-if="liveServerInfo?.forumUrl" :href="liveServerInfo.forumUrl" target="_blank" rel="noopener noreferrer" class="explorer-btn explorer-btn--ghost explorer-btn--sm" title="Visit Forum">
                      Forum
                    </a>

                    <!-- Online Players -->
                    <button
                      v-if="liveServerInfo"
                      type="button"
                      class="explorer-btn explorer-btn--sm flex items-center gap-2"
                      :class="liveServerInfo.players.length > 0 ? 'text-neon-green border-neon-green bg-green-900/20' : 'text-neutral-400 border-neutral-700'"
                      @click.stop="openPlayersModal"
                    >
                      <span class="w-2 h-2 rounded-full" :class="liveServerInfo.players.length > 0 ? 'bg-neon-green animate-pulse' : 'bg-neutral-500'"></span>
                      <span class="font-bold">{{ liveServerInfo.numPlayers }}</span>
                      <span>online</span>
                    </button>

                    <!-- Join Button -->
                    <button
                      v-if="liveServerInfo?.joinLink"
                      class="explorer-btn explorer-btn--primary explorer-btn--sm"
                      @click="joinServer"
                    >
                      JOIN SERVER
                    </button>
                  </div>
                </div>

                <!-- Forecast & Data Info -->
                <div class="flex items-center justify-between mt-4 pt-4 border-t border-[var(--border-color)]">
                  <div class="text-[10px] text-neutral-500 font-mono">
                    Data {{ formatDate(serverDetails.startPeriod) }} - {{ formatDate(serverDetails.endPeriod) }}
                  </div>

                  <!-- Forecast Mini Chart -->
                  <button
                    v-if="serverBusyIndicator && serverHourlyTimeline.length > 0"
                    type="button"
                    class="inline-flex items-end gap-0.5 px-2 py-1 rounded border border-[var(--border-color)] bg-[var(--bg-panel)] group/forecast"
                    @click.stop="toggleForecastOverlay"
                  >
                    <span class="text-[10px] text-neutral-500 mr-1 hidden sm:inline font-mono uppercase">Forecast</span>
                    <span
                      v-for="(entry, index) in serverHourlyTimeline"
                      :key="index"
                      class="w-1 rounded-t"
                      :class="entry.isCurrentHour ? 'bg-neon-cyan' : 'bg-neutral-700'"
                      :style="{ height: getMiniTimelineBarHeight(entry) + 'px' }"
                      :title="formatTimelineTooltip(entry)"
                    />
                    <ForecastModal
                      :show-overlay="true"
                      :show-modal="showForecastOverlay"
                      :hourly-timeline="serverHourlyTimeline"
                      :current-status="`${serverBusyIndicator.currentPlayers} players (typical: ${Math.round(serverBusyIndicator.typicalPlayers)})`"
                      :current-players="serverBusyIndicator.currentPlayers"
                      overlay-class="opacity-0 group-hover/forecast:opacity-100"
                      @close="closeForecastOverlay"
                    />
                  </button>
                </div>
              </div>
            </div>

            <!-- Main Grid Layout -->
            <div class="grid grid-cols-1 xl:grid-cols-12 gap-6">
              
              <!-- Left Column -->
              <div class="xl:col-span-6 space-y-6">
                
                <!-- Recent Sessions -->
                <div class="explorer-card">
                  <div class="explorer-card-header flex items-center justify-between">
                    <h3 class="explorer-card-title">RECENT SESSIONS</h3>
                    <router-link
                      :to="`/servers/${encodeURIComponent(serverName)}/sessions`"
                      class="explorer-link text-xs font-mono uppercase"
                    >
                      View All &rarr;
                    </router-link>
                  </div>
                  <div class="explorer-card-body p-0">
                    <RecentSessionsList
                      v-if="serverDetails?.serverGuid"
                      :server-guid="serverDetails.serverGuid"
                      :server-name="serverName"
                      :limit="5"
                      :initial-visible-count="2"
                      empty-message="No recent sessions found"
                    />
                  </div>
                </div>

                <!-- Leaderboards -->
                <div class="explorer-card">
                  <div class="explorer-card-header">
                    <h3 class="explorer-card-title">LEADERBOARDS</h3>
                  </div>
                  <div class="explorer-card-body">
                    <ServerLeaderboards
                      :leaderboards-data="leaderboardsData"
                      :is-loading="isLeaderboardsLoading"
                      :error="leaderboardsError"
                      :server-name="serverName"
                      :server-guid="serverDetails.serverGuid"
                      :min-players-for-weighting="minPlayersForWeighting"
                      :min-rounds-for-kill-boards="minRoundsForKillBoards"
                      @update-min-players-for-weighting="handleMinPlayersUpdate"
                      @update-min-rounds-for-kill-boards="handleMinRoundsUpdate"
                      @period-change="handleLeaderboardPeriodChange"
                    />
                  </div>
                </div>

              </div>

              <!-- Right Column -->
              <div class="xl:col-span-6 space-y-6">
                
                <!-- Map Rotation -->
                <div class="explorer-card">
                  <div 
                    class="explorer-card-header flex items-center justify-between cursor-pointer hover:bg-white/5 transition-colors"
                    @click="toggleMapRotation"
                  >
                    <div class="flex items-center gap-3">
                      <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-orange-400 flex-shrink-0"><path d="m3 6 6-3 6 3 6-3v15l-6 3-6-3-6 3z"/><path d="M9 3v15"/><path d="M15 6v15"/></svg>
                      <div>
                        <h3 class="explorer-card-title">MAP ROTATION</h3>
                        <div class="text-[10px] text-neutral-500 font-mono mt-0.5">TOP MAP WINNERS</div>
                      </div>
                    </div>
                    <div class="explorer-toggle-btn" :class="{ 'rotate-180': showMapRotation }">
                      ▼
                    </div>
                  </div>
                  
                  <div v-if="showMapRotation" class="explorer-card-body border-t border-[var(--border-color)] p-0">
                    <div v-if="isMapsLoading && mapRotation.length === 0" class="p-8 flex justify-center">
                      <div class="explorer-spinner"></div>
                    </div>
                    <div v-else-if="mapsError" class="p-6 text-neon-red text-sm text-center">
                      {{ mapsError }}
                    </div>
                    <MapRotationTable
                      v-else
                      :map-rotation="mapRotation"
                      :current-page="mapRotationPage"
                      :total-pages="mapRotationTotalPages"
                      :total-count="mapRotationTotalCount"
                      :page-size="mapRotationPageSize"
                      :is-loading="isMapsLoading"
                      @navigate="handleMapNavigate"
                      @page-change="handleMapRotationPageChange"
                    />
                  </div>
                </div>

                <!-- Player Activity History -->
                <div class="explorer-card">
                  <div 
                    class="explorer-card-header flex items-center justify-between cursor-pointer hover:bg-white/5 transition-colors"
                    @click="togglePlayerHistory"
                  >
                    <div class="flex items-center gap-3">
                      <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="text-cyan-400 flex-shrink-0"><polyline points="22,12 18,12 15,21 9,3 6,12 2,12"/></svg>
                      <div>
                        <h3 class="explorer-card-title">PLAYER ACTIVITY</h3>
                        <div class="text-[10px] text-neutral-500 font-mono mt-0.5">POPULATION TRENDS</div>
                      </div>
                    </div>
                    <div class="explorer-toggle-btn" :class="{ 'rotate-180': showPlayerHistory }">
                      ▼
                    </div>
                  </div>

                  <div v-if="showPlayerHistory" class="explorer-card-body border-t border-[var(--border-color)]">
                    <div v-if="serverInsights?.playersOnlineHistory">
                      <!-- Period Controls -->
                      <div class="flex justify-center mb-4">
                        <div class="explorer-toggle-group">
                          <button
                            v-for="period in ['1d', '3d', '7d']"
                            :key="period"
                            class="explorer-toggle-btn"
                            :class="{ 'explorer-toggle-btn--active': historyPeriod === period }"
                            @click="handleHistoryPeriodChange(period as '1d' | '3d' | '7d')"
                          >
                            {{ period === '1d' ? '24H' : period === '3d' ? '3D' : '7D' }}
                          </button>
                          
                          <!-- More Dropdown -->
                          <div class="relative">
                            <button
                              class="explorer-toggle-btn flex items-center gap-1"
                              :class="{ 'explorer-toggle-btn--active': historyPeriod === 'longer' }"
                              @click="toggleLongerDropdown"
                            >
                              {{ getLongerPeriodLabel() }} ▼
                            </button>
                            <div v-if="showLongerDropdown" class="absolute top-full right-0 mt-1 bg-[var(--bg-card)] border border-[var(--border-color)] rounded shadow-xl z-50 min-w-[120px]">
                              <button
                                v-for="period in [{ id: '1month', label: '1 Month' }, { id: '3months', label: '3 Months' }, { id: 'thisyear', label: 'This Year' }, { id: 'alltime', label: 'All Time' }]"
                                :key="period.id"
                                class="w-full text-left px-3 py-2 text-xs font-mono hover:bg-white/5 hover:text-neon-cyan transition-colors"
                                :class="{ 'text-neon-cyan': longerPeriod === period.id }"
                                @click="selectLongerPeriod(period.id as '1month' | '3months' | 'thisyear' | 'alltime')"
                              >
                                {{ period.label }}
                              </button>
                            </div>
                          </div>
                        </div>
                      </div>

                      <PlayerHistoryChart
                        :chart-data="serverInsights.playersOnlineHistory.dataPoints"
                        :insights="serverInsights.playersOnlineHistory.insights"
                        :period="serverInsights.playersOnlineHistory.period"
                        :rolling-window="historyRollingWindow"
                        :loading="isInsightsLoading"
                        :error="insightsError"
                        @rolling-window-change="handleRollingWindowChange"
                      />
                    </div>
                    <div v-else-if="isInsightsLoading" class="p-8 flex justify-center">
                      <div class="explorer-spinner"></div>
                    </div>
                  </div>
                </div>

              </div>
            </div>

          </div>

          <!-- Empty State -->
          <div v-else class="explorer-empty">
            <div class="explorer-empty-icon"><svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" class="text-neutral-500"><path d="M3 3v18h18"/><path d="M18 17V9"/><path d="M13 17V5"/><path d="M8 17v-3"/></svg></div>
            <p class="explorer-empty-title">No server data available</p>
          </div>
        </div>
      </div>
    </div>

    <!-- Players Modal (Overlay) -->
    <div v-if="showPlayersModal" class="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4" @click="closePlayersModal">
      <div class="w-full max-w-4xl max-h-[90vh] overflow-y-auto bg-[var(--bg-panel)] border border-[var(--border-color)] rounded-lg shadow-2xl" @click.stop>
        <div class="p-4 border-b border-[var(--border-color)] flex justify-between items-center sticky top-0 bg-[var(--bg-panel)] z-10">
          <h3 class="explorer-card-title text-lg">ONLINE PLAYERS</h3>
          <button class="explorer-btn explorer-btn--ghost explorer-btn--sm" @click="closePlayersModal">CLOSE</button>
        </div>
        <PlayersPanel
          :show="true"
          :server="liveServerInfo"
          :inline="true"
          @close="closePlayersModal"
        />
      </div>
    </div>

    <!-- Map Detail Panel (Overlay) -->
    <div v-if="showMapDetailPanel && selectedMapName && serverDetails?.serverGuid" class="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4" @click="handleCloseMapDetailPanel">
      <div class="w-full max-w-5xl max-h-[90vh] overflow-hidden flex flex-col bg-[var(--bg-panel)] border border-[var(--border-color)] rounded-lg shadow-2xl" @click.stop>
        <!-- Header -->
        <div class="p-4 border-b border-[var(--border-color)] flex justify-between items-center bg-[var(--bg-panel)]">
          <div>
            <h2 class="text-lg font-bold text-neon-cyan font-mono">
              {{ showRankingsInPanel ? `RANKINGS: ${rankingsMapNameForPanel}` : selectedMapName }}
            </h2>
            <p class="text-xs text-neutral-400 font-mono mt-1">ON {{ serverName }}</p>
          </div>
          <button class="explorer-btn explorer-btn--ghost explorer-btn--sm" aria-label="Close map detail panel" @click="handleCloseMapDetailPanel">CLOSE</button>
        </div>

        <!-- Content -->
        <div class="flex-1 overflow-y-auto p-4">
          <div v-if="showRankingsInPanel && rankingsMapNameForPanel">
            <button
              class="explorer-btn explorer-btn--ghost explorer-btn--sm mb-4 flex items-center gap-2"
              @click="handleCloseRankingsInPanel"
            >
              &larr; BACK TO MAP DETAIL
            </button>
            <MapRankingsPanel
              :map-name="rankingsMapNameForPanel"
              :server-guid="serverDetails.serverGuid"
              :game="(serverDetails.gameId as any) || 'bf1942'"
            />
          </div>
          <ServerMapDetailPanel
            v-else
            :server-guid="serverDetails.serverGuid"
            :map-name="selectedMapName"
            @navigate-to-server="handleNavigateToServerFromMap"
            @navigate-to-map="handleNavigateToMapFromMap"
            @close="handleCloseMapDetailPanel"
            @open-rankings="handleOpenRankingsFromMap"
          />
        </div>
      </div>
    </div>

  </div>
</template>

<style src="./portal-layout.css"></style>
<style scoped src="./DataExplorer.vue.css"></style>
