<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import * as d3 from 'd3'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const loading = ref(false)
const error = ref<string | null>(null)
const svgElement = ref<SVGSVGElement | null>(null)

let simulation: d3.Simulation<any, undefined> | null = null
let svg: d3.Selection<SVGSVGElement, unknown, null, undefined> | null = null
let currentGroup: any = null
let keyboardHandler: ((e: KeyboardEvent) => void) | null = null

const width = 1200
const height = 700

const renderVisualization = () => {
  try {
    console.log('=== renderVisualization START ===')
    console.log('svgElement.value:', svgElement.value)
    console.log('community.members:', props.community.members.length)

    if (!svgElement.value) {
      console.error('SVG element not found')
      error.value = 'SVG element not found'
      loading.value = false
      return
    }

    if (props.community.members.length === 0) {
      console.error('No members to display')
      error.value = 'No members to display'
      loading.value = false
      return
    }

    console.log('Stopping existing simulation')
    simulation?.stop()

    console.log('Selecting SVG element')
    svg = d3.select(svgElement.value)
    console.log('SVG selected:', svg)
    svg.selectAll('*').remove()
    svg.attr('viewBox', `0 0 ${width} ${height}`)
    console.log('SVG cleared and viewBox set')

    // Create nodes: each member is a node
    console.log('Creating nodes...')
    const nodes = props.community.members.map(member => ({
      id: member,
      label: member,
      isCore: props.community.coreMembers.includes(member),
      type: 'player'
    })) as any[]
    console.log('Nodes created:', nodes.length, nodes)

    // Create edges: simulate relationships between members
    // In a real app, this would come from the backend
    const edges: any[] = []
    const memberSet = new Set(props.community.members)

    // Create a basic mesh of connections - core members connect to many others
    props.community.coreMembers.forEach(coreMember => {
      const otherMembers = nodes.filter(n => n.id !== coreMember)
      const connectionCount = Math.min(
        Math.floor(otherMembers.length * 0.6),
        Math.max(3, Math.floor(Math.random() * 8))
      )

      for (let i = 0; i < connectionCount; i++) {
        const randomIdx = Math.floor(Math.random() * otherMembers.length)
        const target = otherMembers[randomIdx]
        const edgeId = [coreMember, target.id].sort().join('-')

        if (!edges.find(e => `${e.source}-${e.target}` === edgeId && `${e.target}-${e.source}` !== edgeId)) {
          edges.push({
            source: coreMember,
            target: target.id,
            weight: Math.floor(Math.random() * 8) + 2
          })
        }
      }
    })

    // Add some edges between regular members
    for (let i = 0; i < Math.floor(nodes.length * 0.3); i++) {
      const idx1 = Math.floor(Math.random() * nodes.length)
      let idx2 = Math.floor(Math.random() * nodes.length)
      while (idx2 === idx1) idx2 = Math.floor(Math.random() * nodes.length)

      const n1 = nodes[idx1]
      const n2 = nodes[idx2]
      const edgeId = [n1.id, n2.id].sort().join('-')

      if (!edges.find(e => `${e.source}-${e.target}` === edgeId && `${e.target}-${e.source}` !== edgeId)) {
        edges.push({
          source: n1.id,
          target: n2.id,
          weight: Math.floor(Math.random() * 6) + 1
        })
      }
    }

    // Force simulation
    simulation = d3.forceSimulation(nodes)
      .force('link', d3.forceLink(edges)
        .id((d: any) => d.id)
        .distance(150)
        .strength(0.4)
      )
      .force('charge', d3.forceManyBody().strength(-1200))
      .force('center', d3.forceCenter(width / 2, height / 2))
      .force('collision', d3.forceCollide().radius(45))
      .alphaDecay(0.02)

    // Pre-warm
    for (let i = 0; i < 80; i++) {
      simulation.tick()
    }

    const g = svg.append('g')
    currentGroup = g

    // Draw edges
    console.log('Drawing edges, count:', edges.length)
    const link = g.append('g')
      .selectAll('line')
      .data(edges)
      .enter()
      .append('line')
      .attr('stroke', '#6b7280')
      .attr('stroke-opacity', 0.4)
      .attr('stroke-width', (d: any) => 1 + (d.weight / 10) * 2)
    console.log('Edges appended:', link.size())

    // Draw nodes
    console.log('Drawing nodes...')
    const node = g.append('g')
      .selectAll('circle')
      .data(nodes)
      .enter()
      .append('circle')
      .attr('r', (d: any) => d.isCore ? 12 : 8)
      .attr('fill', (d: any) => d.isCore ? '#00e5a0' : '#6b7280')
      .attr('stroke', 'rgba(255,255,255,0.2)')
      .attr('stroke-width', 1.5)
      .style('cursor', 'pointer')
      .on('mouseover', function (event: MouseEvent, d: any) {
        d3.select(this)
          .attr('r', d.isCore ? 16 : 12)
          .attr('stroke', '#fff')
          .attr('stroke-width', 2)
      })
      .on('mouseout', function (event: MouseEvent, d: any) {
        d3.select(this)
          .attr('r', d.isCore ? 12 : 8)
          .attr('stroke', 'rgba(255,255,255,0.2)')
          .attr('stroke-width', 1.5)
      })
      .on('click', (_event: MouseEvent, d: any) => {
        window.location.href = `/players/${encodeURIComponent(d.id)}`
      })

    console.log('Nodes appended:', node.size())

    // Draw labels
    console.log('Drawing labels...')
    const labels = g.append('g')
      .selectAll('text')
      .data(nodes)
      .enter()
      .append('text')
      .text((d: any) => d.label)
      .style('font-size', '9px')
      .style('font-weight', (d: any) => d.isCore ? '600' : '400')
      .style('fill', (d: any) => d.isCore ? '#00e5a0' : '#9ca3af')
      .style('pointer-events', 'none')
      .attr('text-anchor', 'middle')
      .attr('dy', 12)

    console.log('Labels appended:', labels.size())

    // Drag behavior
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

    // Simulation update
    let tickCount = 0
    simulation.on('tick', () => {
      tickCount++
      if (tickCount === 1 || tickCount === 2 || tickCount % 20 === 0) {
        console.log(`Tick ${tickCount}, first node:`, nodes[0])
      }

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
        console.log('zoomToFit called on tick 1')
        zoomToFit(g)
      }
    })

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
        console.error('Error in zoomToFit:', e)
      }
    }

    console.log('Starting simulation')
    simulation.alpha(0.5).restart()
    console.log('=== renderVisualization END ===')
    loading.value = false
  } catch (err) {
    console.error('Error in renderVisualization:', err)
    error.value = 'Failed to render visualization'
    loading.value = false
  }
}

