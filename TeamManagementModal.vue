<template>
  <div class="modal-overlay" @click="handleOverlayClick">
    <div class="modal-content" @click.stop>
      <div class="modal-header">
        <h2>Manage Team: {{ team.teamName }}</h2>
        <button class="close-button" @click="closeModal">×</button>
      </div>

      <div class="modal-body">
        <!-- Team Info Section -->
        <div class="team-info">
          <p><strong>Team:</strong> {{ team.teamName }}</p>
          <p><strong>Tag:</strong> {{ team.tag }}</p>
          <p><strong>Players:</strong> {{ team.players?.length || 0 }}</p>
        </div>

        <!-- Delete Team Section -->
        <div class="delete-section">
          <h3>Danger Zone</h3>

          <!-- Warning Message (shown when confirming) -->
          <div v-if="deleteState === 'confirming'" class="warning-message">
            <div class="warning-icon">⚠️</div>
            <div class="warning-text">
              <strong>This action cannot be undone.</strong>
              <p>Deleting your team will:</p>
              <ul>
                <li>Remove all team members from the tournament</li>
                <li>Delete the team permanently</li>
                <li>Cannot be undone once matches begin</li>
              </ul>
            </div>
          </div>

          <!-- Error Message -->
          <div v-if="deleteState === 'error'" class="error-message">
            <div class="error-icon">❌</div>
            <p>{{ errorMessage }}</p>
          </div>

          <!-- Action Buttons -->
          <div class="delete-actions">
            <button
              :class="['btn', getButtonClass()]"
              @click="handleDeleteClick"
              :disabled="deleteState === 'deleting'"
            >
              {{ getButtonText() }}
            </button>

            <button
              v-if="deleteState === 'confirming'"
              class="btn btn-secondary"
              @click="cancelDelete"
            >
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: 'TeamManagementModal',
  props: {
    team: {
      type: Object,
      required: true
    }
  },
  emits: ['close', 'team-deleted'],
  data() {
    return {
      deleteState: 'idle', // 'idle' | 'confirming' | 'deleting' | 'error'
      errorMessage: ''
    };
  },
  methods: {
    handleOverlayClick() {
      this.closeModal();
    },

    closeModal() {
      this.$emit('close');
    },

    handleDeleteClick() {
      if (this.deleteState === 'idle') {
        this.deleteState = 'confirming';
        this.errorMessage = '';
      } else if (this.deleteState === 'confirming') {
        this.confirmDelete();
      } else if (this.deleteState === 'error') {
        this.confirmDelete();
      }
    },

    async confirmDelete() {
      this.deleteState = 'deleting';

      try {
        const response = await fetch(`/stats/tournament/${this.team.tournamentId}/my-team`, {
          method: 'DELETE',
          headers: {
            'Authorization': `Bearer ${this.getAuthToken()}`,
            'Content-Type': 'application/json'
          }
        });

        if (response.ok) {
          this.$emit('team-deleted');
          this.closeModal();
        } else {
          const error = await response.json();
          this.errorMessage = error.message || 'Failed to delete team';
          this.deleteState = 'error';
        }
      } catch (error) {
        this.errorMessage = 'Network error. Please try again.';
        this.deleteState = 'error';
      }
    },

    cancelDelete() {
      this.deleteState = 'idle';
      this.errorMessage = '';
    },

    getButtonText() {
      switch (this.deleteState) {
        case 'idle':
          return 'Delete Team';
        case 'confirming':
          return 'Confirm Delete';
        case 'deleting':
          return 'Deleting...';
        case 'error':
          return 'Try Again';
        default:
          return 'Delete Team';
      }
    },

    getButtonClass() {
      switch (this.deleteState) {
        case 'idle':
          return 'btn-danger-outline';
        case 'confirming':
          return 'btn-danger';
        case 'deleting':
          return 'btn-danger btn-disabled';
        case 'error':
          return 'btn-danger';
        default:
          return 'btn-danger-outline';
      }
    },

    getAuthToken() {
      return localStorage.getItem('authToken') || sessionStorage.getItem('authToken');
    }
  }
};
</script>

