export interface PlayerAliasSuspicionReport {
  player1: string
  player2: string
  overallSimilarityScore: number
  suspicionLevel: 'Unrelated' | 'Potential' | 'Likely' | 'VeryLikely'
  statAnalysis: StatSimilarityAnalysis
  behavioralAnalysis: BehavioralAnalysis
  networkAnalysis: NetworkAnalysis
  temporalAnalysis: TemporalAnalysis
  activityTimeline?: ActivityTimeline
  redFlags: string[]
  greenFlags: string[]
  analysisTimestamp: string
  daysAnalyzed: number
  analysisConfidence: number
}

export interface StatSimilarityAnalysis {
  score: number
  kdRatioDifference: number
  killRateDifference: number
  scorePerRoundDifference: number
  mapPerformanceSimilarity: number
  serverPerformanceSimilarity: number
  analysis: string
  hasSufficientData: boolean
}

export interface BehavioralAnalysis {
  score: number
  playTimeOverlapScore: number
  serverAffinityScore: number
  pingConsistencyScore: number
  sessionPatternScore: number
  analysis: string
  hasSufficientData: boolean
}

export interface NetworkAnalysis {
  score: number
  sharedTeammateCount: number
  teammateOverlapPercentage: number
  mutualConnectionScore: number
  hasDirectConnection: boolean
  networkShapeSimilarity: number
  analysis: string
  hasSufficientData: boolean
}

export interface TemporalAnalysis {
  score: number
  temporalOverlapMinutes: number
  significantTemporalOverlap: boolean
  invertedActivityScore: number
  activityGapConsistency: number
  analysis: string
  hasSufficientData: boolean
}

export interface ActivityTimeline {
  player1Activity: ActivityPeriod
  player2Activity: ActivityPeriod
  gap: GapAnalysis
  player1Timeline: DailyActivity[]
  player2Timeline: DailyActivity[]
  asciiTimeline: string
  analysis: string
  switchoverSuspicionScore: number
  hasSufficientData: boolean
}

export interface ActivityPeriod {
  firstSeen: string
  lastSeen: string
  totalActiveDays: number
  daysSinceLast: number
  totalSessions: number
  avgSessionsPerDay: number
  isCurrentlyActive: boolean
}

export interface GapAnalysis {
  daysBetween: number
  accountStoppedFirst: string
  accountStartedSecond: string
  switchoverStart: string
  switchoverEnd: string
  switchoverWindowDays: number
  overlapRatio: number
  patternDescription: string
}

export interface DailyActivity {
  date: string
  sessionCount: number
  totalMinutes: number
  avgKd: number
  wasActive: boolean
  intensityScore: number
}
