<script setup lang="ts">
import { ref, onMounted, watch, onUnmounted, nextTick } from 'vue'
import * as d3 from 'd3'
import { fetchPlayerNetworkGraph, type PlayerNetworkGraph, type NetworkEdge } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  playerName: string
}>()

const width = ref(800)
const height = ref(600)
const depth = ref(2)
const maxNodes = ref(100)
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

const formatDate = (dateStr?: string) => {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString()
}

const initializeD3 = () => {
  if (!svgElement.value) return

  // Clean up previous simulation
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
    .force('link', d3.forceLink().id((d: any) => d.id).distance((d: any) => 150 / Math.sqrt(d.weight || 1)))
    .force('charge', d3.forceManyBody().strength(-300))
    .force('center', d3.forceCenter(width.value / 2, height.value / 2))
    .force('collision', d3.forceCollide().radius(30))
}

const updateVisualization = () => {
  if (!networkData.value || !g || !simulation) return

  g.selectAll('*').remove()

  const nodes = networkData.value.nodes.map(n => ({ ...n }))
  const links: any[] = networkData.value.edges.map((e: NetworkEdge) => ({
    source: e.source,
    target: e.target,
    weight: e.weight,
    lastInteraction: e.lastInteraction
  }))

  if (nodes.length === 0) return

  const link = g.append('g')
    .selectAll('line')
    .data(links)
    .enter().append('line')
    .attr('stroke', 'var(--portal-border-focus, #2a2a38)')
    .attr('stroke-opacity', 0.6)
    .attr('stroke-width', (d: any) => Math.max(1, Math.sqrt(d.weight)))
    .on('mouseover', function (event: MouseEvent, d: any) {
      showTooltip(event, { sessionCount: d.weight, lastPlayed: d.lastInteraction })
    })
    .on('mouseout', hideTooltip)

  const node = g.append('g')
    .selectAll('circle')
    .data(nodes)
    .enter().append('circle')
    .attr('r', (d: any) => d.id === props.playerName ? 12 : 8)
    .attr('fill', (d: any) => {
      if (d.id === props.playerName) return '#eab308'
      const isDirectConnection = links.some(l =>
        (l.source === d.id || l.target === d.id) &&
        (l.source === props.playerName || l.target === props.playerName)
      )
      return isDirectConnection ? '#00e5a0' : '#6b7280'
    })
    .attr('stroke', 'rgba(255,255,255,0.2)')
    .attr('stroke-width', 1.5)
    .style('cursor', 'pointer')
    .on('click', (_event: MouseEvent, d: any) => {
      if (d.id !== props.playerName) {
        window.location.href = `/players/${encodeURIComponent(d.id)}`
      }
    })
    .on('mouseover', function (event: MouseEvent, d: any) {
      showTooltip(event, { name: d.label })
      d3.select(this).attr('r', d.id === props.playerName ? 15 : 10)
    })
    .on('mouseout', function (_event: MouseEvent, d: any) {
      hideTooltip()
      d3.select(this).attr('r', d.id === props.playerName ? 12 : 8)
    })
    .call(d3.drag<SVGCircleElement, any>()
      .on('start', dragstarted)
      .on('drag', dragged)
      .on('end', dragended))

  const label = g.append('g')
    .selectAll('text')
    .data(nodes)
    .enter().append('text')
    .text((d: any) => d.label)
    .style('font-size', (d: any) => d.id === props.playerName ? '14px' : '10px')
    .style('fill', 'var(--portal-text-bright, #e5e7eb)')
    .style('pointer-events', 'none')
    .attr('text-anchor', 'middle')
    .attr('dy', -15)

  simulation.nodes(nodes as any).on('tick', () => {
    link
      .attr('x1', (d: any) => d.source.x)
      .attr('y1', (d: any) => d.source.y)
      .attr('x2', (d: any) => d.target.x)
      .attr('y2', (d: any) => d.target.y)

    node
      .attr('cx', (d: any) => d.x)
      .attr('cy', (d: any) => d.y)

    label
      .attr('x', (d: any) => d.x)
      .attr('y', (d: any) => d.y)
  })

  simulation.force<d3.ForceLink<any, any>>('link')!.links(links)
  simulation.alpha(1).restart()
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
  if (svg && zoom) {
    svg.transition().duration(750).call(zoom.transform, d3.zoomIdentity)
  }
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
    <!-- Loading overlay - SVG stays in DOM -->
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
            <option :value="50">50</option>
            <option :value="100">100</option>
            <option :value="150">150</option>
            <option :value="200">200</option>
          </select>
        </label>
        <button class="w-full mt-1 px-3 py-1 text-xs bg-[var(--portal-accent-dim,rgba(0,229,160,0.12))] text-[var(--portal-accent,#00e5a0)] border border-[var(--portal-accent,#00e5a0)]/30 rounded hover:bg-[var(--portal-accent-dim)]/60 transition-colors" @click="resetZoom">
          Reset View
        </button>
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
      </div>
    </div>

    <!-- SVG Container - always in DOM -->
    <svg ref="svgElement" :width="width" :height="height" class="w-full rounded-lg" style="background: var(--portal-bg, #06060a)">
    </svg>

    <!-- Tooltip -->
    <div ref="tooltip" class="absolute hidden bg-neutral-800 text-neutral-200 p-2 rounded shadow-lg text-sm z-20 border border-neutral-700">
      <div class="font-medium">{{ tooltipData.name }}</div>
      <div v-if="tooltipData.sessionCount" class="text-neutral-400 text-xs">
        Sessions: {{ tooltipData.sessionCount }}
      </div>
      <div v-if="tooltipData.lastPlayed" class="text-neutral-400 text-xs">
        Last played: {{ formatDate(tooltipData.lastPlayed) }}
      </div>
    </div>
  </div>
</template>
