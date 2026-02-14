<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import type { LeaderboardsData } from '../services/serverDetailsService';
import PlayerName from './PlayerName.vue';

const props = defineProps<{
  leaderboardsData: LeaderboardsData | null;
  isLoading: boolean;
  error: string | null;
  serverName: string;
  serverGuid?: string;
  minPlayersForWeighting?: number;
  minRoundsForKillBoards?: number;
}>();

const emit = defineEmits<{
  updateMinPlayersForWeighting: [value: number];
  updateMinRoundsForKillBoards: [value: number];
  periodChange: [period: 'week' | 'month' | 'alltime'];
}>();

const selectedTimePeriod = ref<'week' | 'month' | 'alltime'>('week');
const showWeightedPlacements = ref(true);
const showPlacementInfo = ref(false);
const localMinPlayersForWeighting = ref(props.minPlayersForWeighting || 15);
const localMinRoundsForKillBoards = ref(props.minRoundsForKillBoards || 20);

watch(() => props.minPlayersForWeighting, (newValue) => {
  if (newValue !== undefined) localMinPlayersForWeighting.value = newValue;
});
watch(() => props.minRoundsForKillBoards, (newValue) => {
  if (newValue !== undefined) localMinRoundsForKillBoards.value = newValue;
});

const toggleTimePeriod = (period: 'week' | 'month' | 'alltime') => {
  selectedTimePeriod.value = period;
  emit('periodChange', period);
};
const togglePlacementType = () => { showWeightedPlacements.value = !showWeightedPlacements.value; };
const updateMinPlayersWeighting = (value: number) => {
  localMinPlayersForWeighting.value = value;
  emit('updateMinPlayersForWeighting', value);
};
const updateMinRoundsForKillBoards = (value: number) => {
  localMinRoundsForKillBoards.value = value;
  emit('updateMinRoundsForKillBoards', value);
};

const currentMostActivePlayers = computed(() => props.leaderboardsData?.mostActivePlayersByTime ?? []);
const currentTopScores = computed(() => props.leaderboardsData?.topScores ?? []);
const currentTopKDRatios = computed(() => props.leaderboardsData?.topKDRatios ?? []);
const currentTopKillRates = computed(() => props.leaderboardsData?.topKillRates ?? []);
const currentTopPlacements = computed(() => {
  if (!props.leaderboardsData) return [];
  if (showWeightedPlacements.value && props.leaderboardsData.weightedTopPlacements?.length)
    return props.leaderboardsData.weightedTopPlacements;
  return props.leaderboardsData.topPlacements ?? [];
});

const hasAnyPlacementData = computed(() => !!(
  props.leaderboardsData?.topPlacements?.length || props.leaderboardsData?.weightedTopPlacements?.length
));

const placementTypeLabel = computed(() =>
  showWeightedPlacements.value ? 'High-Stakes Champions' : 'Olympic Champions'
);
const placementTypeSubtitle = computed(() =>
  showWeightedPlacements.value
    ? `Only rounds with ${localMinPlayersForWeighting.value}+ players count`
    : 'All rounds count equally'
);
const placementTypeDescription = computed(() =>
  showWeightedPlacements.value
    ? 'Placements earned in competitive rounds with more players. Small or empty rounds are filtered out so rankings reflect real competition.'
    : 'Every round counts the same regardless of player count. Players who finish 1st, 2nd, or 3rd earn points \u2014 3 for gold, 2 for silver, 1 for bronze.'
);

const TOP_N = 5;

/** Compute the width of a performance bar relative to the max value in the list */
const barWidth = (value: number, maxValue: number): string => {
  if (maxValue <= 0) return '0%';
  return `${Math.max(10, Math.round((value / maxValue) * 100))}%`;
};
</script>

