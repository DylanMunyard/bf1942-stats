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

const svgElement = ref<SVGSVGElement | null>(null)
const tooltip = ref<HTMLDivElement | null>(null)
const tooltipData = ref<{ name?: string; sessionCount?: number; lastPlayed?: string }>({})

let simulation: d3.Simulation<d3.SimulationNodeDatum, undefined> | null = null
let svg: d3.Selection<SVGSVGElement, unknown, null, undefined> | null = null
let g: d3.Selection<SVGGElement, unknown, null, undefined> | null = null
let zoom: d3.ZoomBehavior<SVGSVGElement, unknown> | null = null

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

  // Only center player gets a permanent label
  g.append('text')
    .attr('class', 'center-label')
    .text(props.playerName)
    .style('font-size', '13px')
    .style('font-weight', '600')
    .style('fill', '#eab308')
    .style('pointer-events', 'none')
    .attr('text-anchor', 'middle')
    .attr('dy', -20)

  // Hover label (hidden by default)
  g.append('text')
    .attr('class', 'hover-label')
    .style('font-size', '11px')
    .style('fill', 'var(--portal-text-bright, #e5e7eb)')
    .style('pointer-events', 'none')
    .attr('text-anchor', 'middle')
    .attr('dy', -18)
    .style('display', 'none')

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

    // Keep center player label positioned
    const centerNode = nodes.find(n => n.id === props.playerName) as any
    if (centerNode) {
      g!.select('.center-label')
        .attr('x', centerNode.x)
        .attr('y', centerNode.y)
    }

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

const handleResize = () => {
  const container = svgElement.value?.parentElement
  if (container) {
    width.value = container.offsetWidth
    height.value = Math.min(container.offsetWidth * 0.75, 600)

    if (svg) {
      svg.attr('width', width.value).attr('height', height.value)
      simulation?.force('center', d3.forceCenter(width.value / 2, height.value / 2))
      simulation?.alpha(0.3).restart()
    }
  }
}

onMounted(() => {
  handleResize()
  window.addEventListener('resize', handleResize)
  fetchNetworkData()
})

onUnmounted(() => {
  window.removeEventListener('resize', handleResize)
  simulation?.stop()
})

watch(() => props.playerName, () => {
  fetchNetworkData()
})
</script>

<template>
  <div class="player-network-graph relative">
    <!-- Loading overlay -->
    <div v-if="loading" class="absolute inset-0 flex items-center justify-center z-10 bg-[var(--portal-bg,#06060a)]/80">
      <div class="explorer-spinner" />
    </div>

    <div v-if="error" class="text-center py-8">
      <p class="text-red-400">{{ error }}</p>
    </div>

    <!-- Controls -->
    <div class="absolute top-3 right-3 z-10 bg-[var(--portal-surface-elevated,#111118)] border border-[var(--portal-border,#1a1a24)] rounded-lg p-3 text-sm">
      <h3 class="text-xs font-semibold text-neutral-400 uppercase tracking-wider mb-2">Controls</h3>
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
    </div>

    <!-- Legend -->
    <div class="absolute bottom-3 left-3 z-10 bg-[var(--portal-surface-elevated,#111118)] border border-[var(--portal-border,#1a1a24)] rounded-lg p-3">
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
    <svg ref="svgElement" :width="width" :height="height" class="w-full rounded-lg" style="background: var(--portal-bg, #06060a)">
    </svg>

    <!-- Tooltip -->
    <div ref="tooltip" class="absolute hidden bg-neutral-800 text-neutral-200 p-2 rounded shadow-lg text-sm z-20 border border-neutral-700 pointer-events-none">
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
