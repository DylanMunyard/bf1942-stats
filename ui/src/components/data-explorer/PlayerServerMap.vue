<script setup lang="ts">
import { ref, onMounted, watch, onUnmounted, computed } from 'vue'
import * as d3 from 'd3'
import { fetchPlayerServerMap, type CommunityServerMap } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  playerName: string
}>()

const loading = ref(true)
const error = ref<string | null>(null)
const serverMapData = ref<CommunityServerMap | null>(null)
const svgElement = ref<SVGSVGElement | null>(null)
const isMaximized = ref(false)

let simulation: d3.Simulation<any, undefined> | null = null
let svg: d3.Selection<SVGSVGElement, unknown, null, undefined> | null = null
let currentGroup: any = null
let zoomBehavior: any = null
let keyboardHandler: ((e: KeyboardEvent) => void) | null = null

const width = 600
const height = 350

const stats = computed(() => {
  if (!serverMapData.value) return null
  return {
    players: serverMapData.value.players.length,
    servers: serverMapData.value.servers.length,
    connections: serverMapData.value.edges.length,
    maxSessions: Math.max(...serverMapData.value.edges.map(e => e.weight), 1)
  }
})

const resetView = () => {
  if (!currentGroup || !svg || !zoomBehavior) return

  try {
    const bounds = currentGroup.node().getBBox()
    const fullWidth = width
    const fullHeight = height
    const midX = bounds.x + bounds.width / 2
    const midY = bounds.y + bounds.height / 2

    if (bounds.width > 0 && bounds.height > 0) {
      const scale = 0.85 / Math.max(bounds.width / fullWidth, bounds.height / fullHeight)
      const translate = [fullWidth / 2 - scale * midX, fullHeight / 2 - scale * midY]

      const transform = d3.zoomIdentity
        .translate(translate[0], translate[1])
        .scale(scale)

      svg.transition()
        .duration(750)
        .call(zoomBehavior.transform as any, transform)
    }
  } catch (e) {
    console.error('Error resetting view:', e)
  }
}

const loadServerMap = async () => {
  loading.value = true
  error.value = null

  try {
    serverMapData.value = await fetchPlayerServerMap(props.playerName)

    loading.value = false

    await new Promise(resolve => setTimeout(resolve, 50))
    renderVisualization()
  } catch (err) {
    error.value = 'Failed to load server map'
    console.error('Error loading server map:', err)
    loading.value = false
  }
}

