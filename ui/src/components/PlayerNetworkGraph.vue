<script setup lang="ts">
import { ref, onMounted, watch, onUnmounted, nextTick, computed } from 'vue'
import * as d3 from 'd3'
import { fetchPlayerNetworkGraph, type PlayerNetworkGraph, type NetworkEdge } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  playerName: string
}>()

const width = ref(800)
const height = ref(600)
const depth = ref(1)
const maxNodes = ref(50)
const minOverlap = ref(3)
const loading = ref(false)
const error = ref<string | null>(null)
const networkData = ref<PlayerNetworkGraph | null>(null)
const showControls = ref(false)
const isMobile = ref(false)
const isFullscreen = ref(false)

const svgElement = ref<SVGSVGElement | null>(null)
const tooltip = ref<HTMLDivElement | null>(null)
const tooltipData = ref<{ name?: string; sessionCount?: number; lastPlayed?: string }>({})

let simulation: d3.Simulation<d3.SimulationNodeDatum, undefined> | null = null
let svg: d3.Selection<SVGSVGElement, unknown, null, undefined> | null = null
let g: d3.Selection<SVGGElement, unknown, null, undefined> | null = null
let zoom: d3.ZoomBehavior<SVGSVGElement, unknown> | null = null

const checkMobile = () => {
  isMobile.value = window.innerWidth < 768
}

const filteredData = computed(() => {
  if (!networkData.value) return { nodes: [], edges: [] }

  const edges = networkData.value.edges.filter(e => e.weight >= minOverlap.value)

  const connectedIds = new Set<string>()
  connectedIds.add(props.playerName)
  for (const e of edges) {
    connectedIds.add(e.source)
    connectedIds.add(e.target)
  }

  const nodes = networkData.value.nodes.filter(n => connectedIds.has(n.id))
  return { nodes, edges }
})

const fetchNetworkData = async () => {
  loading.value = true
  error.value = null

  try {
    networkData.value = await fetchPlayerNetworkGraph(props.playerName, depth.value, maxNodes.value)
    await nextTick()
    initializeD3()
    updateVisualization()
  } catch {
    error.value = 'Failed to load network data'
  } finally {
    loading.value = false
  }
}

const onFilterChange = () => {
  if (!networkData.value || !g || !simulation) return
  updateVisualization()
}

const formatDate = (dateStr?: string) => {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString()
}

const initializeD3 = () => {
  if (!svgElement.value) return

  simulation?.stop()

  svg = d3.select(svgElement.value)
  svg.selectAll('*').remove()

  zoom = d3.zoom<SVGSVGElement, unknown>()
    .scaleExtent([0.1, 10])
    .on('zoom', (event) => {
      g?.attr('transform', event.transform)
    })

  svg.call(zoom)
  g = svg.append('g')

  simulation = d3.forceSimulation()
    .force('link', d3.forceLink().id((d: any) => d.id).distance((d: any) => 200 / Math.sqrt(d.weight || 1)))
    .force('charge', d3.forceManyBody().strength(-500))
    .force('center', d3.forceCenter(width.value / 2, height.value / 2))
    .force('collision', d3.forceCollide().radius(40))
}

