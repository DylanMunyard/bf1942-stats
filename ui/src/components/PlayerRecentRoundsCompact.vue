<script setup lang="ts">
import { computed } from 'vue';
import { useRouter } from 'vue-router';
import type { Session } from '@/types/playerStatsTypes';

const props = defineProps<{
  sessions: Session[];
  playerName: string;
}>();

const router = useRouter();

const compactSessions = computed(() => (props.sessions ?? []).slice(0, 3));

const formatPlacement = (placement: number | null): string => {
  if (!placement || placement <= 0) return 'n/a';
  const mod10 = placement % 10;
  const mod100 = placement % 100;
  let suffix = 'th';
  if (mod10 === 1 && mod100 !== 11) suffix = 'st';
  else if (mod10 === 2 && mod100 !== 12) suffix = 'nd';
  else if (mod10 === 3 && mod100 !== 13) suffix = 'rd';
  return `${placement}${suffix}`;
};

const getResultBadgeClass = (result: Session['teamResult']): string => {
  if (result === 'win') return 'bg-emerald-500/15 border-emerald-500/50 text-emerald-300';
  if (result === 'loss') return 'bg-red-500/15 border-red-500/50 text-red-300';
  if (result === 'tie') return 'bg-amber-500/15 border-amber-500/50 text-amber-300';
  return 'bg-neutral-500/10 border-neutral-600/50 text-neutral-400';
};

const getResultLabel = (result: Session['teamResult']): string => {
  if (result === 'win') return 'W';
  if (result === 'loss') return 'L';
  if (result === 'tie') return 'T';
  return '-';
};

const navigateToRoundReport = (session: Session) => {
  router.push({
    name: 'round-report',
    params: { roundId: session.roundId },
    query: { players: props.playerName },
  });
};
</script>

<template>
  <div v-if="compactSessions.length > 0" class="inline-flex items-center gap-1.5 min-w-0 rounded-md border border-neutral-700/70 bg-neutral-950/60 px-2 py-1">
    <span class="text-[10px] uppercase tracking-wide text-neutral-500 font-semibold">Recent</span>
    <button
      v-for="(session, index) in compactSessions"
      :key="`${session.roundId}-${session.sessionId}-${index}`"
      type="button"
      class="inline-flex items-center gap-1 rounded border border-neutral-700/60 bg-neutral-900/60 hover:bg-neutral-800/70 px-1.5 py-0.5 text-[11px] transition-colors min-w-0"
      :title="`${session.mapName} • ${session.serverName}`"
      @click="navigateToRoundReport(session)"
    >
      <span
        class="inline-flex items-center px-1 py-0.5 rounded border text-[10px] font-bold"
        :class="getResultBadgeClass(session.teamResult ?? 'unknown')"
        :title="session.playerTeamLabel ? `Team: ${session.playerTeamLabel}` : 'Team result unavailable'"
      >
        {{ getResultLabel(session.teamResult ?? 'unknown') }}
      </span>
      <span class="font-bold text-amber-300">{{ session.totalScore.toLocaleString() }}</span>
      <span class="text-neutral-400 font-mono">{{ formatPlacement(session.placement) }}</span>
    </button>
    <router-link
      :to="`/players/${encodeURIComponent(playerName)}/sessions`"
      class="text-[10px] text-neutral-400 hover:text-neutral-200 transition-colors"
    >
      All
    </router-link>
  </div>
</template>
