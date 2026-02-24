<template>
  <div class="report-container">
    <!-- Overall Score Section -->
    <div class="overall-score-section">
      <div class="score-card">
        <div class="score-visual">
          <div class="score-circle" :class="`level-${suspicionLevel}`">
            <div class="score-value">{{ (report.overallSimilarityScore * 100).toFixed(0) }}%</div>
            <div class="score-label">Similarity</div>
          </div>
        </div>

        <div class="score-info">
          <div class="score-header">
            <h2>{{ report.player1 }} vs {{ report.player2 }}</h2>
            <div class="suspicion-badge" :class="`level-${suspicionLevel}`">
              {{ suspicionLabel }}
            </div>
          </div>

          <p class="score-confidence">
            <span class="confidence-label">Analysis Confidence:</span>
            <span class="confidence-bar">
              <span class="confidence-fill" :style="{ width: (report.analysisConfidence * 100) + '%' }"></span>
            </span>
            <span class="confidence-value">{{ (report.analysisConfidence * 100).toFixed(0) }}%</span>
          </p>

          <p class="score-meta">
            Analyzed {{ report.daysAnalyzed }} days of historical data
          </p>
        </div>
      </div>
    </div>

    <!-- Similarity Radar Chart -->
    <div class="radar-section">
      <h3>Similarity Analysis</h3>
      <SimilarityRadarChart
        :stat-analysis="report.statAnalysis"
        :behavioral-analysis="report.behavioralAnalysis"
        :network-analysis="report.networkAnalysis"
        :temporal-analysis="report.temporalAnalysis"
        :is-dark-mode="true"
      />
    </div>

    <!-- Analysis Grid -->
    <div class="analysis-grid">
      <!-- Stat Analysis -->
      <div class="analysis-card">
        <div class="card-header">
          <h3>Statistical Profile</h3>
          <span :class="['data-badge', report.statAnalysis.hasSufficientData ? 'available' : 'insufficient']">
            {{ report.statAnalysis.hasSufficientData ? 'Full Data' : 'Insufficient' }}
          </span>
        </div>
        <div class="card-body">
          <div class="score-bar">
            <div class="bar-fill" :style="{ width: (report.statAnalysis.score * 100) + '%' }"></div>
          </div>
          <p class="score-text">{{ (report.statAnalysis.score * 100).toFixed(1) }}% match</p>

          <div class="metrics">
            <div class="metric">
              <span class="metric-label">K/D Difference:</span>
              <span class="metric-value">{{ (report.statAnalysis.kdRatioDifference * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Kill Rate:</span>
              <span class="metric-value">{{ (report.statAnalysis.killRateDifference * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Map Performance:</span>
              <span class="metric-value">{{ (report.statAnalysis.mapPerformanceSimilarity * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Server Performance:</span>
              <span class="metric-value">{{ (report.statAnalysis.serverPerformanceSimilarity * 100).toFixed(0) }}%</span>
            </div>
          </div>

          <p class="analysis-text">{{ report.statAnalysis.analysis }}</p>
        </div>
      </div>

      <!-- Behavioral Analysis -->
      <div class="analysis-card">
        <div class="card-header">
          <h3>Behavioral Patterns</h3>
          <span :class="['data-badge', report.behavioralAnalysis.hasSufficientData ? 'available' : 'insufficient']">
            {{ report.behavioralAnalysis.hasSufficientData ? 'Full Data' : 'Insufficient' }}
          </span>
        </div>
        <div class="card-body">
          <div class="score-bar">
            <div class="bar-fill" :style="{ width: (report.behavioralAnalysis.score * 100) + '%' }"></div>
          </div>
          <p class="score-text">{{ (report.behavioralAnalysis.score * 100).toFixed(1) }}% match</p>

          <div class="metrics">
            <div class="metric">
              <span class="metric-label">Play Time Overlap:</span>
              <span class="metric-value">{{ (report.behavioralAnalysis.playTimeOverlapScore * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Server Affinity:</span>
              <span class="metric-value">{{ (report.behavioralAnalysis.serverAffinityScore * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Ping Consistency:</span>
              <span class="metric-value">{{ (report.behavioralAnalysis.pingConsistencyScore * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Session Pattern:</span>
              <span class="metric-value">{{ (report.behavioralAnalysis.sessionPatternScore * 100).toFixed(0) }}%</span>
            </div>
          </div>

          <p class="analysis-text">{{ report.behavioralAnalysis.analysis }}</p>
        </div>
      </div>

      <!-- Network Analysis -->
      <div class="analysis-card">
        <div class="card-header">
          <h3>Network Relationships</h3>
          <span :class="['data-badge', report.networkAnalysis.hasSufficientData ? 'available' : 'insufficient']">
            {{ report.networkAnalysis.hasSufficientData ? 'Full Data' : 'Insufficient' }}
          </span>
        </div>
        <div class="card-body">
          <div class="score-bar">
            <div class="bar-fill" :style="{ width: (report.networkAnalysis.score * 100) + '%' }"></div>
          </div>
          <p class="score-text">{{ (report.networkAnalysis.score * 100).toFixed(1) }}% match</p>

          <div class="metrics">
            <div class="metric">
              <span class="metric-label">Shared Teammates:</span>
              <span class="metric-value">{{ report.networkAnalysis.sharedTeammateCount }}</span>
            </div>
            <div class="metric">
              <span class="metric-label">Teammate Overlap:</span>
              <span class="metric-value">{{ (report.networkAnalysis.teammateOverlapPercentage * 100).toFixed(0) }}%</span>
            </div>
            <div class="metric">
              <span class="metric-label">Direct Connection:</span>
              <span class="metric-value">{{ report.networkAnalysis.hasDirectConnection ? 'Yes' : 'No' }}</span>
            </div>
          </div>

          <p class="analysis-text">{{ report.networkAnalysis.analysis }}</p>
        </div>
      </div>

      <!-- Temporal Analysis -->
      <div class="analysis-card">
        <div class="card-header">
          <h3>Temporal Consistency</h3>
          <span :class="['data-badge', report.temporalAnalysis.hasSufficientData ? 'available' : 'insufficient']">
            {{ report.temporalAnalysis.hasSufficientData ? 'Full Data' : 'Insufficient' }}
          </span>
        </div>
        <div class="card-body">
          <div class="score-bar">
            <div class="bar-fill" :style="{ width: (report.temporalAnalysis.score * 100) + '%' }"></div>
          </div>
          <p class="score-text">{{ (report.temporalAnalysis.score * 100).toFixed(1) }}% match</p>

          <div class="metrics">
            <div class="metric">
              <span class="metric-label">Temporal Overlap:</span>
              <span class="metric-value">{{ report.temporalAnalysis.temporalOverlapMinutes }} min</span>
            </div>
            <div class="metric">
              <span class="metric-label">Activity Pattern:</span>
              <span class="metric-value">{{ report.temporalAnalysis.invertedActivityScore > 0.5 ? 'Separated' : 'Overlapped' }}</span>
            </div>
            <div class="metric">
              <span class="metric-label">Co-sessions:</span>
              <span class="metric-value">{{ report.temporalAnalysis.significantTemporalOverlap ? 'Yes' : 'No' }}</span>
            </div>
          </div>

          <p class="analysis-text">{{ report.temporalAnalysis.analysis }}</p>
        </div>
      </div>
    </div>

    <!-- Evidence Section -->
    <div class="evidence-section">
      <div class="evidence-grid">
        <!-- Red Flags -->
        <div v-if="report.redFlags.length" class="evidence-card red-flags">
          <h3 class="evidence-title">Red Flags</h3>
          <ul class="evidence-list">
            <li v-for="(flag, idx) in report.redFlags" :key="idx" class="evidence-item">
              {{ flag }}
            </li>
          </ul>
        </div>

        <!-- Green Flags -->
        <div v-if="report.greenFlags.length" class="evidence-card green-flags">
          <h3 class="evidence-title">Green Flags</h3>
          <ul class="evidence-list">
            <li v-for="(flag, idx) in report.greenFlags" :key="idx" class="evidence-item">
              {{ flag }}
            </li>
          </ul>
        </div>

        <!-- No Evidence -->
        <div v-if="!report.redFlags.length && !report.greenFlags.length" class="evidence-card neutral">
          <h3 class="evidence-title">No Conclusive Evidence</h3>
          <p class="evidence-text">The analysis did not find strong indicators in either direction.</p>
        </div>
      </div>
    </div>

    <!-- Timeline Section (if available) -->
    <div v-if="report.activityTimeline" class="timeline-section">
      <h3>Activity Timeline</h3>
      <div class="timeline-card">
        <div class="timeline-content">
          <div class="timeline-period">
            <span class="period-label">{{ report.player1 }} Activity</span>
            <span class="period-dates">
              {{ formatDate(report.activityTimeline.player1Activity.firstSeen) }} →
              {{ formatDate(report.activityTimeline.player1Activity.lastSeen) }}
            </span>
            <span class="period-sessions">{{ report.activityTimeline.player1Activity.totalSessions }} sessions</span>
          </div>

          <div class="gap-info" :class="`gap-${report.activityTimeline.gap.switchoverWindowDays > 30 ? 'large' : 'tight'}`">
            <span class="gap-days">{{ report.activityTimeline.gap.switchoverWindowDays }} days</span>
            <span class="gap-description">{{ report.activityTimeline.gap.patternDescription }}</span>
          </div>

          <div class="timeline-period">
            <span class="period-label">{{ report.player2 }} Activity</span>
            <span class="period-dates">
              {{ formatDate(report.activityTimeline.player2Activity.firstSeen) }} →
              {{ formatDate(report.activityTimeline.player2Activity.lastSeen) }}
            </span>
            <span class="period-sessions">{{ report.activityTimeline.player2Activity.totalSessions }} sessions</span>
          </div>
        </div>
      </div>
    </div>

  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import SimilarityRadarChart from './SimilarityRadarChart.vue'
import type { PlayerAliasSuspicionReport } from '../types/alias-detection'

interface Props {
  report: PlayerAliasSuspicionReport
}

const props = defineProps<Props>()

const suspicionLevel = computed(() => {
  const score = props.report.overallSimilarityScore
  if (score >= 0.85) return 'very-likely'
  if (score >= 0.70) return 'likely'
  if (score >= 0.50) return 'potential'
  return 'unrelated'
})

const suspicionLabel = computed(() => {
  const level = suspicionLevel.value
  const labels: Record<string, string> = {
    'very-likely': 'Very Likely Alias',
    'likely': 'Likely Alias',
    'potential': 'Potential Alias',
    'unrelated': 'Unrelated'
  }
  return labels[level] || 'Unknown'
})

const formatDate = (date: string) => {
  return new Date(date).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric'
  })
}
</script>

<style scoped>
.report-container {
  display: grid;
  gap: 2rem;
  height: 100%;
}

/* Overall Score Section */
.overall-score-section {
  grid-column: 1 / -1;
  display: flex;
}

/* Radar Section */
.radar-section {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.radar-section > h3 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
  color: #cbd5e1;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.score-card {
  background: rgba(30, 41, 59, 0.6);
  border: 1px solid #334155;
  border-radius: 12px;
  padding: 2rem;
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 2rem;
  align-items: center;
}

.score-visual {
  position: relative;
  display: flex;
  justify-content: center;
  align-items: center;
}

.score-circle {
  width: 160px;
  height: 160px;
  border-radius: 50%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  position: relative;
  overflow: hidden;
}

.score-circle::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: 50%;
  opacity: 0.1;
  z-index: -1;
}

.score-circle.level-very-likely {
  background: radial-gradient(circle, rgba(239, 68, 68, 0.15) 0%, rgba(239, 68, 68, 0) 100%);
  border: 3px solid #dc2626;
  color: #fca5a5;
}

.score-circle.level-likely {
  background: radial-gradient(circle, rgba(249, 115, 22, 0.15) 0%, rgba(249, 115, 22, 0) 100%);
  border: 3px solid #ea580c;
  color: #fedba8;
}

.score-circle.level-potential {
  background: radial-gradient(circle, rgba(234, 179, 8, 0.15) 0%, rgba(234, 179, 8, 0) 100%);
  border: 3px solid #ca8a04;
  color: #fde047;
}

.score-circle.level-unrelated {
  background: radial-gradient(circle, rgba(34, 197, 94, 0.15) 0%, rgba(34, 197, 94, 0) 100%);
  border: 3px solid #16a34a;
  color: #86efac;
}

.score-value {
  font-size: 3rem;
  line-height: 1;
}

.score-label {
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-top: 0.5rem;
  opacity: 0.8;
}

.score-info {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.score-header {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.score-header h2 {
  margin: 0;
  font-size: 1.5rem;
  color: #e2e8f0;
}

.suspicion-badge {
  padding: 0.5rem 1rem;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 600;
  white-space: nowrap;
}

.suspicion-badge.level-very-likely {
  background: rgba(239, 68, 68, 0.2);
  color: #fca5a5;
  border: 1px solid #dc2626;
}

.suspicion-badge.level-likely {
  background: rgba(249, 115, 22, 0.2);
  color: #fedba8;
  border: 1px solid #ea580c;
}

.suspicion-badge.level-potential {
  background: rgba(234, 179, 8, 0.2);
  color: #fde047;
  border: 1px solid #ca8a04;
}

.suspicion-badge.level-unrelated {
  background: rgba(34, 197, 94, 0.2);
  color: #86efac;
  border: 1px solid #16a34a;
}

.score-confidence {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin: 0;
  font-size: 0.9rem;
  color: #cbd5e1;
}

.confidence-label {
  flex-shrink: 0;
}

.confidence-bar {
  flex: 1;
  height: 8px;
  background: rgba(15, 23, 42, 0.6);
  border-radius: 4px;
  overflow: hidden;
  min-width: 100px;
}

.confidence-fill {
  display: block;
  height: 100%;
  background: linear-gradient(90deg, #3b82f6 0%, #8b5cf6 100%);
}

.confidence-value {
  flex-shrink: 0;
  font-weight: 600;
  color: #e2e8f0;
}

.score-meta {
  margin: 0;
  color: #94a3b8;
  font-size: 0.9rem;
}

/* Analysis Grid */
.analysis-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1.5rem;
}

.analysis-card {
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 10px;
  overflow: hidden;
}

.card-header {
  padding: 1.25rem;
  border-bottom: 1px solid #334155;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
}

.card-header h3 {
  margin: 0;
  font-size: 1.1rem;
  color: #e2e8f0;
}

.data-badge {
  padding: 0.35rem 0.75rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  white-space: nowrap;
}

.data-badge.available {
  background: rgba(34, 197, 94, 0.2);
  color: #86efac;
  border: 1px solid #16a34a;
}

.data-badge.insufficient {
  background: rgba(107, 114, 128, 0.2);
  color: #cbd5e1;
  border: 1px solid #475569;
}

.card-body {
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.score-bar {
  height: 8px;
  background: rgba(15, 23, 42, 0.6);
  border-radius: 4px;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  background: linear-gradient(90deg, #3b82f6 0%, #8b5cf6 100%);
  border-radius: 4px;
}

.score-text {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 600;
  color: #cbd5e1;
}

.metrics {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding: 1rem;
  background: rgba(15, 23, 42, 0.3);
  border-radius: 6px;
}

.metric {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.9rem;
}

.metric-label {
  color: #94a3b8;
}

.metric-value {
  font-weight: 600;
  color: #e2e8f0;
}

.analysis-text {
  margin: 0;
  font-size: 0.85rem;
  color: #cbd5e1;
  line-height: 1.5;
}

/* Evidence Section */
.evidence-section {
  grid-column: 1 / -1;
}

.evidence-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1.5rem;
}

.evidence-card {
  border: 1.5px solid #334155;
  border-radius: 10px;
  padding: 1.5rem;
  background: rgba(30, 41, 59, 0.4);
}

.evidence-card.red-flags {
  border-color: #dc2626;
  background: rgba(239, 68, 68, 0.05);
}

.evidence-card.green-flags {
  border-color: #16a34a;
  background: rgba(34, 197, 94, 0.05);
}

.evidence-card.neutral {
  border-color: #475569;
  background: rgba(71, 85, 105, 0.05);
}

.evidence-title {
  margin: 0 0 1rem 0;
  font-size: 1rem;
  font-weight: 600;
  color: #e2e8f0;
}

.evidence-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.evidence-item {
  padding: 0.75rem;
  background: rgba(15, 23, 42, 0.3);
  border-radius: 6px;
  color: #cbd5e1;
  font-size: 0.9rem;
  line-height: 1.4;
}

.evidence-card.red-flags .evidence-item {
  border-left: 3px solid #dc2626;
  padding-left: 1rem;
}

.evidence-card.green-flags .evidence-item {
  border-left: 3px solid #16a34a;
  padding-left: 1rem;
}

.evidence-text {
  margin: 0;
  color: #94a3b8;
  font-size: 0.9rem;
}

/* Timeline Section */
.timeline-section {
  grid-column: 1 / -1;
}

.timeline-section h3 {
  margin: 0 0 1.5rem 0;
  font-size: 1.25rem;
  color: #e2e8f0;
}

.timeline-card {
  background: rgba(30, 41, 59, 0.4);
  border: 1px solid #334155;
  border-radius: 10px;
  padding: 2rem 1.5rem;
}

.timeline-content {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 2rem;
  align-items: center;
}

.timeline-period {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.period-label {
  font-weight: 600;
  color: #e2e8f0;
  font-size: 0.95rem;
}

.period-dates {
  font-size: 0.85rem;
  color: #94a3b8;
}

.period-sessions {
  font-size: 0.85rem;
  color: #64748b;
}

.gap-info {
  text-align: center;
  padding: 1rem;
  border-radius: 8px;
  border: 2px solid #334155;
}

.gap-info.gap-tight {
  background: rgba(239, 68, 68, 0.1);
  border-color: #dc2626;
}

.gap-info.gap-large {
  background: rgba(34, 197, 94, 0.1);
  border-color: #16a34a;
}

.gap-days {
  display: block;
  font-size: 1.5rem;
  font-weight: 700;
  color: #e2e8f0;
}

.gap-description {
  display: block;
  font-size: 0.85rem;
  color: #94a3b8;
  margin-top: 0.25rem;
}


@media (max-width: 768px) {
  .full-comparison-wrapper {
    height: 100vh;
  }

  .report-container {
    gap: 1.5rem;
  }

  .score-card {
    grid-template-columns: 1fr;
    gap: 1.5rem;
  }

  .score-circle {
    width: 120px;
    height: 120px;
  }

  .score-value {
    font-size: 2.5rem;
  }

  .analysis-grid {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .evidence-grid {
    grid-template-columns: 1fr;
  }

  .timeline-content {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .gap-info {
    order: 2;
  }

  .timeline-section h3 {
    font-size: 1.1rem;
  }

  .card-header {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