const updateVisualization = () => {
  if (!g || !simulation) return

  const { nodes: filteredNodes, edges: filteredEdges } = filteredData.value
  if (filteredNodes.length === 0) return

  g.selectAll('*').remove()

  const nodes = filteredNodes.map(n => ({ ...n }))
  const links: any[] = filteredEdges.map((e: NetworkEdge) => ({
    source: e.source,
    target: e.target,
    weight: e.weight,
    lastInteraction: e.lastInteraction
  }))

  const maxWeight = Math.max(1, ...links.map((l: any) => l.weight))
  const nodeWeights = new Map<string, number>()
  for (const l of links) {
    nodeWeights.set(l.source, (nodeWeights.get(l.source) || 0) + l.weight)
    nodeWeights.set(l.target, (nodeWeights.get(l.target) || 0) + l.weight)
  }
  const maxNodeWeight = Math.max(1, ...nodeWeights.values())

  const nodeRadius = (d: any) => {
    if (d.id === props.playerName) return 14
    const w = nodeWeights.get(d.id) || 1
    return 5 + 9 * (w / maxNodeWeight)
  }

  const link = g.append('g')
    .selectAll('line')
    .data(links)
    .enter().append('line')
    .attr('stroke', 'var(--portal-border-focus, #2a2a38)')
    .attr('stroke-opacity', 0.5)
    .attr('stroke-width', (d: any) => Math.max(0.5, 3 * (d.weight / maxWeight)))
    .on('mouseover', function (event: MouseEvent, d: any) {
      d3.select(this).attr('stroke-opacity', 1).attr('stroke', '#00e5a0')
      showTooltip(event, { name: `${d.source?.label || d.source} — ${d.target?.label || d.target}`, sessionCount: d.weight, lastPlayed: d.lastInteraction })
    })
    .on('mouseout', function () {
      d3.select(this).attr('stroke-opacity', 0.5).attr('stroke', 'var(--portal-border-focus, #2a2a38)')
      hideTooltip()
    })

  const node = g.append('g')
    .selectAll('circle')
    .data(nodes)
    .enter().append('circle')
    .attr('r', nodeRadius)
    .attr('fill', (d: any) => {
      if (d.id === props.playerName) return '#eab308'
      const isDirectConnection = links.some(l =>
        (l.source === d.id || l.target === d.id) &&
        (l.source === props.playerName || l.target === props.playerName)
      )
      return isDirectConnection ? '#00e5a0' : '#6b7280'
    })
    .attr('stroke', 'rgba(255,255,255,0.15)')
    .attr('stroke-width', 1.5)
    .style('cursor', 'pointer')
    .on('click', (_event: MouseEvent, d: any) => {
      if (d.id !== props.playerName) {
        window.location.href = `/players/${encodeURIComponent(d.id)}`
      }
    })
    .on('mouseover', function (event: MouseEvent, d: any) {
      d3.select(this)
        .attr('r', nodeRadius(d) + 3)
        .attr('stroke', '#fff')
        .attr('stroke-width', 2)
      showLabel(d)
      showTooltip(event, { name: d.label, sessionCount: nodeWeights.get(d.id) })
    })
    .on('mouseout', function (_event: MouseEvent, d: any) {
      d3.select(this)
        .attr('r', nodeRadius(d))
        .attr('stroke', 'rgba(255,255,255,0.15)')
        .attr('stroke-width', 1.5)
      hideLabel()
      hideTooltip()
    })
    .call(d3.drag<SVGCircleElement, any>()
      .on('start', dragstarted)
      .on('drag', dragged)
      .on('end', dragended))

  // Create labels for all nodes
  const labels = g.append('g')
    .selectAll('text')
    .data(nodes)
    .enter().append('text')
    .attr('class', 'node-label')
    .text((d: any) => d.label)
    .style('font-size', (d: any) => d.id === props.playerName ? '12px' : '10px')
    .style('font-weight', (d: any) => d.id === props.playerName ? '600' : '400')
    .style('fill', (d: any) => {
      if (d.id === props.playerName) return '#eab308'
      const isDirectConnection = links.some(l =>
        (l.source === d.id || l.target === d.id) &&
        (l.source === props.playerName || l.target === props.playerName)
      )
      return isDirectConnection ? '#00e5a0' : '#9ca3af'
    })
    .style('pointer-events', 'none')
    .attr('text-anchor', 'middle')
    .attr('dy', (d: any) => {
      const radius = nodeRadius(d)
      return radius + 14 // Position below the node
    })
    .style('opacity', 0.9)
    .style('text-shadow', '0 0 3px rgba(0, 0, 0, 0.8), 0 0 6px rgba(0, 0, 0, 0.6)')

  let tickCount = 0
  simulation.nodes(nodes as any).on('tick', () => {
    tickCount++

    link
      .attr('x1', (d: any) => d.source.x)
      .attr('y1', (d: any) => d.source.y)
      .attr('x2', (d: any) => d.target.x)
      .attr('y2', (d: any) => d.target.y)

    node
      .attr('cx', (d: any) => d.x)
      .attr('cy', (d: any) => d.y)

    // Keep all labels positioned with their nodes
    labels
      .attr('x', (d: any) => d.x)
      .attr('y', (d: any) => d.y)

    // Auto zoom-to-fit once simulation settles
    if (tickCount === 150) {
      zoomToFit()
    }
  })

  simulation.force<d3.ForceLink<any, any>>('link')!.links(links)
  simulation.alpha(1).restart()
}

