import axios from 'axios'
import type { PlayerAliasSuspicionReport } from '../types/alias-detection'

const API_BASE = '/stats'

class AliasDetectionService {
  /**
   * Compare two players for alias/similarity patterns
   */
  async comparePlayersAsync(
    player1: string,
    player2: string,
    lookBackDays: number = 3650
  ): Promise<PlayerAliasSuspicionReport> {
    try {
      const response = await axios.get<PlayerAliasSuspicionReport>(
        `${API_BASE}/alias-detection/compare`,
        {
          params: {
            player1: player1.trim(),
            player2: player2.trim(),
            lookBackDays
          },
          timeout: 30000 // 30 second timeout for analysis
        }
      )

      if (!response.data) {
        throw new Error('No data returned from server')
      }

      return response.data
    } catch (error) {
      if (axios.isAxiosError(error)) {
        if (error.response?.status === 400) {
          const message = error.response.data?.error || 'Invalid player names'
          throw new Error(message)
        }
        if (error.response?.status === 500) {
          throw new Error('Server error while analyzing players')
        }
        if (error.code === 'ECONNABORTED') {
          throw new Error('Analysis took too long. Please try again.')
        }
        if (error.message === 'Network Error') {
          throw new Error('Network error. Please check your connection.')
        }
        throw new Error(error.response?.data?.error || error.message)
      }
      throw error
    }
  }

  /**
   * Get batch alias analysis for comparing one player against multiple candidates
   */
  async findPotentialAliasesAsync(
    targetPlayer: string,
    candidatePlayers: string[],
    lookBackDays: number = 90
  ): Promise<PlayerAliasSuspicionReport[]> {
    try {
      const response = await axios.post<{ topSuspects: PlayerAliasSuspicionReport[] }>(
        `${API_BASE}/alias-detection/batch`,
        {
          targetPlayer: targetPlayer.trim(),
          candidatePlayers: candidatePlayers.map(p => p.trim()),
          lookBackDays
        },
        {
          timeout: 60000 // 1 minute timeout for batch analysis
        }
      )

      return response.data.topSuspects || []
    } catch (error) {
      if (axios.isAxiosError(error)) {
        throw new Error(error.response?.data?.error || error.message)
      }
      throw error
    }
  }
}

export const aliasDetectionService = new AliasDetectionService()
