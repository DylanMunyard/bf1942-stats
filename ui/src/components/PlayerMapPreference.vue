<template>
  <div class="player-map-preference">
    <!-- Loading state -->
    <div v-if="loading" class="loading-state">
      Loading map preferences...
    </div>

    <!-- Error state -->
    <div v-else-if="error" class="error-state">
      {{ error }}
    </div>

    <!-- Map preference data -->
    <div v-else-if="topMap" class="preference-content">
      <!-- Hero card for top map -->
      <div class="hero-map hero-map--clickable" @click="emit('navigateToMap', topMap.mapName)">
        <h4 class="hero-map-name">{{ topMap.mapName }}</h4>
        <div class="hero-stats">
          <div class="stat-item">
            <span class="stat-label">K/D</span>
            <span class="stat-value">{{ calculateKD(topMap) }}</span>
          </div>
          <div class="stat-item">
            <span class="stat-label">Sessions</span>
            <span class="stat-value">{{ topMap.sessionsPlayed }}</span>
          </div>
          <div class="stat-item">
            <span class="stat-label">Time</span>
            <span class="stat-value">{{ formatPlayTime(topMap.totalPlayTimeMinutes) }}</span>
          </div>
        </div>
      </div>

      <!-- Top 5 maps bars -->
      <div v-if="topMaps.length > 1" class="map-bars">
        <div v-for="(map, index) in topMaps.slice(0, 5)" :key="map.mapName" class="map-bar-row map-bar-row--clickable" @click="emit('navigateToMap', map.mapName)">
          <div class="map-bar-label">
            <span class="map-position">{{ index + 1 }}</span>
            <span class="map-name">{{ map.mapName }}</span>
          </div>
          <div class="map-bar-container">
            <div 
              class="map-bar"
              :style="{ width: getBarWidth(map) + '%' }"
            ></div>
            <span class="map-bar-time">{{ formatPlayTime(map.totalPlayTimeMinutes) }}</span>
          </div>
        </div>
      </div>

      <!-- No maps fallback -->
      <div v-else class="no-maps">
        <p>No recent map activity in the last 30 days</p>
      </div>
    </div>

    <!-- No data state -->
    <div v-else class="no-data">
      No map data available
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { fetchPlayerMapStats } from '../services/playerStatsApi';
import type { PlayerMapStatEntry } from '../types/playerStatsTypes';

const props = defineProps<{
  playerName: string;
  game?: string;
}>();

const emit = defineEmits<{
  navigateToMap: [mapName: string];
}>();

const loading = ref(true);
const error = ref<string | null>(null);
const mapStats = ref<PlayerMapStatEntry[]>([]);

// Get top maps sorted by play time
const topMaps = computed(() => {
  return [...mapStats.value].sort((a, b) => b.totalPlayTimeMinutes - a.totalPlayTimeMinutes);
});

// Get the top map
const topMap = computed(() => {
  return topMaps.value[0] || null;
});

// Calculate K/D ratio
function calculateKD(map: PlayerMapStatEntry | null): string {
  if (!map) return '0.00';
  const ratio = map.totalDeaths > 0 ? map.totalKills / map.totalDeaths : map.totalKills;
  return ratio.toFixed(2);
}

// Calculate bar width as percentage of max play time
function getBarWidth(map: PlayerMapStatEntry): number {
  if (!topMap.value) return 0;
  return (map.totalPlayTimeMinutes / topMap.value.totalPlayTimeMinutes) * 100;
}

// Format play time to human readable format
function formatPlayTime(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const mins = Math.round(minutes % 60);
  
  if (hours === 0) {
    return `${mins}m`;
  } else if (mins === 0) {
    return `${hours}h`;
  } else {
    return `${hours}h ${mins}m`;
  }
}

async function loadData() {
  loading.value = true;
  error.value = null;

  try {
    mapStats.value = await fetchPlayerMapStats(
      props.playerName,
      props.game || 'bf1942',
      30 // Last 30 days
    );
  } catch (err) {
    error.value = 'Failed to load map preferences';
    console.error(err);
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  loadData();
});
</script>

<style scoped>
.player-map-preference {
  padding: 16px;
}

.loading-state,
.error-state,
.no-data,
.no-maps {
  text-align: center;
  color: var(--text-secondary);
  padding: 24px;
}

.error-state {
  color: var(--error-color);
}

/* Hero map card */
.hero-map {
  background: rgba(245, 158, 11, 0.05); /* Subtle amber tint */
  border: 1px solid rgba(245, 158, 11, 0.2);
  border-radius: 4px;
  padding: 16px;
  margin-bottom: 20px;
}

.hero-map--clickable {
  cursor: pointer;
  transition: border-color 0.2s ease, background 0.2s ease;
}

.hero-map--clickable:hover {
  border-color: rgba(245, 158, 11, 0.5);
  background: rgba(245, 158, 11, 0.1);
}

.hero-map-name {
  font-size: 18px;
  font-weight: 600;
  color: var(--neon-gold);
  margin: 0 0 12px 0;
}

.hero-stats {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.stat-label {
  font-size: 11px;
  text-transform: uppercase;
  color: var(--text-secondary);
  margin-bottom: 4px;
}

.stat-value {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-primary);
}

/* Map bars */
.map-bars {
  margin-top: 16px;
}

.map-bar-row {
  margin-bottom: 12px;
}

.map-bar-row--clickable {
  cursor: pointer;
  border-radius: 4px;
  padding: 4px;
  margin-left: -4px;
  margin-right: -4px;
  transition: background 0.2s ease;
}

.map-bar-row--clickable:hover {
  background: rgba(255, 255, 255, 0.05);
}

.map-bar-row--clickable:hover .map-name {
  color: var(--neon-cyan);
}

.map-bar-label {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.map-position {
  width: 20px;
  height: 20px;
  background: var(--bg-card);
  border: 1px solid var(--border-color);
  border-radius: 2px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--text-secondary);
}

.map-name {
  font-size: 13px;
  color: var(--text-primary);
}

.map-bar-container {
  position: relative;
  height: 20px;
  background: var(--bg-panel);
  border-radius: 2px;
  overflow: hidden;
}

.map-bar {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  background: linear-gradient(90deg, 
    rgba(0, 217, 255, 0.3) 0%, 
    rgba(0, 217, 255, 0.6) 100%
  );
  transition: width 0.3s ease;
}

.map-bar-time {
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  font-size: 11px;
  color: var(--text-secondary);
  z-index: 1;
}
</style>