const showLabel = (d: any) => {
  if (d.id === props.playerName) return
  g?.select('.hover-label')
    .text(d.label)
    .attr('x', d.x)
    .attr('y', d.y)
    .style('display', 'block')
}

const hideLabel = () => {
  g?.select('.hover-label').style('display', 'none')
}

const zoomToFit = () => {
  if (!svg || !g || !zoom) return
  const gNode = g.node()
  if (!gNode) return

  const bounds = gNode.getBBox()
  if (bounds.width === 0 || bounds.height === 0) return

  const padding = 60
  const fullWidth = width.value
  const fullHeight = height.value
  const bWidth = bounds.width + padding * 2
  const bHeight = bounds.height + padding * 2

  const scale = Math.min(fullWidth / bWidth, fullHeight / bHeight, 1.5)
  const tx = fullWidth / 2 - (bounds.x + bounds.width / 2) * scale
  const ty = fullHeight / 2 - (bounds.y + bounds.height / 2) * scale

  svg.transition().duration(600).call(
    zoom.transform,
    d3.zoomIdentity.translate(tx, ty).scale(scale)
  )
}

const dragstarted = (event: any, d: any) => {
  if (!event.active) simulation?.alphaTarget(0.3).restart()
  d.fx = d.x
  d.fy = d.y
}

const dragged = (event: any, d: any) => {
  d.fx = event.x
  d.fy = event.y
}

const dragended = (event: any, d: any) => {
  if (!event.active) simulation?.alphaTarget(0)
  d.fx = null
  d.fy = null
}

const showTooltip = (event: MouseEvent, data: typeof tooltipData.value) => {
  tooltipData.value = data
  const el = tooltip.value
  if (!el) return
  el.style.display = 'block'
  el.style.left = `${event.pageX + 10}px`
  el.style.top = `${event.pageY - 10}px`
}

const hideTooltip = () => {
  if (tooltip.value) tooltip.value.style.display = 'none'
}

const resetZoom = () => {
  zoomToFit()
}

const toggleFullscreen = () => {
  isFullscreen.value = !isFullscreen.value
  nextTick(() => {
    handleResize()
    setTimeout(() => {
      zoomToFit()
    }, 300)
  })
}

// Handle escape key to exit fullscreen
const handleEscape = (e: KeyboardEvent) => {
  if (e.key === 'Escape' && isFullscreen.value) {
    isFullscreen.value = false
    nextTick(() => {
      handleResize()
    })
  }
}

const handleResize = () => {
  checkMobile()
  const container = svgElement.value?.parentElement
  if (container) {
    // Fullscreen mode on desktop
    if (isFullscreen.value && !isMobile.value) {
      width.value = window.innerWidth
      height.value = window.innerHeight
    }
    // On mobile, use full viewport dimensions
    else if (isMobile.value) {
      width.value = window.innerWidth
      height.value = window.innerHeight - 48 // Account for controls bar
    } else {
      width.value = container.offsetWidth
      height.value = Math.min(container.offsetWidth * 0.75, 600)
    }

    if (svg) {
      svg.attr('width', width.value).attr('height', height.value)
      simulation?.force('center', d3.forceCenter(width.value / 2, height.value / 2))
      simulation?.alpha(0.3).restart()
    }
  }
}

onMounted(() => {
  checkMobile()
  handleResize()
  window.addEventListener('resize', handleResize)
  window.addEventListener('keydown', handleEscape)
  fetchNetworkData()
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  window.removeEventListener('keydown', handleEscape)
  simulation?.stop()
})

