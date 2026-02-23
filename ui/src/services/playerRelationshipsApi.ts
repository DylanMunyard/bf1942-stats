import axios from 'axios';

export interface PlayerRelationship {
  player1Name: string;
  player2Name: string;
  sessionCount: number;
  totalMinutes: number;
  firstPlayedTogether: string;
  lastPlayedTogether: string;
  serverGuids: string[];
  avgScoreDiff: number;
}

export interface PlayerNetworkStats {
  playerName: string;
  connectionCount: number;
  totalCoPlaySessions: number;
  serverCount: number;
  firstSeen: string;
  lastSeen: string;
}

export interface NetworkNode {
  id: string;
  label: string;
}

export interface NetworkEdge {
  source: string;
  target: string;
  weight: number;
  lastInteraction: string;
}

export interface PlayerNetworkGraph {
  centerPlayer: string;
  nodes: NetworkNode[];
  edges: NetworkEdge[];
  depth: number;
}

const BASE = '/stats/relationships';

export async function fetchPlayerTeammates(playerName: string, limit = 20): Promise<PlayerRelationship[]> {
  const response = await axios.get<PlayerRelationship[]>(
    `${BASE}/players/${encodeURIComponent(playerName)}/teammates`,
    { params: { limit } }
  );
  return response.data;
}

export async function fetchPlayerNetworkStats(playerName: string): Promise<PlayerNetworkStats> {
  const response = await axios.get<PlayerNetworkStats>(
    `${BASE}/players/${encodeURIComponent(playerName)}/network-stats`
  );
  return response.data;
}

export async function fetchPlayerNetworkGraph(
  playerName: string,
  depth = 2,
  maxNodes = 100
): Promise<PlayerNetworkGraph> {
  const response = await axios.get<PlayerNetworkGraph>(
    `${BASE}/players/${encodeURIComponent(playerName)}/network-graph`,
    { params: { depth, maxNodes } }
  );
  return response.data;
}

export async function fetchRecentConnections(
  playerName: string,
  daysSince = 30
): Promise<PlayerRelationship[]> {
  const response = await axios.get<PlayerRelationship[]>(
    `${BASE}/players/${encodeURIComponent(playerName)}/recent-connections`,
    { params: { daysSince } }
  );
  return response.data;
}

export async function fetchPotentialConnections(
  playerName: string,
  limit = 20,
  daysActive = 30
): Promise<string[]> {
  const response = await axios.get<string[]>(
    `${BASE}/players/${encodeURIComponent(playerName)}/potential-connections`,
    { params: { limit, daysActive } }
  );
  return response.data;
}

export async function fetchPlayerRelationship(
  player1: string,
  player2: string
): Promise<PlayerRelationship | null> {
  try {
    const response = await axios.get<PlayerRelationship>(
      `${BASE}/players/${encodeURIComponent(player1)}/relationship/${encodeURIComponent(player2)}`
    );
    return response.data;
  } catch (err: any) {
    if (err.response?.status === 404) return null;
    throw err;
  }
}

// Community interfaces
export interface PlayerCommunity {
  id: string;
  name: string;
  members: string[];
  coreMembers: string[];
  primaryServers: string[];  // Simplified to just server names
  formationDate: string;
  lastActiveDate: string;
  avgSessionsPerPair: number;
  cohesionScore: number;
  memberCount: number;
  isActive: boolean;
}

// Community API methods
const COMMUNITIES_BASE = '/stats/communities';

export async function fetchAllCommunities(
  minSize = 3,
  activeOnly = true
): Promise<PlayerCommunity[]> {
  const response = await axios.get<PlayerCommunity[]>(
    COMMUNITIES_BASE,
    { params: { minSize, activeOnly } }
  );
  return response.data;
}

export async function fetchCommunity(communityId: string): Promise<PlayerCommunity> {
  const response = await axios.get<PlayerCommunity>(
    `${COMMUNITIES_BASE}/${encodeURIComponent(communityId)}`
  );
  return response.data;
}

export async function fetchPlayerCommunities(playerName: string): Promise<PlayerCommunity[]> {
  const response = await axios.get<PlayerCommunity[]>(
    `${COMMUNITIES_BASE}/players/${encodeURIComponent(playerName)}`
  );
  return response.data;
}

export async function triggerCommunityDetection(): Promise<{ message: string }> {
  const response = await axios.post<{ message: string }>(
    `${COMMUNITIES_BASE}/detect`
  );
  return response.data;
}

// Server map (bipartite graph) interfaces
export interface ServerMapNode {
  id: string;
  label: string;
  type: 'player' | 'server';
  isCore: boolean;
}

export interface ServerMapEdge {
  source: string;
  target: string;
  weight: number;
  lastPlayed: string;
}

export interface CommunityServerMap {
  players: ServerMapNode[];
  servers: ServerMapNode[];
  edges: ServerMapEdge[];
}

export async function fetchCommunityServerMap(communityId: string): Promise<CommunityServerMap> {
  const response = await axios.get<CommunityServerMap>(
    `${COMMUNITIES_BASE}/${encodeURIComponent(communityId)}/server-map`
  );
  return response.data;
}
