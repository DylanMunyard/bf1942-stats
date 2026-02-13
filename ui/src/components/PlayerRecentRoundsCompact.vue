<script setup lang="ts">
import { computed, reactive, watch } from 'vue';
import { useRouter } from 'vue-router';
import type { Session } from '@/types/playerStatsTypes';
import { fetchRoundReport } from '@/services/serverDetailsService';

const props = defineProps<{
  sessions: Session[];
  playerName: string;
}>();

const router = useRouter();

type RoundContext = {
  placement: number | null;
  totalParticipants: number | null;
  teamResult: 'win' | 'loss' | 'tie' | 'unknown';
  playerTeamLabel: string | null;
  loading: boolean;
};

const roundContextById = reactive<Record<string, RoundContext>>({});

const compactSessions = computed(() => (props.sessions ?? []).slice(0, 3));

const normalizeName = (name: string): string => name.trim().toLowerCase();

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

const getResultBadgeClass = (result: RoundContext['teamResult']): string => {
  if (result === 'win') return 'bg-emerald-500/15 border-emerald-500/50 text-emerald-300';
  if (result === 'loss') return 'bg-red-500/15 border-red-500/50 text-red-300';
  if (result === 'tie') return 'bg-amber-500/15 border-amber-500/50 text-amber-300';
  return 'bg-neutral-500/10 border-neutral-600/50 text-neutral-400';
};

const getResultLabel = (result: RoundContext['teamResult']): string => {
  if (result === 'win') return 'W';
  if (result === 'loss') return 'L';
  if (result === 'tie') return 'T';
  return '-';
};

const hydrateRoundContext = async (session: Session) => {
  if (!session.roundId || roundContextById[session.roundId]) return;

  roundContextById[session.roundId] = {
    placement: null,
    totalParticipants: null,
    teamResult: 'unknown',
    playerTeamLabel: null,
    loading: true,
  };

  try {
    const report = await fetchRoundReport(session.roundId);
    const snapshots = report.leaderboardSnapshots ?? [];
    const latestSnapshot = snapshots.length > 0 ? snapshots[snapshots.length - 1] : null;
    const entries = latestSnapshot?.entries ?? [];
    const myName = normalizeName(props.playerName);
    const myEntry = entries.find(entry => normalizeName(entry.playerName) === myName);
    const myTeamLabel = myEntry?.teamLabel?.trim() || null;
    const team1 = report.round?.team1Label?.trim();
    const team2 = report.round?.team2Label?.trim();
    const tickets1 = report.round?.tickets1;
    const tickets2 = report.round?.tickets2;

    let teamResult: RoundContext['teamResult'] = 'unknown';
    if (
      typeof tickets1 === 'number' &&
      typeof tickets2 === 'number' &&
      tickets1 >= 0 &&
      tickets2 >= 0 &&
      team1 &&
      team2 &&
      myTeamLabel
    ) {
      if (tickets1 === tickets2) {
        teamResult = 'tie';
      } else {
        const winningTeam = tickets1 > tickets2 ? team1 : team2;
        teamResult = normalizeName(winningTeam) === normalizeName(myTeamLabel) ? 'win' : 'loss';
      }
    }

    roundContextById[session.roundId] = {
      placement: myEntry?.rank ?? null,
      totalParticipants: report.round?.totalParticipants ?? (entries.length || null),
      teamResult,
      playerTeamLabel: myTeamLabel,
      loading: false,
    };
  } catch (error) {
    console.error('Failed to load compact round context:', error);
    roundContextById[session.roundId] = {
      placement: null,
      totalParticipants: null,
      teamResult: 'unknown',
      playerTeamLabel: null,
      loading: false,
    };
  }
};

watch(
  compactSessions,
  (sessions) => {
    sessions.forEach((session) => hydrateRoundContext(session));
  },
  { immediate: true }
);

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
        :class="getResultBadgeClass(roundContextById[session.roundId || '']?.teamResult ?? 'unknown')"
        :title="roundContextById[session.roundId || '']?.playerTeamLabel ? `Team: ${roundContextById[session.roundId || ''].playerTeamLabel}` : 'Team result unavailable'"
      >
        {{ getResultLabel(roundContextById[session.roundId || '']?.teamResult ?? 'unknown') }}
      </span>
      <span class="font-bold text-amber-300">{{ session.totalScore.toLocaleString() }}</span>
      <span class="text-neutral-400 font-mono">{{ formatPlacement(roundContextById[session.roundId || '']?.placement ?? null) }}</span>
    </button>
    <router-link
      :to="`/players/${encodeURIComponent(playerName)}/sessions`"
      class="text-[10px] text-neutral-400 hover:text-neutral-200 transition-colors"
    >
      All
    </router-link>
  </div>
</template>
