import React, { useState } from 'react';

const TeamManagementModal = ({ team, onClose, onTeamDeleted }) => {
  const [deleteState, setDeleteState] = useState('idle'); // 'idle' | 'confirming' | 'deleting' | 'error'
  const [errorMessage, setErrorMessage] = useState('');

  const handleDeleteClick = () => {
    if (deleteState === 'idle') {
      setDeleteState('confirming');
      setErrorMessage('');
    } else if (deleteState === 'confirming') {
      handleConfirmDelete();
    }
  };

  const handleConfirmDelete = async () => {
    setDeleteState('deleting');

    try {
      const response = await fetch(`/stats/tournament/${team.tournamentId}/my-team`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${getAuthToken()}`,
          'Content-Type': 'application/json'
        }
      });

      if (response.ok) {
        onTeamDeleted();
        onClose();
      } else {
        const error = await response.json();
        setErrorMessage(error.message || 'Failed to delete team');
        setDeleteState('error');
      }
    } catch (error) {
      setErrorMessage('Network error. Please try again.');
      setDeleteState('error');
    }
  };

  const handleCancel = () => {
    setDeleteState('idle');
    setErrorMessage('');
  };

  const getButtonText = () => {
    switch (deleteState) {
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
  };

  const getButtonVariant = () => {
    switch (deleteState) {
      case 'idle':
        return 'danger-outline';
      case 'confirming':
        return 'danger';
      case 'deleting':
        return 'danger disabled';
      case 'error':
        return 'danger';
      default:
        return 'danger-outline';
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Manage Team: {team.teamName}</h2>
          <button className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="modal-body">
          {/* Other team management options would go here */}
          <div className="team-info">
            <p><strong>Team:</strong> {team.teamName}</p>
            <p><strong>Tag:</strong> {team.tag}</p>
            <p><strong>Players:</strong> {team.players?.length || 0}</p>
          </div>

          {/* Delete Team Section */}
          <div className="delete-section">
            <h3>Danger Zone</h3>

            {deleteState === 'confirming' && (
              <div className="warning-message">
                <div className="warning-icon">⚠️</div>
                <div className="warning-text">
                  <strong>This action cannot be undone.</strong>
                  <p>Deleting your team will:</p>
                  <ul>
                    <li>Remove all team members from the tournament</li>
                    <li>Delete the team permanently</li>
                    <li>Cannot be undone once matches begin</li>
                  </ul>
                </div>
              </div>
            )}

            {deleteState === 'error' && (
              <div className="error-message">
                <div className="error-icon">❌</div>
                <p>{errorMessage}</p>
              </div>
            )}

            <div className="delete-actions">
              <button
                className={`btn ${getButtonVariant()}`}
                onClick={handleDeleteClick}
                disabled={deleteState === 'deleting'}
              >
                {getButtonText()}
              </button>

              {deleteState === 'confirming' && (
                <button
                  className="btn btn-secondary"
                  onClick={handleCancel}
                >
                  Cancel
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

// Helper function to get auth token (implement based on your auth system)
const getAuthToken = () => {
  return localStorage.getItem('authToken') || sessionStorage.getItem('authToken');
};

export default TeamManagementModal;