<style scoped>
/* Include the same CSS from TeamManagementModal.css */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-content {
  background: white;
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
  max-width: 500px;
  width: 90%;
  max-height: 80vh;
  overflow-y: auto;
  animation: modalSlideIn 0.2s ease-out;
}

@keyframes modalSlideIn {
  from {
    opacity: 0;
    transform: scale(0.9) translateY(-20px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px;
  border-bottom: 1px solid #e1e5e9;
}

.modal-header h2 {
  margin: 0;
  font-size: 1.5rem;
  font-weight: 600;
  color: #1a202c;
}

.close-button {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: #718096;
  padding: 4px;
  border-radius: 4px;
  transition: all 0.2s;
}

.close-button:hover {
  background: #f7fafc;
  color: #2d3748;
}

.modal-body {
  padding: 20px;
}

.team-info {
  margin-bottom: 24px;
  padding: 16px;
  background: #f8fafc;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.team-info p {
  margin: 8px 0;
  color: #4a5568;
}

.delete-section {
  border: 2px solid #fed7d7;
  border-radius: 8px;
  padding: 20px;
  background: #fef5e7;
}

.delete-section h3 {
  margin: 0 0 16px 0;
  color: #c53030;
  font-size: 1.1rem;
  font-weight: 600;
}

.warning-message {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  padding: 16px;
  background: #fff5f5;
  border: 1px solid #feb2b2;
  border-radius: 6px;
  animation: warningSlideIn 0.3s ease-out;
}

@keyframes warningSlideIn {
  from {
    opacity: 0;
    transform: translateY(-10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.warning-icon {
  font-size: 24px;
  flex-shrink: 0;
}

.warning-text strong {
  color: #c53030;
  display: block;
  margin-bottom: 8px;
}

.warning-text ul {
  margin: 8px 0 0 0;
  padding-left: 20px;
  color: #744210;
}

.warning-text li {
  margin-bottom: 4px;
}

.error-message {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  padding: 16px;
  background: #fed7d7;
  border: 1px solid #fc8181;
  border-radius: 6px;
}

.error-icon {
  font-size: 20px;
  flex-shrink: 0;
}

.error-message p {
  margin: 0;
  color: #c53030;
  font-weight: 500;
}

.delete-actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

.btn {
  padding: 10px 16px;
  border-radius: 6px;
  font-weight: 500;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s;
  border: 1px solid transparent;
  min-width: 120px;
}

.btn:disabled,
.btn-disabled {
  cursor: not-allowed !important;
  opacity: 0.6;
}

.btn-danger-outline {
  background: white;
  color: #e53e3e;
  border-color: #e53e3e;
}

.btn-danger-outline:hover {
  background: #e53e3e;
  color: white;
}

.btn-danger {
  background: #e53e3e;
  color: white;
  border-color: #e53e3e;
  animation: dangerPulse 0.5s ease-out;
}

@keyframes dangerPulse {
  0% { transform: scale(1); }
  50% { transform: scale(1.02); }
  100% { transform: scale(1); }
}

.btn-danger:hover {
  background: #c53030;
  border-color: #c53030;
  transform: translateY(-1px);
  box-shadow: 0 2px 8px rgba(197, 48, 48, 0.3);
}

.btn-secondary {
  background: white;
  color: #718096;
  border-color: #e2e8f0;
}

.btn-secondary:hover {
  background: #f7fafc;
  border-color: #cbd5e0;
}

/* Responsive Design */
@media (max-width: 480px) {
  .modal-content {
    width: 95%;
    margin: 20px;
  }

  .modal-header,
  .modal-body {
    padding: 16px;
  }

  .delete-actions {
    flex-direction: column;
  }

  .btn {
    width: 100%;
  }

  .warning-message {
    flex-direction: column;
    text-align: center;
  }
}
</style>