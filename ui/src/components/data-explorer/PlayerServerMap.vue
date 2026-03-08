<script setup lang="ts">
import { ref, onMounted, watch, onUnmounted, computed, nextTick } from 'vue'
import * as d3 from 'd3'
import { fetchPlayerServerMap, fetchCommunityServerMap, type CommunityServerMap } from '@/services/playerRelationshipsApi'

const BANDS = [
  { label: 'Primary', min: 25, color: '#8b5cf6', bg: 'rgba(139,92,246,0.08)' },
  { label: 'Regular', min: 10, color: '#22d3ee', bg: 'rgba(34,211,238,0.08)' },
  { label: 'Occasional', min: 3, color: '#f59e0b', bg: 'rgba(245,158,11,0.08)' },
  { label: 'Rare', min: 0, color: '#6b7280', bg: 'rgba(107,114,128,0.08)' },
] as const

const getBand = (sessions: number) => BANDS.find(b => sessions >= b.min)!
const getBandIndex = (sessions: number) => BANDS.findIndex(b => sessions >= b.min)

const props = defineProps<{
  playerName?: string
  communityId?: string
}>()

const svgElement = ref<SVGSVGElement | null>(null)
const containerRef = ref<HTMLDivElement | null>(null)
const width = ref(600)
const height = ref(600)
const loading = ref(false)
const error = ref<string | null>(null)
const searchQuery = ref('')
const hoveredServer = ref<string | null>(null)
const showAll = ref(true)
const activeBand = ref<number | null>(null)
const zoomTransform = ref<d3.ZoomTransform>(d3.zoomIdentity)
const isFullscreen = ref(false)
const rawData = ref<CommunityServerMap | null>(null)

interface ServerNode {
  id: string
  label: string
  sessions: number
  playerCount: number
  lastPlayed: string
}

const allServers = ref<ServerNode[]>([])

const centerLabel = computed(() => {
  if (props.communityId) return props.communityId.substring(0, 10)
  return props.playerName || '?'
})

const fetchData = async () => {
  loading.value = true
  error.value = null
  try {
    if (props.communityId) {
      rawData.value = await fetchCommunityServerMap(props.communityId)
    } else if (props.playerName) {
      rawData.value = await fetchPlayerServerMap(props.playerName)
    } else {
      error.value = 'No player or community specified'
      loading.value = false
      return
    }

    // Aggregate edges per server
    const serverMap = new Map<string, { sessions: number; playerCount: Set<string>; lastPlayed: string }>()
    const serverLabels = new Map<string, string>()
    for (const s of rawData.value.servers) {
      serverLabels.set(s.id, s.label)
    }

    for (const edge of rawData.value.edges) {
      // Edges connect players to servers
      const serverId = serverLabels.has(edge.target) ? edge.target : edge.source
      const playerId = serverId === edge.target ? edge.source : edge.target

      const existing = serverMap.get(serverId)
      if (existing) {
        existing.sessions += edge.weight
        existing.playerCount.add(playerId)
        if (edge.lastPlayed > existing.lastPlayed) existing.lastPlayed = edge.lastPlayed
      } else {
        serverMap.set(serverId, {
          sessions: edge.weight,
          playerCount: new Set([playerId]),
          lastPlayed: edge.lastPlayed,
        })
      }
    }

    allServers.value = Array.from(serverMap.entries())
      .map(([id, data]) => ({
        id,
        label: serverLabels.get(id) || id.substring(0, 8),
        sessions: data.sessions,
        playerCount: data.playerCount.size,
        lastPlayed: data.lastPlayed,
      }))
      .sort((a, b) => b.sessions - a.sessions)

    await nextTick()
    renderOrbit()
  } catch {
    error.value = 'Failed to load server map'
  } finally {
    loading.value = false
  }
}

const searchTerms = computed(() => {
  const raw = searchQuery.value.trim().toLowerCase()
  if (!raw) return []
  return raw.split(',').map(t => t.trim()).filter(t => t.length > 0)
})

const isSearchMatch = (label: string) => {
  if (searchTerms.value.length === 0) return false
  const lower = label.toLowerCase()
  return searchTerms.value.some(t => lower.includes(t))
}

const filteredData = computed(() => {
  let data = [...allServers.value]

  if (activeBand.value !== null) {
    data = data.filter(s => getBandIndex(s.sessions) === activeBand.value)
  }

  if (searchTerms.value.length > 0) {
    return data.filter(s => isSearchMatch(s.label))
  }

  if (showAll.value) return data
  return data.slice(0, 30)
})