<template>
  <!-- Loading skeleton -->
  <div v-if="isLoading && !leaderboardsData" class="space-y-4 py-2 animate-pulse">
    <div class="h-7 w-28 rounded bg-neutral-800" />
    <div class="h-24 rounded-lg bg-neutral-800/60" />
    <div class="h-px bg-neutral-700/40" />
    <div class="grid grid-cols-2 gap-3">
      <div v-for="i in 4" :key="i" class="h-40 bg-neutral-800/50 rounded-lg" />
    </div>
  </div>

  <!-- Error -->
  <div v-else-if="error" class="py-8 text-center">
    <p class="text-sm text-red-400/80">{{ error }}</p>
  </div>

  <!-- Content -->
  <div v-else class="space-y-5">
    <!-- Period selector -->
    <div class="flex items-center gap-3 flex-wrap">
      <div class="flex items-center rounded-md bg-black/40 p-0.5 border border-neutral-700/50">
        <button
          v-for="period in ([['week', '7d'], ['month', '30d'], ['alltime', 'All']] as const)"
          :key="period[0]"
          type="button"
          class="relative px-3 py-1 text-[11px] font-medium tracking-wide rounded transition-all duration-200"
          :class="selectedTimePeriod === period[0]
            ? 'bg-neutral-700/60 text-neutral-100 shadow-sm'
            : 'text-neutral-400 hover:text-neutral-200'"
          :disabled="isLoading"
          @click="toggleTimePeriod(period[0])"
        >
          <span v-if="isLoading && selectedTimePeriod === period[0]" class="inline-block w-3 h-3 border border-neutral-500 border-t-neutral-200 rounded-full animate-spin align-middle" />
          <span v-else>{{ period[1] }}</span>
        </button>
      </div>
    </div>

    <!-- ═══ Champions Section ═══ -->
    <section v-if="hasAnyPlacementData" class="space-y-3">
      <!-- Header row with toggle & settings -->
      <div class="space-y-2">
        <div class="flex items-center justify-between flex-wrap gap-2">
          <div class="flex items-center gap-2">
            <div>
              <h3 class="text-[11px] font-medium text-neutral-400 uppercase tracking-[0.12em]">
                {{ placementTypeLabel }}
              </h3>
              <p class="text-[10px] text-neutral-500 mt-0.5">{{ placementTypeSubtitle }}</p>
            </div>
            <!-- Info toggle -->
            <button
              type="button"
              class="w-4 h-4 rounded-full text-[9px] font-bold leading-none transition-colors shrink-0"
              :class="showPlacementInfo
                ? 'bg-neutral-600 text-neutral-200'
                : 'bg-neutral-800 text-neutral-500 hover:text-neutral-300 hover:bg-neutral-700'"
              title="How does this work?"
              @click="showPlacementInfo = !showPlacementInfo"
            >?</button>
          </div>
          <div class="flex items-center gap-2">
            <!-- Two-segment toggle -->
            <div class="flex rounded border border-neutral-700/50 overflow-hidden">
              <button
                type="button"
                class="px-2 py-0.5 text-[10px] font-medium transition-colors"
                :class="showWeightedPlacements
                  ? 'text-neutral-100 bg-neutral-700/60'
                  : 'text-neutral-500 hover:text-neutral-300 bg-transparent'"
                @click="showWeightedPlacements || togglePlacementType()"
              >High-Stakes</button>
              <button
                type="button"
                class="px-2 py-0.5 text-[10px] font-medium transition-colors border-l border-neutral-700/50"
                :class="!showWeightedPlacements
                  ? 'text-neutral-100 bg-neutral-700/60'
                  : 'text-neutral-500 hover:text-neutral-300 bg-transparent'"
                @click="showWeightedPlacements && togglePlacementType()"
              >All Rounds</button>
            </div>
            <select
              v-if="showWeightedPlacements"
              :value="localMinPlayersForWeighting"
              class="text-[10px] text-neutral-400 rounded px-1.5 py-0.5 cursor-pointer border border-neutral-700/50 bg-black/30 outline-none focus:border-neutral-500"
              aria-label="Minimum players"
              @change="updateMinPlayersWeighting(parseInt(($event.target as HTMLSelectElement).value))"
            >
              <option v-for="n in [10, 15, 20, 25, 30]" :key="n" :value="n">{{ n }}+ players</option>
            </select>
          </div>
        </div>
        <!-- Contextual explanation -->
        <div
          v-if="showPlacementInfo"
          class="text-[11px] leading-relaxed text-neutral-400 bg-neutral-800/40 rounded-md px-3 py-2.5 border border-neutral-700/30"
        >
          <p>{{ placementTypeDescription }}</p>
          <p class="mt-1.5 text-neutral-500">
            <span class="text-neutral-400">Scoring:</span>
            3 pts for 1st, 2 pts for 2nd, 1 pt for 3rd.
            Toggle between <span class="text-neutral-300">High-Stakes</span> (only competitive rounds)
            and <span class="text-neutral-300">All Rounds</span> (every round counts).
          </p>
        </div>
      </div>

      <!-- Top placements -->
      <div v-if="currentTopPlacements.length > 0" class="space-y-3">

        <!-- #1 Featured Champion — ambient spotlight -->
        <div class="relative rounded-lg overflow-hidden bg-gradient-to-b from-yellow-900/10 to-neutral-900/50 border border-yellow-600/20 shadow-[0_0_15px_-3px_rgba(234,179,8,0.1)]">
          <div class="absolute inset-0 bg-[radial-gradient(circle_at_top,_rgba(234,179,8,0.15)_0%,_transparent_60%)]" />
          <div class="relative text-center py-6 px-4">
            <div class="mb-2 text-[10px] font-bold uppercase tracking-widest text-yellow-500/80">Current Champion</div>
            <router-link
              :to="`/players/${encodeURIComponent(currentTopPlacements[0].playerName)}`"
              class="inline-block group/champ"
            >
              <span class="text-xl sm:text-3xl font-black tracking-tight text-white drop-shadow-[0_0_10px_rgba(234,179,8,0.3)] group-hover/champ:text-yellow-100 transition-colors duration-300">
                <PlayerName :name="currentTopPlacements[0].playerName" source="server-leaderboards" />
              </span>
            </router-link>
            <!-- Points -->
            <div class="mt-3 text-lg font-mono tabular-nums text-yellow-400 font-bold">
              {{ currentTopPlacements[0].placementPoints }} <span class="text-yellow-600/80 text-xs font-normal uppercase">pts</span>
            </div>
            <!-- Medal breakdown with labels -->
            <div class="mt-3 flex items-center justify-center gap-4 text-[11px] font-mono tabular-nums">
              <span class="flex items-center gap-1 text-yellow-200 bg-yellow-500/10 px-2 py-0.5 rounded border border-yellow-500/20">
                <span class="font-bold">{{ currentTopPlacements[0].firstPlaces }}</span> <span class="text-yellow-500/80 text-[9px] uppercase">Gold</span>
              </span>
              <span class="flex items-center gap-1 text-slate-300">
                <span>{{ currentTopPlacements[0].secondPlaces }}</span> <span class="text-slate-500 text-[9px] uppercase">Silv</span>
              </span>
              <span class="flex items-center gap-1 text-amber-600">
                <span>{{ currentTopPlacements[0].thirdPlaces }}</span> <span class="text-amber-800 text-[9px] uppercase">Brnz</span>
              </span>
            </div>
          </div>
        </div>

        <!-- #2 and #3 side by side -->
        <div v-if="currentTopPlacements.length > 1" class="grid grid-cols-2 gap-3">
          <!-- #2 Silver -->
          <div
            v-if="currentTopPlacements[1]"
            class="relative overflow-hidden bg-gradient-to-br from-slate-800/30 to-neutral-900/50 rounded-lg py-3.5 px-4 border border-slate-500/20"
          >
            <div class="absolute top-0 right-0 p-2 opacity-10">
              <span class="text-4xl font-black text-slate-400">2</span>
            </div>
            <div class="relative z-10">
               <div class="text-[9px] font-bold uppercase tracking-widest text-slate-500 mb-1">Runner Up</div>
               <router-link
                :to="`/players/${encodeURIComponent(currentTopPlacements[1].playerName)}`"
                class="block text-sm sm:text-base text-slate-200 truncate hover:text-white transition-colors font-bold"
              >
                <PlayerName :name="currentTopPlacements[1].playerName" source="server-leaderboards" />
              </router-link>
              <div class="mt-1 text-xs font-mono text-slate-400">
                <span class="text-slate-300 font-bold">{{ currentTopPlacements[1].placementPoints }}</span> pts
              </div>
            </div>
          </div>

          <!-- #3 Bronze -->
          <div
            v-if="currentTopPlacements[2]"
            class="relative overflow-hidden bg-gradient-to-br from-amber-900/20 to-neutral-900/50 rounded-lg py-3.5 px-4 border border-amber-700/20"
          >
             <div class="absolute top-0 right-0 p-2 opacity-10">
              <span class="text-4xl font-black text-amber-600">3</span>
            </div>
            <div class="relative z-10">
               <div class="text-[9px] font-bold uppercase tracking-widest text-amber-700 mb-1">Third Place</div>
               <router-link
                :to="`/players/${encodeURIComponent(currentTopPlacements[2].playerName)}`"
                class="block text-sm sm:text-base text-amber-200/90 truncate hover:text-amber-100 transition-colors font-bold"
              >
                <PlayerName :name="currentTopPlacements[2].playerName" source="server-leaderboards" />
              </router-link>
              <div class="mt-1 text-xs font-mono text-amber-500/80">
                <span class="text-amber-500 font-bold">{{ currentTopPlacements[2].placementPoints }}</span> pts
              </div>
            </div>
          </div>
        </div>

        <!-- #4+ remaining -->
        <div v-if="currentTopPlacements.length > 3">
          <div
            v-for="(player, idx) in currentTopPlacements.slice(3)"
            :key="player.playerName"
            class="grid grid-cols-[2rem_1fr_auto] items-center gap-x-3 py-2 px-2 rounded hover:bg-neutral-800/30 transition-colors border-b border-neutral-800/40 last:border-b-0"
          >
            <span class="text-right text-xs font-mono text-neutral-500 tabular-nums">{{ idx + 4 }}</span>
            <router-link
              :to="`/players/${encodeURIComponent(player.playerName)}`"
              class="text-sm text-neutral-300 truncate hover:text-neutral-100 transition-colors"
            >
              <PlayerName :name="player.playerName" source="server-leaderboards" />
            </router-link>
            <span class="text-xs font-mono text-neutral-500 tabular-nums whitespace-nowrap">
              {{ player.placementPoints }} pts
              <span class="text-neutral-600 hidden sm:inline ml-2">{{ player.firstPlaces }}-{{ player.secondPlaces }}-{{ player.thirdPlaces }}</span>
            </span>
          </div>
        </div>
      </div>

      <!-- Empty placements -->
      <div v-else class="py-6 text-center text-xs text-neutral-500">
        No champions for this period
      </div>
    </section>

    <!-- Centered fading divider -->
    <div class="h-px bg-gradient-to-r from-transparent via-neutral-700/60 to-transparent" />

    <!-- ═══ Stat Boards ═══ -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">

      <!-- Most Active -->
      <div class="bg-neutral-900/40 rounded-lg p-4 border border-cyan-900/30 relative overflow-hidden group/card hover:border-cyan-700/30 transition-colors">
        <div class="absolute top-0 left-0 w-full h-0.5 bg-gradient-to-r from-cyan-500/50 to-transparent"></div>
        <div class="text-[10px] font-bold uppercase tracking-[0.15em] text-cyan-500/80 mb-3 flex items-center gap-2">
          <span class="w-1.5 h-1.5 rounded-full bg-cyan-500"></span> Most Active
        </div>
        <template v-if="currentMostActivePlayers.length > 0">
          <div
            v-for="(player, i) in currentMostActivePlayers.slice(0, TOP_N)"
            :key="player.playerName"
            class="relative grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 py-1.5 px-2 rounded group/row z-10"
          >
            <!-- Performance bar -->
            <div
              class="absolute inset-y-0 left-0 rounded-sm transition-all duration-500 opacity-20 group-hover/row:opacity-30"
              :class="i === 0 ? 'bg-cyan-400' : 'bg-cyan-900'"
              :style="{ width: barWidth(player.minutesPlayed, currentMostActivePlayers[0].minutesPlayed) }"
            />
            <span
              class="relative text-right text-[10px] font-mono tabular-nums"
              :class="i === 0 ? 'text-cyan-300 font-bold' : 'text-neutral-600'"
            >{{ i + 1 }}</span>
            <router-link
              :to="`/players/${encodeURIComponent(player.playerName)}`"
              class="relative text-[13px] truncate transition-colors duration-200 font-medium"
              :class="i === 0 ? 'text-cyan-100' : 'text-neutral-400 group-hover/row:text-neutral-200'"
            >
              <PlayerName :name="player.playerName" source="server-leaderboards" />
            </router-link>
            <span
              class="relative text-[11px] font-mono tabular-nums whitespace-nowrap"
              :class="i === 0 ? 'text-cyan-200' : 'text-neutral-500'"
            >{{ player.minutesPlayed }}m</span>
          </div>
        </template>
        <div v-else class="text-xs text-neutral-500 py-3 text-center">No data</div>
      </div>

      <!-- Top Scores -->
      <div class="bg-neutral-900/40 rounded-lg p-4 border border-emerald-900/30 relative overflow-hidden group/card hover:border-emerald-700/30 transition-colors">
        <div class="absolute top-0 left-0 w-full h-0.5 bg-gradient-to-r from-emerald-500/50 to-transparent"></div>
        <div class="text-[10px] font-bold uppercase tracking-[0.15em] text-emerald-500/80 mb-3 flex items-center gap-2">
          <span class="w-1.5 h-1.5 rounded-full bg-emerald-500"></span> Top Scores
        </div>
        <template v-if="currentTopScores.length > 0">
          <div
            v-for="(entry, i) in currentTopScores.slice(0, TOP_N)"
            :key="`${entry.playerName}-${entry.score}-${i}`"
            class="relative grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 py-1.5 px-2 rounded group/row z-10"
          >
            <div
              class="absolute inset-y-0 left-0 rounded-sm transition-all duration-500 opacity-20 group-hover/row:opacity-30"
              :class="i === 0 ? 'bg-emerald-400' : 'bg-emerald-900'"
              :style="{ width: barWidth(entry.score, currentTopScores[0].score) }"
            />
            <span
              class="relative text-right text-[10px] font-mono tabular-nums"
              :class="i === 0 ? 'text-emerald-300 font-bold' : 'text-neutral-600'"
            >{{ i + 1 }}</span>
            <router-link
              :to="`/players/${encodeURIComponent(entry.playerName)}`"
              class="relative text-[13px] truncate transition-colors duration-200 font-medium"
              :class="i === 0 ? 'text-emerald-100' : 'text-neutral-400 group-hover/row:text-neutral-200'"
            >
              <PlayerName :name="entry.playerName" source="server-leaderboards" />
            </router-link>
            <span
              class="relative text-[11px] font-mono tabular-nums whitespace-nowrap"
              :class="i === 0 ? 'text-emerald-200' : 'text-neutral-500'"
            >{{ entry.score.toLocaleString() }}</span>
          </div>
        </template>
        <div v-else class="text-xs text-neutral-500 py-3 text-center">No data</div>
      </div>

      <!-- Elite K/D -->
      <div class="bg-neutral-900/40 rounded-lg p-4 border border-purple-900/30 relative overflow-hidden group/card hover:border-purple-700/30 transition-colors">
        <div class="absolute top-0 left-0 w-full h-0.5 bg-gradient-to-r from-purple-500/50 to-transparent"></div>
        <div class="flex items-center justify-between gap-2 mb-3 relative z-10">
          <div class="text-[10px] font-bold uppercase tracking-[0.15em] text-purple-500/80 flex items-center gap-2">
            <span class="w-1.5 h-1.5 rounded-full bg-purple-500"></span> Elite K/D
          </div>
          <select
            :value="localMinRoundsForKillBoards"
            class="text-[9px] text-purple-400/80 rounded px-1.5 py-0.5 cursor-pointer border border-purple-900/50 bg-purple-900/10 outline-none focus:border-purple-500/50 hover:bg-purple-900/20 transition-colors"
            aria-label="Minimum rounds"
            @change="updateMinRoundsForKillBoards(parseInt(($event.target as HTMLSelectElement).value))"
          >
            <option v-for="n in [5, 10, 15, 20, 25, 30, 50, 100]" :key="n" :value="n">{{ n }}+ rnds</option>
          </select>
        </div>
        <template v-if="currentTopKDRatios.length > 0">
          <div
            v-for="(entry, i) in currentTopKDRatios.slice(0, TOP_N)"
            :key="`${entry.playerName}-${entry.kdRatio}-${i}`"
            class="relative grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 py-1.5 px-2 rounded group/row z-10"
          >
            <div
              class="absolute inset-y-0 left-0 rounded-sm transition-all duration-500 opacity-20 group-hover/row:opacity-30"
              :class="i === 0 ? 'bg-purple-400' : 'bg-purple-900'"
              :style="{ width: barWidth(entry.kdRatio ?? 0, currentTopKDRatios[0].kdRatio ?? 0) }"
            />
            <span
              class="relative text-right text-[10px] font-mono tabular-nums"
              :class="i === 0 ? 'text-purple-300 font-bold' : 'text-neutral-600'"
            >{{ i + 1 }}</span>
            <router-link
              :to="`/players/${encodeURIComponent(entry.playerName)}`"
              class="relative text-[13px] truncate transition-colors duration-200 font-medium"
              :class="i === 0 ? 'text-purple-100' : 'text-neutral-400 group-hover/row:text-neutral-200'"
            >
              <PlayerName :name="entry.playerName" source="server-leaderboards" />
            </router-link>
            <span
              class="relative text-[11px] font-mono tabular-nums whitespace-nowrap"
              :class="i === 0 ? 'text-purple-200' : 'text-neutral-500'"
            >{{ (entry.kdRatio ?? 0).toFixed(2) }}</span>
          </div>
        </template>
        <div v-else class="text-xs text-neutral-500 py-3 text-center">No data</div>
      </div>

      <!-- Kill Rate -->
      <div class="bg-neutral-900/40 rounded-lg p-4 border border-rose-900/30 relative overflow-hidden group/card hover:border-rose-700/30 transition-colors">
        <div class="absolute top-0 left-0 w-full h-0.5 bg-gradient-to-r from-rose-500/50 to-transparent"></div>
        <div class="flex items-center justify-between gap-2 mb-3 relative z-10">
          <div class="text-[10px] font-bold uppercase tracking-[0.15em] text-rose-500/80 flex items-center gap-2">
            <span class="w-1.5 h-1.5 rounded-full bg-rose-500"></span> Kill Rate
          </div>
          <select
            :value="localMinRoundsForKillBoards"
            class="text-[9px] text-rose-400/80 rounded px-1.5 py-0.5 cursor-pointer border border-rose-900/50 bg-rose-900/10 outline-none focus:border-rose-500/50 hover:bg-rose-900/20 transition-colors"
            aria-label="Minimum rounds"
            @change="updateMinRoundsForKillBoards(parseInt(($event.target as HTMLSelectElement).value))"
          >
            <option v-for="n in [5, 10, 15, 20, 25, 30, 50, 100]" :key="n" :value="n">{{ n }}+ rnds</option>
          </select>
        </div>
        <template v-if="currentTopKillRates.length > 0">
          <div
            v-for="(entry, i) in currentTopKillRates.slice(0, TOP_N)"
            :key="`${entry.playerName}-${entry.killRate}-${i}`"
            class="relative grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 py-1.5 px-2 rounded group/row z-10"
          >
            <div
              class="absolute inset-y-0 left-0 rounded-sm transition-all duration-500 opacity-20 group-hover/row:opacity-30"
              :class="i === 0 ? 'bg-rose-400' : 'bg-rose-900'"
              :style="{ width: barWidth(entry.killRate ?? 0, currentTopKillRates[0].killRate ?? 0) }"
            />
            <span
              class="relative text-right text-[10px] font-mono tabular-nums"
              :class="i === 0 ? 'text-rose-300 font-bold' : 'text-neutral-600'"
            >{{ i + 1 }}</span>
            <router-link
              :to="`/players/${encodeURIComponent(entry.playerName)}`"
              class="relative text-[13px] truncate transition-colors duration-200 font-medium"
              :class="i === 0 ? 'text-rose-100' : 'text-neutral-400 group-hover/row:text-neutral-200'"
            >
              <PlayerName :name="entry.playerName" source="server-leaderboards" />
            </router-link>
            <span
              class="relative text-[11px] font-mono tabular-nums whitespace-nowrap"
              :class="i === 0 ? 'text-rose-200' : 'text-neutral-500'"
            >{{ (entry.killRate ?? 0).toFixed(2) }}/m</span>
          </div>
        </template>
        <div v-else class="text-xs text-neutral-500 py-3 text-center">No data</div>
      </div>

    </div>
  </div>
</template>
