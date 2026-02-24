<template>
  <div class="form-container">
    <div class="form-card">
      <div class="form-header">
        <h2>Compare Players</h2>
        <p>Enter two player names to analyze their activity patterns and relationships</p>
      </div>

      <form @submit.prevent="handleSubmit" class="form-content">
        <div class="input-group">
          <div class="input-field">
            <label>Player 1</label>
            <PlayerSearchInput
              v-model="player1"
              :player-number="1"
              @select="selectPlayer1"
              @enter="handleSubmit"
            />
          </div>

          <div class="vs-separator">
            <div class="separator-line"></div>
            <span class="separator-text">vs</span>
            <div class="separator-line"></div>
          </div>

          <div class="input-field">
            <label>Player 2</label>
            <PlayerSearchInput
              v-model="player2"
              :player-number="2"
              @select="selectPlayer2"
              @enter="handleSubmit"
            />
          </div>
        </div>

        <button type="submit" class="btn-submit" :disabled="loading || !player1 || !player2">
          <span v-if="!loading" class="btn-text">Analyze</span>
          <span v-else class="btn-text loading">
            <span class="spinner"></span>
            Analyzing...
          </span>
        </button>
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import PlayerSearchInput from './PlayerSearchInput.vue'

interface PlayerSearchResult {
  playerName: string
  totalPlayTimeMinutes: number
  lastSeen: string
  isActive: boolean
}

const emit = defineEmits<{
  search: [player1: string, player2: string]
}>()

interface Props {
  loading?: boolean
  initialPlayer1?: string
  initialPlayer2?: string
}

const props = withDefaults(defineProps<Props>(), {
  loading: false,
  initialPlayer1: '',
  initialPlayer2: ''
})

const player1 = ref('')
const player2 = ref('')

// Set initial values from props
watch(
  () => [props.initialPlayer1, props.initialPlayer2],
  ([p1, p2]) => {
    if (p1) player1.value = p1
    if (p2) player2.value = p2
  },
  { immediate: true }
)

const handleSubmit = () => {
  emit('search', player1.value.trim(), player2.value.trim())
}

const selectPlayer1 = (player: PlayerSearchResult) => {
  player1.value = player.playerName
}

const selectPlayer2 = (player: PlayerSearchResult) => {
  player2.value = player.playerName
}
</script>

<style scoped>
.form-container {
  width: 100%;
}

.form-card {
  background: rgba(30, 41, 59, 0.8);
  border: 1px solid #334155;
  border-radius: 12px;
  backdrop-filter: blur(10px);
  overflow: hidden;
}

.form-header {
  padding: 2rem;
  border-bottom: 1px solid #334155;
  background: rgba(15, 23, 42, 0.5);
}

.form-header h2 {
  margin: 0 0 0.5rem 0;
  color: #e2e8f0;
  font-size: 1.5rem;
  font-weight: 600;
}

.form-header p {
  margin: 0;
  color: #94a3b8;
  font-size: 0.9rem;
}

.form-content {
  padding: 2rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.input-group {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 1.5rem;
  align-items: flex-start;
}

.input-field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.input-field label {
  color: #cbd5e1;
  font-size: 0.85rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.input-wrapper {
  position: relative;
}

.input {
  width: 100%;
  padding: 0.875rem 1rem;
  background: rgba(15, 23, 42, 0.6);
  border: 1.5px solid #475569;
  border-radius: 8px;
  color: #e2e8f0;
  font-size: 1rem;
  transition: all 0.2s;
  font-family: inherit;
}

.input:focus {
  outline: none;
  border-color: #3b82f6;
  background: rgba(15, 23, 42, 0.8);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.input:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.input::placeholder {
  color: #64748b;
}

.suggestions {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  background: rgba(30, 41, 59, 0.95);
  border: 1px solid #334155;
  border-top: none;
  border-radius: 0 0 8px 8px;
  max-height: 200px;
  overflow-y: auto;
  z-index: 10;
}

.suggestion-item {
  padding: 0.75rem 1rem;
  color: #e2e8f0;
  cursor: pointer;
  transition: background-color 0.15s;
  border-bottom: 1px solid #334155;
}

.suggestion-item:last-child {
  border-bottom: none;
}

.suggestion-item:hover {
  background: rgba(59, 130, 246, 0.1);
  color: #3b82f6;
}

.vs-separator {
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  gap: 0.5rem;
  padding-top: 1.5rem;
}

.separator-line {
  flex: 1;
  height: 1px;
  background: linear-gradient(90deg, transparent, #475569, transparent);
  width: 100%;
  max-width: 30px;
}

.separator-text {
  color: #64748b;
  font-size: 0.85rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.btn-submit {
  padding: 1rem 2rem;
  background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
  color: white;
  border: none;
  border-radius: 8px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.3s;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  min-height: 48px;
  margin-top: 0.5rem;
}

.btn-submit:hover:not(:disabled) {
  background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
  transform: translateY(-2px);
  box-shadow: 0 8px 16px rgba(59, 130, 246, 0.4);
}

.btn-submit:active:not(:disabled) {
  transform: translateY(0);
}

.btn-submit:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}

.btn-text {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
}

.spinner {
  display: inline-block;
  width: 1rem;
  height: 1rem;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

@media (max-width: 768px) {
  .form-card {
    border-radius: 8px;
  }

  .form-header {
    padding: 1.5rem;
  }

  .form-header h2 {
    font-size: 1.25rem;
  }

  .form-content {
    padding: 1.5rem;
    gap: 1rem;
  }

  .input-group {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .vs-separator {
    display: none;
  }

  .btn-submit {
    padding: 0.875rem 1.5rem;
    font-size: 0.95rem;
    min-height: 44px;
  }
}
</style>
