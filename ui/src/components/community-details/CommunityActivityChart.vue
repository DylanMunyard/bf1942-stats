<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import type { PlayerCommunity } from '@/services/playerRelationshipsApi'

const props = defineProps<{
  community: PlayerCommunity
}>()

const chartCanvas = ref<HTMLCanvasElement | null>(null)
let chart: any = null

// Simulate activity data based on community metadata
const activityData = computed(() => {
  const formation = new Date(props.community.formationDate)
  const lastActive = new Date(props.community.lastActiveDate)
  const now = new Date()

  const daysExisted = Math.floor((now.getTime() - formation.getTime()) / (1000 * 60 * 60 * 24))

  // Generate simulated activity data for the last 30 days
  const data = []
  for (let i = 29; i >= 0; i--) {
    const date = new Date(now)
    date.setDate(date.getDate() - i)

    // Simulate activity: higher near lastActive date
    const daysDiffFromLastActive = Math.floor((date.getTime() - lastActive.getTime()) / (1000 * 60 * 60 * 24))
    const activity = Math.max(0, 100 - Math.abs(daysDiffFromLastActive) * 8)

    data.push({
      date: date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' }),
      activity: Math.round(activity),
      members: Math.max(2, Math.round(props.community.memberCount * (0.5 + Math.random() * 0.5)))
    })
  }

  return data
})

const maxActivity = computed(() => Math.max(...activityData.value.map(d => d.activity)))
const avgActivity = computed(() => {
  const sum = activityData.value.reduce((acc, d) => acc + d.activity, 0)
  return Math.round(sum / activityData.value.length)
})

onMounted(() => {
  // Chart.js would go here in a real implementation
  // For now, we'll use a simple canvas-based visualization
})
</script>

<template>
  <div class="space-y-4">
    <!-- Summary Stats -->
    <div class="grid grid-cols-3 gap-4">
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Peak Activity</div>
          <div class="text-2xl font-bold text-cyan-400 font-mono">{{ maxActivity }}%</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Avg (30d)</div>
          <div class="text-2xl font-bold text-green-400 font-mono">{{ avgActivity }}%</div>
        </div>
      </div>
      <div class="explorer-card">
        <div class="explorer-card-body">
          <div class="text-xs text-neutral-500 uppercase tracking-wider mb-2">Trend</div>
          <div class="text-2xl font-bold text-purple-400 font-mono">
            {{ activityData[activityData.length - 1].activity > avgActivity ? '↗' : '↘' }}
          </div>
        </div>
      </div>
    </div>

    <!-- Activity Chart (Simplified) -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">ACTIVITY (LAST 30 DAYS)</h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-4">
          <!-- Chart Area -->
          <div class="bg-neutral-900 rounded border border-neutral-700/50 p-4" style="height: 300px">
            <div class="h-full flex flex-col justify-between">
              <!-- Y-axis labels -->
              <div class="flex justify-between text-xs text-neutral-500 mb-2">
                <span>100%</span>
                <span>50%</span>
                <span>0%</span>
              </div>

              <!-- Bars -->
              <div class="flex-1 flex items-flex-end justify-between gap-1 px-2">
                <div
                  v-for="(point, idx) in activityData"
                  :key="idx"
                  class="flex-1 group relative cursor-pointer"
                  :title="`${point.date}: ${point.activity}%`"
                >
                  <div
                    class="w-full bg-gradient-to-t from-cyan-500 to-cyan-400 hover:from-cyan-400 hover:to-cyan-300 transition-colors rounded-t"
                    :style="{ height: `${(point.activity / maxActivity) * 100}%`, minHeight: point.activity > 0 ? '2px' : '0px' }"
                  />
                  <div class="absolute -bottom-6 left-0 right-0 opacity-0 group-hover:opacity-100 transition-opacity text-xs text-neutral-400 text-center whitespace-nowrap pointer-events-none">
                    {{ point.date }}
                  </div>
                </div>
              </div>

              <!-- X-axis -->
              <div class="text-xs text-neutral-500 text-center mt-2">30 days ago → Today</div>
            </div>
          </div>

          <!-- Legend -->
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-2 text-xs">
            <div class="flex items-center gap-2">
              <div class="w-3 h-3 bg-cyan-500 rounded" />
              <span class="text-neutral-400">Community Activity</span>
            </div>
            <div class="flex items-center gap-2">
              <div class="w-3 h-3 bg-neutral-600 rounded" />
              <span class="text-neutral-400">Inactive</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Activity Timeline -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">RECENT ACTIVITY DAYS</h2>
      </div>
      <div class="explorer-card-body">
        <div class="space-y-2">
          <div
            v-for="(point, idx) in activityData.slice(-7).reverse()"
            :key="idx"
            class="flex items-center justify-between p-2 bg-neutral-800/30 rounded hover:bg-neutral-700/30 transition-colors"
          >
            <span class="text-sm text-neutral-300">{{ point.date }}</span>
            <div class="flex items-center gap-3">
              <div class="flex-1 w-32 h-2 bg-neutral-800 rounded-full overflow-hidden">
                <div
                  class="h-full bg-gradient-to-r from-cyan-500 to-cyan-400 rounded-full"
                  :style="{ width: `${point.activity}%` }"
                />
              </div>
              <span class="text-xs font-mono text-neutral-400 w-12 text-right">{{ point.activity }}%</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Insights -->
    <div class="explorer-card">
      <div class="explorer-card-header">
        <h2 class="font-mono font-bold text-cyan-300">ACTIVITY INSIGHTS</h2>
      </div>
      <div class="explorer-card-body space-y-2 text-sm">
        <div class="p-2 bg-neutral-800/50 rounded text-neutral-300">
          📊 Peak activity was <strong>{{ maxActivity }}%</strong> in the last 30 days
        </div>
        <div class="p-2 bg-neutral-800/50 rounded text-neutral-300">
          📈 Average activity level is <strong>{{ avgActivity }}%</strong>
        </div>
        <div class="p-2 bg-neutral-800/50 rounded text-neutral-300">
          🕐 Community has been active for <strong>{{ Math.floor((new Date().getTime() - new Date(community.formationDate).getTime()) / (1000 * 60 * 60 * 24)) }}</strong> days
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.explorer-card-header {
  padding: 1rem;
  border-bottom: 1px solid var(--portal-border, #1a1a24);
}

.explorer-card-header h2 {
  margin: 0;
  font-size: 0.875rem;
}
</style>