const bandCounts = computed(() => {
  const counts = BANDS.map(b => ({ ...b, count: 0 }))
  for (const s of allServers.value) {
    const idx = getBandIndex(s.sessions)
    if (idx >= 0) counts[idx].count++
  }
  return counts
})

const totalCount = computed(() => allServers.value.length)

const toggleBand = (idx: number) => {
  activeBand.value = activeBand.value === idx ? null : idx
}

const resetZoom = () => {
  if (!svgElement.value) return
  const svg = d3.select(svgElement.value)
  const zoomBeh = (svg.node() as any).__zoom_behavior
  if (zoomBeh) {
    svg.transition().duration(300).call(zoomBeh.transform, d3.zoomIdentity)
  }
}

const hashStr = (s: string) => {
  let h = 0
  for (let i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0
  return h
}

const renderOrbit = () => {
  if (!svgElement.value) return

  const items = filteredData.value
  if (items.length === 0) {
    d3.select(svgElement.value).selectAll('*').remove()
    return
  }

  const w = width.value
  const h = height.value
  const cx = w / 2
  const cy = h / 2
  const maxRadius = Math.min(cx, cy) - 40

  const maxSessions = Math.max(...items.map(s => s.sessions), 1)

  // Inverted: high sessions = close, low = far
  const radiusScale = d3.scaleLinear()
    .domain([maxSessions, 1])
    .range([30, maxRadius])
    .clamp(true)

  const sizeScale = d3.scaleSqrt()
    .domain([1, maxSessions])
    .range([6, 20])

  const svg = d3.select(svgElement.value)
  svg.selectAll('*').remove()

  const defs = svg.append('defs')
  const filter = defs.append('filter').attr('id', 'glow')
  filter.append('feGaussianBlur').attr('stdDeviation', '3').attr('result', 'blur')
  const fMerge = filter.append('feMerge')
  fMerge.append('feMergeNode').attr('in', 'blur')
  fMerge.append('feMergeNode').attr('in', 'SourceGraphic')

  const radGrad = defs.append('radialGradient').attr('id', 'center-glow')
  radGrad.append('stop').attr('offset', '0%').attr('stop-color', '#8b5cf6').attr('stop-opacity', 0.3)
  radGrad.append('stop').attr('offset', '100%').attr('stop-color', '#8b5cf6').attr('stop-opacity', 0)

  const g = svg.append('g')

  // Zoom
  const zoomBehavior = d3.zoom<SVGSVGElement, unknown>()
    .scaleExtent([0.5, 5])
    .on('zoom', (event) => {
      g.attr('transform', event.transform)
      zoomTransform.value = event.transform
    })
  svg.call(zoomBehavior);
  (svg.node() as any).__zoom_behavior = zoomBehavior
  if (zoomTransform.value !== d3.zoomIdentity) {
    svg.call(zoomBehavior.transform, zoomTransform.value)
  }

  // Center glow
  g.append('circle').attr('cx', cx).attr('cy', cy).attr('r', 50).attr('fill', 'url(#center-glow)')

  // Ring guides at band thresholds
  for (const band of BANDS) {
    if (band.min <= 0) continue
    const r = radiusScale(band.min)
    if (r > 30 && r < maxRadius) {
      g.append('circle')
        .attr('cx', cx).attr('cy', cy).attr('r', r)
        .attr('fill', 'none').attr('stroke', band.color)
        .attr('stroke-width', 0.5).attr('stroke-dasharray', '6,4').attr('opacity', 0.3)
      g.append('text')
        .attr('x', cx + 4).attr('y', cy - r + 12)
        .attr('fill', band.color).attr('font-size', '9px')
        .attr('font-family', 'monospace').attr('opacity', 0.6)
        .text(`${band.min}+ · ${band.label}`)
    }
  }

  // Build orbit nodes
  const goldenAngle = Math.PI * (3 - Math.sqrt(5))

  type OrbitNode = {
    id: string; label: string; sessions: number; playerCount: number; lastPlayed: string;
    x: number; y: number; r: number; color: string; matched: boolean;
  }
  const orbitNodes: OrbitNode[] = []

  // Group by band for spread
  const bandBuckets: typeof items[] = BANDS.map(() => [])
  for (const item of items) {
    bandBuckets[getBandIndex(item.sessions)].push(item)
  }

  for (let bi = 0; bi < BANDS.length; bi++) {
    const bucket = bandBuckets[bi]
    if (bucket.length === 0) continue

    const bandMinSessions = BANDS[bi].min
    const bandMaxSessions = bi === 0 ? maxSessions : BANDS[bi - 1].min - 1
    const rMin = radiusScale(Math.max(bandMaxSessions, bandMinSessions)) + 8
    const rMax = radiusScale(bandMinSessions) - 4

    const sorted = [...bucket].sort((a, b) => b.sessions - a.sessions)

    for (let i = 0; i < sorted.length; i++) {
      const item = sorted[i]
      const band = getBand(item.sessions)
      const matched = isSearchMatch(item.label)
      const angle = (i * goldenAngle) + (bi * 1.2)
      const radialT = rMax > rMin ? ((hashStr(item.id) & 0xffff) / 0xffff) : 0.5
      const r = rMin + (rMax - rMin) * radialT

      orbitNodes.push({
        id: item.id,
        label: item.label,
        sessions: item.sessions,
        playerCount: item.playerCount,
        lastPlayed: item.lastPlayed,
        x: cx + r * Math.cos(angle),
        y: cy + r * Math.sin(angle),
        r: sizeScale(item.sessions),
        color: band.color,
        matched,
      })
    }
  }

  // Connection lines
  g.selectAll('.connection-line')
    .data(orbitNodes)
    .enter()
    .append('line')
    .attr('class', 'connection-line')
    .attr('x1', cx).attr('y1', cy)
    .attr('x2', d => d.x).attr('y2', d => d.y)
    .attr('stroke', d => d.color)
    .attr('stroke-width', d => d.matched ? 1.5 : 0.5)
    .attr('opacity', d => d.matched ? 0.5 : 0.1)

  // Server nodes
  const nodes = g.selectAll('.server-node')
    .data(orbitNodes)
    .enter()
    .append('g')
    .attr('class', 'server-node')
    .attr('transform', d => `translate(${d.x},${d.y})`)
    .style('cursor', 'pointer')
    .on('mouseenter', (_event: MouseEvent, d: OrbitNode) => {
      hoveredServer.value = d.id
      d3.select(_event.currentTarget as SVGGElement).select('rect')
        .transition().duration(200)
        .attr('filter', 'url(#glow)')
      g.selectAll('.connection-line')
        .filter((ld: any) => ld.id === d.id)
        .transition().duration(200)
        .attr('opacity', 0.6).attr('stroke-width', 1.5)
    })
    .on('mouseleave', (_event: MouseEvent, d: OrbitNode) => {
      hoveredServer.value = null
      d3.select(_event.currentTarget as SVGGElement).select('rect')
        .transition().duration(200)
        .attr('filter', null)
      g.selectAll('.connection-line')
        .filter((ld: any) => ld.id === d.id)
        .transition().duration(200)
        .attr('opacity', d.matched ? 0.5 : 0.1)
        .attr('stroke-width', d.matched ? 1.5 : 0.5)
    })
    .on('click', (_event: MouseEvent, d: OrbitNode) => {
      window.location.href = `/servers/${encodeURIComponent(d.label)}`
    })

  // Rounded rect for server nodes (to distinguish from player circles)
  nodes.append('rect')
    .attr('x', d => -(d.matched ? d.r * 1.3 : d.r))
    .attr('y', d => -(d.matched ? d.r * 0.8 : d.r * 0.6))
    .attr('width', d => (d.matched ? d.r * 1.3 : d.r) * 2)
    .attr('height', d => (d.matched ? d.r * 0.8 : d.r * 0.6) * 2)
    .attr('rx', 4)
    .attr('fill', d => d.color)
    .attr('opacity', d => d.matched ? 1 : 0.8)
    .attr('stroke', d => d.matched ? '#fff' : d.color)
    .attr('stroke-width', d => d.matched ? 2 : 1)
    .attr('stroke-opacity', d => d.matched ? 0.9 : 0.3)

  // Labels
  nodes.append('text')
    .attr('dy', d => -(d.matched ? d.r * 0.8 : d.r * 0.6) - 4)
    .attr('text-anchor', 'middle')
    .attr('fill', d => d.matched ? '#fff' : d.color)
    .attr('font-size', d => d.matched ? '11px' : d.r > 10 ? '10px' : '8px')
    .attr('font-weight', d => d.matched ? 'bold' : 'normal')
    .attr('font-family', 'monospace')
    .attr('opacity', d => d.matched ? 1 : (d.sessions > maxSessions * 0.3 ? 0.8 : 0.4))
    .style('pointer-events', 'none')
    .style('text-shadow', '0 0 4px rgba(0,0,0,0.9)')
    .text(d => d.label.length > 20 ? d.label.substring(0, 19) + '\u2026' : d.label)

  // Center node
  g.append('circle')
    .attr('cx', cx).attr('cy', cy).attr('r', 18)
    .attr('fill', '#0d1117')
    .attr('stroke', '#8b5cf6').attr('stroke-width', 2)
    .attr('filter', 'url(#glow)')

  g.append('text')
    .attr('x', cx).attr('y', cy + 1)
    .attr('text-anchor', 'middle').attr('dominant-baseline', 'middle')
    .attr('fill', '#8b5cf6').attr('font-size', '9px')
    .attr('font-weight', 'bold').attr('font-family', 'monospace')
    .style('pointer-events', 'none')
    .text(centerLabel.value.length > 10
      ? centerLabel.value.substring(0, 9) + '\u2026'
      : centerLabel.value)

  // Animate
  nodes.attr('opacity', 0)
    .transition().duration(400)
    .delay((_d: OrbitNode, i: number) => i * 12)
    .attr('opacity', 1)
}

const handleResize = () => {
  if (!containerRef.value) return
  if (isFullscreen.value) {
    width.value = window.innerWidth - 80 - 40
    height.value = window.innerHeight - 40
  } else {
    const rect = containerRef.value.getBoundingClientRect()
    width.value = rect.width
    height.value = Math.min(rect.width, 900)
  }
  renderOrbit()
}

const toggleFullscreen = () => {
  isFullscreen.value = !isFullscreen.value
  nextTick(() => handleResize())
}

const handleEscape = (e: KeyboardEvent) => {
  if (e.key === 'Escape' && isFullscreen.value) {
    isFullscreen.value = false
    nextTick(() => handleResize())
  }
}

let resizeObserver: ResizeObserver | null = null

onMounted(() => {
  handleResize()
  fetchData()
  resizeObserver = new ResizeObserver(handleResize)
  if (containerRef.value) resizeObserver.observe(containerRef.value)
  document.addEventListener('keydown', handleEscape)
})

onUnmounted(() => {
  resizeObserver?.disconnect()
  document.removeEventListener('keydown', handleEscape)
})

watch(() => props.playerName, () => fetchData())
watch(() => props.communityId, () => fetchData())
watch(searchQuery, () => renderOrbit())
watch(showAll, () => renderOrbit())
watch(activeBand, () => renderOrbit())

const hoveredItem = computed(() => {
  if (!hoveredServer.value) return null
  const s = allServers.value.find(d => d.id === hoveredServer.value)
  if (!s) return null
  return { ...s, band: getBand(s.sessions) }
})

const formatDate = (dateStr?: string) => {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString()
}
</script>

<template>
  <div ref="containerRef" class="server-orbit-container" :class="{ 'server-orbit--fullscreen': isFullscreen }">
    <div class="orbit-header">
      <h3 class="orbit-title">SERVER-PLAYER NETWORK</h3>
      <p class="orbit-subtitle">
        <template v-if="communityId">Community server connections by play time</template>
        <template v-else>Your servers by play time</template>
      </p>
    </div>

    <!-- Controls -->
    <div class="orbit-controls">
      <div class="control-row">
        <div class="search-wrapper">
          <input
            v-model="searchQuery"
            type="text"
            placeholder="Search servers..."
            class="search-input"
          />
          <button v-if="searchQuery" class="search-clear" @click="searchQuery = ''">&times;</button>
        </div>
      </div>
      <div class="control-row control-row--between">
        <label class="toggle-label">
          <input v-model="showAll" type="checkbox" class="toggle-checkbox" />
          <span>Show all ({{ totalCount }})</span>
        </label>
        <div class="control-actions">
          <button class="zoom-reset-btn" title="Reset zoom" @click="resetZoom">Reset zoom</button>
          <button class="zoom-reset-btn" :title="isFullscreen ? 'Exit fullscreen (ESC)' : 'Fullscreen'" @click="toggleFullscreen">
            {{ isFullscreen ? 'Exit' : 'Fullscreen' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Band summary -->
    <div class="band-summary">
      <div
        v-for="(band, idx) in bandCounts"
        :key="band.label"
        class="band-chip"
        :class="{ 'band-chip--active': activeBand === idx, 'band-chip--inactive': activeBand !== null && activeBand !== idx }"
        :style="{ borderColor: band.color, color: band.color, background: activeBand === idx ? band.bg : undefined }"
        @click="toggleBand(idx)"
      >
        <span class="band-dot" :style="{ background: band.color }"></span>
        {{ band.label }}
        <span class="band-count">{{ band.count }}</span>
      </div>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="orbit-loading">
      <div class="loading-spinner"></div>
      <span>Mapping servers...</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="orbit-error">{{ error }}</div>

    <!-- Empty -->
    <div v-else-if="filteredData.length === 0 && !loading" class="orbit-empty">
      <p v-if="allServers.length === 0">No server data available yet.</p>
      <p v-else>
        No servers{{ searchQuery ? ` matching "${searchQuery}"` : '' }}.
      </p>
      <p class="orbit-empty-hint">
        {{ allServers.length === 0 ? 'Data populates after the daily sync runs.' : 'Try adjusting your filters.' }}
      </p>
    </div>

    <!-- Visualization -->
    <div v-show="filteredData.length > 0 && !loading" class="orbit-viz">
      <svg
        ref="svgElement"
        :width="width"
        :height="height"
        :viewBox="`0 0 ${width} ${height}`"
      ></svg>

      <!-- Hover tooltip -->
      <div v-if="hoveredItem" class="orbit-tooltip">
        <div class="tooltip-name" :style="{ color: hoveredItem.band.color }">{{ hoveredItem.label }}</div>
        <div class="tooltip-row">
          <span class="tooltip-label">Sessions</span>
          <span class="tooltip-value">{{ hoveredItem.sessions }}</span>
        </div>
        <div class="tooltip-row">
          <span class="tooltip-label">Players</span>
          <span class="tooltip-value">{{ hoveredItem.playerCount }}</span>
        </div>
        <div class="tooltip-row">
          <span class="tooltip-label">Band</span>
          <span class="tooltip-value" :style="{ color: hoveredItem.band.color }">{{ hoveredItem.band.label }}</span>
        </div>
        <div class="tooltip-row">
          <span class="tooltip-label">Last played</span>
          <span class="tooltip-value">{{ formatDate(hoveredItem.lastPlayed) }}</span>
        </div>
      </div>
    </div>

    <!-- Footer -->
    <div v-if="filteredData.length > 0 && !loading" class="orbit-footer">
      {{ filteredData.length }} server{{ filteredData.length !== 1 ? 's' : '' }}
      <template v-if="activeBand !== null"> · {{ BANDS[activeBand].label }} only</template>
      <template v-if="rawData"> · {{ rawData.players.length }} players</template>
      <template v-if="zoomTransform.k !== 1"> · {{ Math.round(zoomTransform.k * 100) }}%</template>
    </div>
  </div>
</template>

<style scoped>
.server-orbit-container {
  background: var(--bg-panel, #0d1117);
  border: 1px solid var(--border-color, #30363d);
  border-radius: 8px;
  padding: 1.25rem;
  position: relative;
  overflow: hidden;
}

.orbit-header {
  margin-bottom: 0.75rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--border-color, #30363d);
}

.orbit-title {
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.12em;
  color: #8b5cf6;
  text-transform: uppercase;
  margin: 0;
  font-family: 'JetBrains Mono', monospace;
}

.orbit-subtitle {
  font-size: 0.7rem;
  color: var(--text-secondary, #8b949e);
  margin: 0.25rem 0 0 0;
  font-family: 'JetBrains Mono', monospace;
}

.orbit-controls {
  margin-bottom: 0.75rem;
  padding: 0.5rem 0.75rem;
  background: rgba(255, 255, 255, 0.02);
  border-radius: 6px;
  border: 1px solid var(--border-color, #30363d);
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.control-row { display: flex; align-items: center; }
.control-row--between { justify-content: space-between; }

.search-wrapper {
  position: relative;
  width: 100%;
}

.search-input {
  width: 100%;
  padding: 0.375rem 2rem 0.375rem 0.5rem;
  background: var(--bg-panel, #0d1117);
  border: 1px solid var(--border-color, #30363d);
  border-radius: 6px;
  color: var(--text-primary, #e6edf3);
  font-size: 0.75rem;
  font-family: 'JetBrains Mono', monospace;
  outline: none;
}

.search-input:focus { border-color: #8b5cf6; }
.search-input::placeholder { color: var(--text-secondary, #8b949e); opacity: 0.5; }

.search-clear {
  position: absolute;
  right: 0.375rem;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  color: var(--text-secondary, #8b949e);
  cursor: pointer;
  font-size: 1rem;
  line-height: 1;
  padding: 0 0.25rem;
}

.toggle-label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.75rem;
  color: var(--text-secondary, #8b949e);
  font-family: 'JetBrains Mono', monospace;
  cursor: pointer;
  user-select: none;
}

.toggle-checkbox {
  accent-color: #8b5cf6;
  width: 14px;
  height: 14px;
  cursor: pointer;
}

.zoom-reset-btn {
  background: var(--bg-panel, #0d1117);
  border: 1px solid var(--border-color, #30363d);
  border-radius: 6px;
  color: var(--text-secondary, #8b949e);
  font-size: 0.65rem;
  font-family: 'JetBrains Mono', monospace;
  padding: 0.25rem 0.5rem;
  cursor: pointer;
  transition: all 0.15s;
}

.zoom-reset-btn:hover {
  border-color: #8b5cf6;
  color: #8b5cf6;
}

.control-actions {
  display: flex;
  gap: 0.375rem;
}

.band-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
  margin-bottom: 0.75rem;
}

.band-chip {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.2rem 0.5rem;
  border: 1px solid;
  border-radius: 9999px;
  font-size: 0.65rem;
  font-family: 'JetBrains Mono', monospace;
  cursor: pointer;
  transition: all 0.15s;
  user-select: none;
}

.band-chip:hover { opacity: 0.9; }

.band-chip--active {
  font-weight: 700;
  box-shadow: 0 0 6px rgba(255,255,255,0.1);
}

.band-chip--inactive {
  opacity: 0.35;
}

.band-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
}

.band-count {
  font-weight: 700;
}

.orbit-viz {
  display: flex;
  justify-content: center;
  position: relative;
}

.orbit-viz svg {
  max-width: 100%;
  height: auto;
}

.orbit-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  padding: 4rem 0;
  color: var(--text-secondary, #8b949e);
  font-size: 0.875rem;
  font-family: 'JetBrains Mono', monospace;
}

.loading-spinner {
  width: 1.25rem;
  height: 1.25rem;
  border: 2px solid var(--border-color, #30363d);
  border-top-color: #8b5cf6;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin { to { transform: rotate(360deg); } }

.orbit-error {
  text-align: center;
  padding: 3rem 0;
  color: #f85149;
  font-size: 0.875rem;
}

.orbit-empty {
  text-align: center;
  padding: 3rem 0;
  color: var(--text-secondary, #8b949e);
  font-size: 0.875rem;
}

.orbit-empty-hint {
  font-size: 0.75rem;
  color: var(--text-secondary, #8b949e);
  opacity: 0.6;
  margin-top: 0.25rem;
}

.orbit-tooltip {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
  background: var(--bg-panel, #0d1117);
  border: 1px solid var(--border-color, #30363d);
  border-radius: 6px;
  padding: 0.625rem 0.75rem;
  pointer-events: none;
  min-width: 8rem;
}

.tooltip-name {
  font-size: 0.8rem;
  font-weight: 600;
  margin-bottom: 0.375rem;
  font-family: 'JetBrains Mono', monospace;
}

.tooltip-row {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  font-size: 0.7rem;
  line-height: 1.5;
}

.tooltip-label { color: var(--text-secondary, #8b949e); }
.tooltip-value { color: var(--text-primary, #e6edf3); font-family: 'JetBrains Mono', monospace; }

.orbit-footer {
  text-align: center;
  margin-top: 0.5rem;
  font-size: 0.65rem;
  color: var(--text-secondary, #8b949e);
  opacity: 0.6;
  font-family: 'JetBrains Mono', monospace;
}

/* Fullscreen mode */
.server-orbit--fullscreen {
  position: fixed;
  top: 0;
  left: 0;
  right: 80px;
  bottom: 0;
  z-index: 50;
  background: var(--bg-panel, #0d1117);
  padding: 1.25rem;
  border: none;
  border-radius: 0;
  overflow-y: auto;
  animation: orbit-fade-in 0.2s ease-out;
}

@keyframes orbit-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

@media (max-width: 1023px) {
  .server-orbit--fullscreen {
    right: 0;
  }
}

.server-orbit--fullscreen .orbit-viz svg {
  max-width: 100%;
  max-height: calc(100vh - 200px);
}
</style>