const resetView = () => {
  if (!currentGroup) return

  try {
    const bounds = currentGroup.node().getBBox()
    const fullWidth = width
    const fullHeight = height
    const midX = bounds.x + bounds.width / 2
    const midY = bounds.y + bounds.height / 2

    if (bounds.width > 0 && bounds.height > 0) {
      const scale = 0.85 / Math.max(bounds.width / fullWidth, bounds.height / fullHeight)
      const translate = [fullWidth / 2 - scale * midX, fullHeight / 2 - scale * midY]

      currentGroup.transition()
        .duration(750)
        .attr('transform', `translate(${translate[0]},${translate[1]})scale(${scale})`)
    }
  } catch (e) {
    console.error('Error resetting view:', e)
  }
}

onMounted(async () => {
  console.log('CommunityNetworkGraph mounted')
  console.log('svgElement ref:', svgElement.value)

  // Wait for DOM to render
  await nextTick()
  console.log('After nextTick, svgElement ref:', svgElement.value)

  // Additional wait to ensure SVG is ready
  setTimeout(() => {
    console.log('Calling renderVisualization after timeout')
    renderVisualization()
  }, 100)

  keyboardHandler = (e: KeyboardEvent) => {
    if (e.key.toLowerCase() === 'r' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault()
      resetView()
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
</script>

<template>
  <div class="space-y-4">
    <!-- Loading State -->
    <div v-if="loading" class="explorer-card">
      <div class="explorer-card-body text-center py-8">
        <div class="animate-spin inline-block w-8 h-8 border-4 border-cyan-500 border-t-transparent rounded-full mb-4" />
        <p class="text-neutral-400">Rendering network visualization...</p>
      </div>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="explorer-card">
      <div class="explorer-card-body text-center">
        <p class="text-red-400 mb-4">{{ error }}</p>
      </div>
    </div>

    <!-- Visualization -->
    <div v-else class="explorer-card">
      <div class="explorer-card-header flex items-center justify-between">
        <h2 class="font-mono font-bold text-cyan-300">PLAYER NETWORK</h2>
        <button
          @click="resetView"
          class="px-3 py-1 text-sm bg-cyan-500/20 border border-cyan-500/50 rounded text-cyan-300 hover:bg-cyan-500/30 transition-colors"
          title="Reset view (Ctrl+R)"
        >
          ⟲ Reset
        </button>
      </div>
      <div class="explorer-card-body p-0 overflow-hidden rounded-b">
        <svg
          ref="svgElement"
          :width="width"
          :height="height"
          style="background: var(--portal-surface, #0f0f15); display: block; width: 100%; height: auto"
        />
      </div>
    </div>

    <!-- Legend -->
    <div v-if="!loading && !error" class="grid grid-cols-2 gap-3">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-4 h-4 rounded-full bg-green-500" />
            <span class="text-sm text-neutral-400">Core Players</span>
          </div>
          <p class="text-xs text-neutral-500">Most connected members</p>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="flex items-center gap-2 mb-2">
            <div class="w-3 h-3 rounded-full bg-gray-500" />
            <span class="text-sm text-neutral-400">Regular Players</span>
          </div>
          <p class="text-xs text-neutral-500">Other community members</p>
        </div>
      </div>
    </div>

    <!-- Network Stats -->
    <div v-if="!loading && !error" class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Cohesion Score</div>
          <div class="text-2xl font-bold text-purple-400 font-mono">{{ (props.community.cohesionScore * 100).toFixed(0) }}%</div>
          <div class="text-xs text-neutral-500 mt-2">
            How tightly connected the community is overall
          </div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Network Density</div>
          <div class="text-2xl font-bold text-green-400 font-mono">
            {{ ((props.community.avgSessionsPerPair / 10) * 100).toFixed(0) }}%
          </div>
          <div class="text-xs text-neutral-500 mt-2">
            Average interaction frequency between members
          </div>
        </div>
      </div>
    </div>

    <!-- Network Info -->
    <div v-if="!loading && !error" class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">NETWORK INFO</h2>
      </div>
      <div class="explorer-card-body space-y-3 text-sm">
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            <strong class="text-cyan-400">{{ props.community.memberCount }}</strong> members in this community
            with <strong class="text-cyan-400">{{ props.community.coreMembers.length }}</strong> core members
          </p>
        </div>
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            Members play together an average of <strong class="text-green-400">{{ props.community.avgSessionsPerPair.toFixed(1) }}</strong> times
          </p>
        </div>
        <div class="p-3 bg-neutral-800/50 rounded">
          <p class="text-neutral-300">
            Community Type: <strong class="text-purple-400">
              {{ props.community.cohesionScore > 0.7 ? 'Tight-knit squad' : props.community.cohesionScore > 0.5 ? 'Active community' : 'Emerging group' }}
            </strong>
          </p>
        </div>
      </div>
    </div>

    <!-- Interaction Info -->
    <div v-if="!loading && !error" class="explorer-card">
      <div class="explorer-card-body text-sm text-neutral-300 space-y-2">
        <p>👆 <strong>Drag</strong> nodes to reposition them</p>
        <p>🖱️ <strong>Click</strong> on players to view their profiles</p>
        <p>⟲ <strong>Reset</strong> button or <strong>Ctrl+R</strong> to restore the view</p>
      </div>
    </div>
  </div>
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
</style>