const renderVisualization = () => {
  try {
    if (!serverMapData.value) {
      console.error('Missing server map data')
      return
    }

    if (!svgElement.value) {
      console.error('Missing SVG element ref')
      return
    }

    simulation?.stop()

    svg = d3.select(svgElement.value)

    if (!svg.node()) {
      console.error('SVG selection is empty')
      return
    }

    svg.selectAll('*').remove()
    svg.attr('viewBox', `0 0 ${width} ${height}`)
  } catch (e) {
    console.error('Error in SVG setup:', e)
    throw e
  }

  try {
    const allNodes = [
      ...serverMapData.value.servers,
      ...serverMapData.value.players
    ] as any[]

    const links = serverMapData.value.edges.map(e => ({
      ...e,
      source: e.source,
      target: e.target
    })) as any[]

    simulation = d3.forceSimulation(allNodes)
      .force('link', d3.forceLink(links)
        .id((d: any) => d.id)
        .distance(120)
        .strength(0.8)
      )
      .force('charge', d3.forceManyBody().strength(-800))
      .force('center', d3.forceCenter(width / 2, height / 2))
      .force('collision', d3.forceCollide().radius(35))
      .alphaDecay(0.02)

    for (let i = 0; i < 80; i++) {
      simulation.tick()
    }

    const g = svg.append('g')
    currentGroup = g

    zoomBehavior = d3.zoom<SVGSVGElement, any>()
      .scaleExtent([0.5, 4])
      .on('zoom', (event: any) => {
        g.attr('transform', event.transform)
      })

    svg.call(zoomBehavior as any)

    const link = g.append('g')
      .selectAll('line')
      .data(links)
      .enter()
      .append('line')
      .attr('stroke', (d: any) => {
        const lastPlayed = new Date(d.lastPlayed)
        const daysSince = (Date.now() - lastPlayed.getTime()) / (1000 * 60 * 60 * 24)

        if (daysSince < 7) return '#10b981'
        if (daysSince < 30) return '#06b6d4'
        return '#6b7280'
      })
      .attr('stroke-opacity', (d: any) => {
        const maxWeight = stats.value?.maxSessions || 1
        return 0.3 + (d.weight / maxWeight) * 0.7
      })
      .attr('stroke-width', (d: any) => {
        const maxWeight = stats.value?.maxSessions || 1
        return 1 + (d.weight / maxWeight) * 4
      })

    const node = g.append('g')
      .selectAll('circle')
      .data(allNodes)
      .enter()
      .append('circle')
      .attr('r', (d: any) => d.type === 'server' ? 18 : d.isCore ? 12 : 8)
      .attr('fill', (d: any) => {
        if (d.type === 'server') return '#8b5cf6'
        if (d.isCore) return '#00e5a0'
        return '#6b7280'
      })
      .attr('stroke', 'rgba(255,255,255,0.2)')
      .attr('stroke-width', 1.5)
      .style('cursor', 'pointer')
      .on('mouseover', function (event: MouseEvent, d: any) {
        d3.select(this)
          .attr('r', d.type === 'server' ? 24 : d.isCore ? 16 : 12)
          .attr('stroke', '#fff')
          .attr('stroke-width', 2)
      })
      .on('mouseout', function (event: MouseEvent, d: any) {
        d3.select(this)
          .attr('r', d.type === 'server' ? 18 : d.isCore ? 12 : 8)
          .attr('stroke', 'rgba(255,255,255,0.2)')
          .attr('stroke-width', 1.5)
      })
      .on('click', (_event: MouseEvent, d: any) => {
        if (d.type === 'player') {
          window.location.href = `/players/${encodeURIComponent(d.id)}`
        } else if (d.type === 'server') {
          window.location.href = `/servers/${encodeURIComponent(d.label)}`
        }
      })

    const labels = g.append('g')
      .selectAll('text')
      .data(allNodes)
      .enter()
      .append('text')
      .text((d: any) => d.label)
      .style('font-size', (d: any) => d.type === 'server' ? '10px' : '9px')
      .style('font-weight', (d: any) => d.type === 'server' ? '600' : d.isCore ? '600' : '400')
      .style('fill', (d: any) => {
        if (d.type === 'server') return '#d8b4fe'
        if (d.isCore) return '#00e5a0'
        return '#9ca3af'
      })
      .style('pointer-events', 'none')
      .attr('text-anchor', 'middle')
      .attr('dy', (d: any) => d.type === 'server' ? 28 : d.isCore ? 20 : 16)

    let tickCount = 0
    simulation.on('tick', () => {
      tickCount++

      link
        .attr('x1', (d: any) => d.source.x)
        .attr('y1', (d: any) => d.source.y)
        .attr('x2', (d: any) => d.target.x)
        .attr('y2', (d: any) => d.target.y)

      node
        .attr('cx', (d: any) => d.x)
        .attr('cy', (d: any) => d.y)

      labels
        .attr('x', (d: any) => d.x)
        .attr('y', (d: any) => d.y)

      if (tickCount === 1) {
        zoomToFit(g)
      }
    })

    node.call(d3.drag()
      .on('start', dragstarted)
      .on('drag', dragged)
      .on('end', dragended) as any)

    function dragstarted(event: any, d: any) {
      if (!event.active) simulation!.alphaTarget(0.3).restart()
      d.fx = d.x
      d.fy = d.y
    }

    function dragged(event: any, d: any) {
      d.fx = event.x
      d.fy = event.y
    }

    function dragended(event: any, d: any) {
      if (!event.active) simulation!.alphaTarget(0)
      d.fx = null
      d.fy = null
    }

    function zoomToFit(group: any) {
      try {
        const bounds = group.node().getBBox()
        const fullWidth = width
        const fullHeight = height
        const midX = bounds.x + bounds.width / 2
        const midY = bounds.y + bounds.height / 2

        if (bounds.width > 0 && bounds.height > 0) {
          const scale = 0.85 / Math.max(bounds.width / fullWidth, bounds.height / fullHeight)
          const translate = [fullWidth / 2 - scale * midX, fullHeight / 2 - scale * midY]

          group.transition()
            .duration(750)
            .attr('transform', `translate(${translate[0]},${translate[1]})scale(${scale})`)
        }
      } catch (e) {
        // Silent fail if bounds can't be calculated
      }
    }

    simulation.alpha(0.5).restart()
  } catch (e) {
    console.error('Error in visualization rendering:', e)
    throw e
  }
}

onMounted(() => {
  loadServerMap()

  keyboardHandler = (e: KeyboardEvent) => {
    if (e.key.toLowerCase() === 'r' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      resetView()
    }
    if (e.key === 'Escape' && isMaximized.value) {
      e.preventDefault()
      isMaximized.value = false
    }
  }

  window.addEventListener('keydown', keyboardHandler)
})

onUnmounted(() => {
  simulation?.stop()

  if (keyboardHandler) {
    window.removeEventListener('keydown', keyboardHandler)
  }
})

watch(() => props.playerName, () => {
  loadServerMap()
})
</script>