watch(() => props.playerName, () => {
  fetchNetworkData()
})
</script>

<template>
  <div class="player-network-graph relative" :class="{ 'mobile-optimized': isMobile, 'desktop-fullscreen': isFullscreen && !isMobile }">
    <!-- Loading overlay -->
    <div v-if="loading" class="absolute inset-0 flex items-center justify-center z-10 bg-[var(--portal-bg,#06060a)]/80">
      <div class="explorer-spinner" />
    </div>

    <div v-if="error" class="text-center py-8">
      <p class="text-red-400">{{ error }}</p>
    </div>

    <!-- Desktop Fullscreen Exit Button -->
    <div v-if="isFullscreen && !isMobile" class="fullscreen-exit-hint">
      <button @click="toggleFullscreen" class="fullscreen-exit-btn" title="Exit fullscreen">
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
      <span class="fullscreen-exit-text">ESC to exit</span>
    </div>

    <!-- Mobile Controls Bar -->
    <div v-if="isMobile" class="mobile-controls-bar">
      <button class="mobile-control-btn" @click="showControls = !showControls">
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6V4m0 2a2 2 0 100 4m0-4a2 2 0 110 4m-6 8a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4m6 6v10m6-2a2 2 0 100-4m0 4a2 2 0 110-4m0 4v2m0-6V4" />
        </svg>
      </button>
      <div class="mobile-stats">
        <span class="text-[var(--portal-accent,#00e5a0)]">{{ filteredData.nodes.length }}</span> nodes
        <span class="mx-1 text-neutral-600">•</span>
        <span class="text-[var(--portal-accent,#00e5a0)]">{{ filteredData.edges.length }}</span> edges
      </div>
      <button class="mobile-control-btn" @click="resetZoom">
        <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0zM10 7v3m0 0v3m0-3h3m-3 0H7" />
        </svg>
      </button>
    </div>

    <!-- Mobile Controls Overlay -->
    <div v-if="isMobile && showControls" class="mobile-controls-overlay" @click.self="showControls = false">
      <div class="mobile-controls-panel">
        <div class="flex justify-between items-center mb-3">
          <h3 class="text-sm font-semibold text-neutral-200">Graph Controls</h3>
          <button @click="showControls = false" class="text-neutral-400 hover:text-neutral-200">
            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
        <div class="space-y-3">
          <label class="block">
            <span class="text-xs text-neutral-400">Network Depth</span>
            <select v-model.number="depth" class="mobile-select" @change="fetchNetworkData">
              <option :value="1">Direct connections only</option>
              <option :value="2">Friends of friends</option>
              <option :value="3">Extended network</option>
            </select>
          </label>
          <label class="block">
            <span class="text-xs text-neutral-400">Maximum Nodes</span>
            <select v-model.number="maxNodes" class="mobile-select" @change="fetchNetworkData">
              <option :value="25">25 nodes</option>
              <option :value="50">50 nodes</option>
              <option :value="100">100 nodes</option>
              <option :value="150">150 nodes</option>
            </select>
          </label>
          <label class="block">
            <span class="text-xs text-neutral-400">Minimum Overlap</span>
            <select v-model.number="minOverlap" class="mobile-select" @change="onFilterChange">
              <option :value="1">1+ sessions</option>
              <option :value="3">3+ sessions</option>
              <option :value="5">5+ sessions</option>
              <option :value="10">10+ sessions</option>
              <option :value="25">25+ sessions</option>
              <option :value="50">50+ sessions</option>
            </select>
          </label>
        </div>
        <!-- Mobile Legend -->
        <div class="mt-4 pt-4 border-t border-neutral-700">
          <h4 class="text-xs font-semibold text-neutral-400 mb-2">LEGEND</h4>
          <div class="grid grid-cols-2 gap-2 text-xs">
            <div class="flex items-center gap-2">
              <div class="w-2.5 h-2.5 rounded-full bg-yellow-500"></div>
              <span class="text-neutral-400">You</span>
            </div>
            <div class="flex items-center gap-2">
              <div class="w-2.5 h-2.5 rounded-full bg-[#00e5a0]"></div>
              <span class="text-neutral-400">Direct</span>
            </div>
            <div class="flex items-center gap-2">
              <div class="w-2.5 h-2.5 rounded-full bg-neutral-500"></div>
              <span class="text-neutral-400">Extended</span>
            </div>
            <div class="flex items-center gap-2">
              <div class="w-6 h-0.5 bg-neutral-600"></div>
              <span class="text-neutral-400">Strength</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Desktop Controls -->
    <div v-if="!isMobile" class="absolute top-3 right-3 bg-[var(--portal-surface-elevated,#111118)] border border-[var(--portal-border,#1a1a24)] rounded-lg p-3 text-sm z-10" :class="{ 'z-61': isFullscreen }">
      <div class="flex items-center justify-between mb-2">
        <h3 class="text-xs font-semibold text-neutral-400 uppercase tracking-wider">Controls</h3>
        <button 
          @click="toggleFullscreen" 
          class="desktop-fullscreen-btn"
          :title="isFullscreen ? 'Exit fullscreen (ESC)' : 'Enter fullscreen'"
        >
          <svg v-if="!isFullscreen" class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5l-5-5m5 5v-4m0 4h-4" />
          </svg>
          <svg v-else class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 9V5m0 0h4M9 5l-5 5m5 5v4m0 0h4m-4 0l-5-5m11-5l5 5m-5-5v4m0-4h4m-9 10l5 5m0 0v-4m0 4h-4" />
          </svg>
        </button>
      </div>
      <div class="space-y-2">
        <label class="block">
          <span class="text-xs text-neutral-500">Depth</span>
          <select v-model.number="depth" class="mt-1 block w-full text-xs bg-neutral-800 border border-neutral-700 rounded px-2 py-1 text-neutral-200" @change="fetchNetworkData">
            <option :value="1">Direct connections</option>
            <option :value="2">Friends of friends</option>
            <option :value="3">Extended network</option>
          </select>
        </label>
        <label class="block">
          <span class="text-xs text-neutral-500">Max nodes</span>
          <select v-model.number="maxNodes" class="mt-1 block w-full text-xs bg-neutral-800 border border-neutral-700 rounded px-2 py-1 text-neutral-200" @change="fetchNetworkData">
            <option :value="25">25</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
            <option :value="150">150</option>
          </select>
        </label>
        <label class="block">
          <span class="text-xs text-neutral-500">Min overlap</span>
          <select v-model.number="minOverlap" class="mt-1 block w-full text-xs bg-neutral-800 border border-neutral-700 rounded px-2 py-1 text-neutral-200" @change="onFilterChange">
            <option :value="1">1+</option>
            <option :value="3">3+</option>
            <option :value="5">5+</option>
            <option :value="10">10+</option>
            <option :value="25">25+</option>
            <option :value="50">50+</option>
          </select>
        </label>
        <button class="w-full mt-1 px-3 py-1 text-xs bg-[var(--portal-accent-dim,rgba(0,229,160,0.12))] text-[var(--portal-accent,#00e5a0)] border border-[var(--portal-accent,#00e5a0)]/30 rounded hover:bg-[var(--portal-accent-dim)]/60 transition-colors" @click="resetZoom">
          Fit to View
        </button>
      </div>
      <div class="mt-2 text-[10px] text-neutral-600 text-center">
        {{ filteredData.nodes.length }} nodes / {{ filteredData.edges.length }} edges
      </div>
      <div v-if="isFullscreen" class="mt-2 pt-2 border-t border-neutral-700/50 text-[10px] text-neutral-500 text-center">
        Press ESC to exit fullscreen
      </div>
    </div>

    <!-- Desktop Legend -->
    <div v-if="!isMobile" class="absolute bottom-3 left-3 bg-[var(--portal-surface-elevated,#111118)] border border-[var(--portal-border,#1a1a24)] rounded-lg p-3 z-10" :class="{ 'z-61': isFullscreen }">
      <h3 class="text-xs font-semibold text-neutral-400 uppercase tracking-wider mb-2">Legend</h3>
      <div class="space-y-1.5 text-xs">
        <div class="flex items-center gap-2">
          <div class="w-3 h-3 rounded-full bg-yellow-500"></div>
          <span class="text-neutral-400">You</span>
        </div>
        <div class="flex items-center gap-2">
          <div class="w-3 h-3 rounded-full bg-[#00e5a0]"></div>
          <span class="text-neutral-400">Direct connection</span>
        </div>
        <div class="flex items-center gap-2">
          <div class="w-3 h-3 rounded-full bg-neutral-500"></div>
          <span class="text-neutral-400">Extended network</span>
        </div>
        <div class="flex items-center gap-2">
          <div class="w-8 h-0.5 bg-neutral-600"></div>
          <span class="text-neutral-400">Connection strength</span>
        </div>
        <div class="mt-1 pt-1 border-t border-neutral-700/50 text-neutral-500">
          Hover nodes for names
        </div>
      </div>
    </div>

    <!-- SVG Container -->
    <svg ref="svgElement" :width="width" :height="height" class="w-full" :class="{ 'rounded-lg': !isMobile }" style="background: var(--portal-bg, #06060a)">
    </svg>

    <!-- Tooltip -->
    <div ref="tooltip" class="absolute hidden bg-neutral-800 text-neutral-200 p-2 rounded shadow-lg text-sm border border-neutral-700 pointer-events-none" :class="{ 'z-52': isFullscreen, 'z-20': !isFullscreen }">
      <div class="font-medium">{{ tooltipData.name }}</div>
      <div v-if="tooltipData.sessionCount" class="text-neutral-400 text-xs">
        Overlap: {{ tooltipData.sessionCount }}
      </div>
      <div v-if="tooltipData.lastPlayed" class="text-neutral-400 text-xs">
        Last played: {{ formatDate(tooltipData.lastPlayed) }}
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Mobile optimization styles */
.mobile-optimized {
  position: fixed;
  inset: 0;
  z-index: 40;
  display: flex;
  flex-direction: column;
  background: var(--portal-bg, #06060a);
}

.mobile-controls-bar {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 48px;
  z-index: 41;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 0.5rem;
  background: rgba(6, 6, 10, 0.98);
  backdrop-filter: blur(8px);
  border-bottom: 1px solid var(--portal-border, #1a1a24);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.mobile-control-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  background: rgba(17, 17, 24, 0.8);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 6px;
  color: var(--portal-accent, #00e5a0);
  transition: all 0.2s;
}

.mobile-control-btn:active {
  background: var(--portal-accent-dim, rgba(0, 229, 160, 0.12));
  transform: scale(0.95);
}

.mobile-stats {
  font-size: 0.7rem;
  color: var(--portal-text, #9ca3af);
  font-family: 'JetBrains Mono', monospace;
  letter-spacing: 0.02em;
  text-align: center;
  flex: 1;
}

.mobile-controls-overlay {
  position: fixed;
  inset: 0;
  z-index: 50;
  background: rgba(0, 0, 0, 0.85);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: flex-end;
  animation: fadeIn 0.2s ease-out;
}

@keyframes fadeIn {
  from { opacity: 0; }
  to { opacity: 1; }
}

.mobile-controls-panel {
  width: 100%;
  max-height: 80vh;
  background: var(--portal-surface-elevated, #111118);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 16px 16px 0 0;
  padding: 1.25rem;
  overflow-y: auto;
  animation: slideUp 0.3s ease-out;
}

@keyframes slideUp {
  from { transform: translateY(100%); }
  to { transform: translateY(0); }
}

.mobile-select {
  width: 100%;
  margin-top: 0.25rem;
  padding: 0.625rem 0.75rem;
  font-size: 0.875rem;
  background: rgba(12, 12, 18, 0.9);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 6px;
  color: var(--portal-text-bright, #e5e7eb);
  transition: all 0.2s;
}

.mobile-select:focus {
  outline: none;
  border-color: var(--portal-accent, #00e5a0);
  box-shadow: 0 0 0 3px rgba(0, 229, 160, 0.15);
}

/* Remove all margins/padding on mobile for full screen usage */
@media (max-width: 767px) {
  .player-network-graph {
    border-radius: 0 !important;
  }
  
  .player-network-graph.mobile-optimized svg {
    position: fixed;
    top: 48px; /* Height of mobile controls bar */
    left: 0;
    right: 0;
    bottom: 0;
    width: 100vw !important;
    height: calc(100vh - 48px) !important;
  }
  
  /* Hide desktop tooltip on mobile, show mobile-optimized version */
  .player-network-graph.mobile-optimized .tooltip {
    padding: 0.375rem 0.5rem;
    font-size: 0.75rem;
    max-width: 200px;
  }
}

/* Ensure controls are properly positioned */
.player-network-graph.mobile-optimized .mobile-controls-bar + svg {
  margin-top: 0;
}

/* Improve touch targets on mobile */
@media (hover: none) and (pointer: coarse) {
  .mobile-control-btn {
    min-width: 44px;
    min-height: 44px;
  }
  
  .mobile-select {
    min-height: 44px;
  }
}

/* Desktop fullscreen mode */
.desktop-fullscreen {
  position: fixed;
  inset: 0;
  z-index: 60; /* Higher than sidebar z-50 */
  background: var(--portal-bg, #06060a);
  padding: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  animation: fadeIn 0.3s ease-out;
}

.player-network-graph {
  transition: all 0.3s ease-out;
}

.desktop-fullscreen-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  background: transparent;
  background-size: 16px 16px;
  background-position: center;
  background-repeat: no-repeat;
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 4px;
  color: var(--portal-text, #9ca3af);
  transition: all 0.2s;
  cursor: pointer;
  flex-shrink: 0;
}

.desktop-fullscreen-btn:hover {
  background-color: var(--portal-accent-dim, rgba(0, 229, 160, 0.12));
  border-color: var(--portal-accent, #00e5a0);
  color: var(--portal-accent, #00e5a0);
}

.desktop-fullscreen-btn svg {
  width: 16px !important;
  height: 16px !important;
  flex-shrink: 0;
}

.fullscreen-exit-hint {
  position: fixed;
  top: 20px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 61; /* Above fullscreen container */
  display: flex;
  align-items: center;
  background: rgba(17, 17, 24, 0.95);
  backdrop-filter: blur(8px);
  border: 1px solid var(--portal-border, #1a1a24);
  border-radius: 8px;
  padding: 0.375rem 0.75rem;
  animation: slideDown 0.3s ease-out;
  font-size: 0.75rem !important;
  max-height: 36px;
}

@keyframes slideDown {
  from {
    opacity: 0;
    transform: translateX(-50%) translateY(-20px);
  }
  to {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
  }
}

.fullscreen-exit-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0.25rem;
  font-size: 0.75rem !important;
  background: transparent;
  border: none;
  color: var(--portal-accent, #00e5a0);
  cursor: pointer;
  transition: all 0.2s;
  width: 24px;
  height: 24px;
}

.fullscreen-exit-btn:hover {
  color: #00fff2;
  text-shadow: 0 0 10px rgba(0, 255, 242, 0.5);
}

.fullscreen-exit-text {
  font-size: 0.625rem !important;
  color: #6b7280;
  margin-left: 0.5rem;
  font-family: system-ui, -apple-system, sans-serif;
  font-weight: 400;
  line-height: 1;
}

/* Adjust controls and legend position in fullscreen */
.desktop-fullscreen .absolute.top-3.right-3 {
  top: 20px;
  right: 100px; /* Move away from sidebar which is 80px wide */
}

.desktop-fullscreen .absolute.bottom-3.left-3 {
  bottom: 20px;
  left: 20px;
}

/* Ensure SVG fills available space in fullscreen */
.desktop-fullscreen svg {
  width: 100% !important;
  height: 100% !important;
  max-width: calc(100vw - 40px);
  max-height: calc(100vh - 40px);
}

/* Utility for tooltip z-index in fullscreen */
.z-52 {
  z-index: 52;
}

.z-61 {
  z-index: 61;
}
</style>