<template>
  <div class="space-y-4">
    <!-- Stats -->
    <div v-if="stats" class="grid grid-cols-2 sm:grid-cols-4 gap-3">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Players</div>
          <div class="text-2xl font-bold text-cyan-400 font-mono">{{ stats.players }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Servers</div>
          <div class="text-2xl font-bold text-purple-400 font-mono">{{ stats.servers }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Connections</div>
          <div class="text-2xl font-bold text-green-400 font-mono">{{ stats.connections }}</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-1">Max Sessions</div>
          <div class="text-2xl font-bold text-yellow-400 font-mono">{{ stats.maxSessions }}</div>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="loading" class="explorer-card">
      <div class="explorer-card-body text-center py-8">
        <div class="animate-spin inline-block w-8 h-8 border-4 border-cyan-500 border-t-transparent rounded-full mb-4" />
        <p class="text-neutral-400">Loading server-player network...</p>
      </div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="explorer-card">
      <div class="explorer-card-body text-center">
        <p class="text-red-400 mb-4">{{ error }}</p>
        <button
          @click="loadServerMap"
          class="px-4 py-2 bg-red-500/20 border border-red-500/50 rounded text-red-400 text-sm hover:bg-red-500/30"
        >
          Try Again
        </button>
      </div>
    </div>

    <!-- Visualization -->
    <div v-else :class="['explorer-card', isMaximized && 'fixed top-0 bottom-0 left-0 right-0 md:right-20 z-50 rounded-none m-0 max-w-none max-h-none']">
      <div class="explorer-card-header flex items-center justify-between">
        <h2 class="font-mono font-bold text-cyan-300">SERVER-PLAYER NETWORK</h2>
        <div class="flex gap-2">
          <button
            @click="resetView"
            class="px-3 py-1 text-sm bg-cyan-500/20 border border-cyan-500/50 rounded text-cyan-300 hover:bg-cyan-500/30 transition-colors"
            title="Reset view (Ctrl+R)"
          >
            ⟲ Reset
          </button>
          <button
            @click="isMaximized = !isMaximized"
            class="px-3 py-1 text-sm bg-cyan-500/20 border border-cyan-500/50 rounded text-cyan-300 hover:bg-cyan-500/30 transition-colors"
            :title="isMaximized ? 'Collapse (Esc)' : 'Expand to fullscreen'"
          >
            {{ isMaximized ? '✕ Close' : '⛶ Expand' }}
          </button>
        </div>
      </div>
      <div :class="['explorer-card-body p-0 overflow-hidden', isMaximized ? 'flex-1' : 'rounded-b']">
        <div class="flex justify-center">
          <svg
            ref="svgElement"
            :width="width"
            :height="height"
            style="background: var(--portal-surface, #0f0f15); display: block; max-width: 100%; height: auto"
          />
        </div>
      </div>
    </div>

    <!-- Legend -->
    <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-4 h-4 rounded-full bg-purple-500" />
            <span class="text-sm text-neutral-400">Servers</span>
          </div>
          <p class="text-xs text-neutral-500">Servers you play on</p>
        </div>
      </div>

      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-3 h-3 rounded-full bg-green-500" />
            <span class="text-sm text-neutral-400">You</span>
          </div>
          <p class="text-xs text-neutral-500">Player you're viewing</p>
        </div>
      </div>

      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-2 h-2 rounded-full bg-gray-500" />
            <span class="text-sm text-neutral-400">Teammates</span>
          </div>
          <p class="text-xs text-neutral-500">Players you've played with</p>
        </div>
      </div>

      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="h-0.5 w-6 bg-green-500" />
            <span class="text-sm text-neutral-400">Recent Activity</span>
          </div>
          <p class="text-xs text-neutral-500">Played in last 7 days</p>
        </div>
      </div>

      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="h-0.5 w-6 bg-cyan-500" />
            <span class="text-sm text-neutral-400">Moderate Activity</span>
          </div>
          <p class="text-xs text-neutral-500">Played in last 30 days</p>
        </div>
      </div>

      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="h-0.5 w-6 bg-gray-500" />
            <span class="text-sm text-neutral-400">Old Activity</span>
          </div>
          <p class="text-xs text-neutral-500">Played 30+ days ago</p>
        </div>
      </div>
    </div>

  </div>

  <!-- Backdrop for maximized state -->
  <div v-if="isMaximized" class="fixed inset-0 bg-black/40 z-40" @click="isMaximized = false" />
</template>

<style scoped>
.explorer-card-header {
  padding: 1rem;
  border-bottom: 1px solid var(--portal-border, #1a1a24);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.explorer-card-header h2 {
  margin: 0;
  font-size: 0.875rem;
}

.explorer-card.fixed {
  display: flex;
  flex-direction: column;
  background: var(--portal-surface, #0f0f15);
  border: 1px solid var(--portal-border, #1a1a24);
}

.explorer-card-body.flex-1 {
  display: flex;
  align-items: center;
  justify-content: center;
}

svg {
  max-width: 100%;
  max-height: 100%;
}
</style>
