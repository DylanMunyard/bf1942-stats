using System.Security.Claims;
using api.Gamification.Models;
using api.PlayerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Gamification;

[ApiController]
[Route("stats/tournament/{tournamentId}/my-team")]
[Authorize]
public class TeamLeaderController(
    PlayerTrackerDbContext context,
    IClock clock,
    ILogger<TeamLeaderController> logger) : ControllerBase
{
    /// <summary>
    /// Get current user's team details (must be team leader or member)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TeamDetailsResponse>> GetMyTeam(int tournamentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Find user's team membership
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            var team = membership.TournamentTeam;

            // Get all team players
            var players = await context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == team.Id)
                .OrderByDescending(ttp => ttp.IsTeamLeader)
                .ThenBy(ttp => ttp.JoinedAt)
                .Select(ttp => new TeamPlayerInfo
                {
                    PlayerName = ttp.PlayerName,
                    IsLeader = ttp.IsTeamLeader,
                    RulesAcknowledged = ttp.RulesAcknowledged,
                    JoinedAt = ttp.JoinedAt,
                    UserId = ttp.UserId
                })
                .ToListAsync();

            return Ok(new TeamDetailsResponse
            {
                TeamId = team.Id,
                TeamName = team.Name,
                Tag = team.Tag,
                CreatedAt = team.CreatedAt,
                RecruitmentStatus = team.RecruitmentStatus,
                Players = players
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting team details for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error retrieving team details" });
        }
    }

    /// <summary>
    /// Update team name/tag (leader only)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateTeam(
        int tournamentId,
        [FromBody] UpdateTeamRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Validate tournament exists and is in registration status
            var tournament = await context.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (tournament.Status != "registration")
                return BadRequest(new { message = "Tournament is not open for registration changes" });

            // Find user's team membership and verify they are the leader
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            if (!membership.IsTeamLeader)
                return Forbid();

            var team = membership.TournamentTeam;

            // Check if new team name conflicts with existing teams
            if (request.TeamName != team.Name)
            {
                var nameExists = await context.TournamentTeams
                    .Where(tt => tt.TournamentId == tournamentId && tt.Name == request.TeamName && tt.Id != team.Id)
                    .AnyAsync();

                if (nameExists)
                    return BadRequest(new { message = "A team with this name already exists" });
            }

            // Update team
            team.Name = request.TeamName;
            team.Tag = request.Tag;

            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} updated team {TeamId} in tournament {TournamentId}",
                userEmail, team.Id, tournamentId);

            return Ok(new { message = "Team updated successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating team in tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error updating team" });
        }
    }

    /// <summary>
    /// Update team recruitment status (leader only)
    /// </summary>
    [HttpPut("recruitment-status")]
    public async Task<IActionResult> UpdateRecruitmentStatus(
        int tournamentId,
        [FromBody] UpdateRecruitmentStatusRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Find user's team membership and verify they are the leader
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            if (!membership.IsTeamLeader)
                return Forbid();

            var team = membership.TournamentTeam;

            // Update recruitment status
            team.RecruitmentStatus = request.RecruitmentStatus;

            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} updated recruitment status for team {TeamId} to {Status} in tournament {TournamentId}",
                userEmail, team.Id, request.RecruitmentStatus, tournamentId);

            return Ok(new { message = "Recruitment status updated successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating recruitment status for team in tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error updating recruitment status" });
        }
    }

    /// <summary>
    /// Add a player to the team (leader only)
    /// </summary>
    [HttpPost("players")]
    public async Task<IActionResult> AddPlayer(
        int tournamentId,
        [FromBody] AddPlayerRequest request)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Validate tournament exists and is in registration status
            var tournament = await context.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (tournament.Status != "registration")
                return BadRequest(new { message = "Tournament is not open for registration changes" });

            // Find user's team membership and verify they are the leader
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            if (!membership.IsTeamLeader)
                return Forbid();

            var team = membership.TournamentTeam;

            // Verify player exists in Players table
            var playerExists = await context.Players.AnyAsync(p => p.Name == request.PlayerName);
            if (!playerExists)
                return BadRequest(new { message = "Player name not found in database" });

            // Check player is not already on a team in this tournament
            var playerOnTeam = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.PlayerName == request.PlayerName && ttp.TournamentTeam.TournamentId == tournamentId)
                .AnyAsync();

            if (playerOnTeam)
                return BadRequest(new { message = "This player is already registered on a team" });

            var now = clock.GetCurrentInstant();

            // Add player to team (without user link - manually added by leader)
            var teamPlayer = new TournamentTeamPlayer
            {
                TournamentTeamId = team.Id,
                PlayerName = request.PlayerName,
                UserId = null,  // No user link for manually added players
                IsTeamLeader = false,
                RulesAcknowledged = false,  // Leader-added players haven't acknowledged rules
                RulesAcknowledgedAt = null,
                JoinedAt = now
            };

            context.TournamentTeamPlayers.Add(teamPlayer);
            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} added player {PlayerName} to team {TeamId} in tournament {TournamentId}",
                userEmail, request.PlayerName, team.Id, tournamentId);

            return Ok(new { message = "Player added successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding player to team in tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error adding player" });
        }
    }

    /// <summary>
    /// Remove a player from the team (leader only)
    /// </summary>
    [HttpDelete("players/{playerName}")]
    public async Task<IActionResult> RemovePlayer(int tournamentId, string playerName)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Validate tournament exists and is in registration status
            var tournament = await context.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            if (tournament.Status != "registration")
                return BadRequest(new { message = "Tournament is not open for registration changes" });

            // Find user's team membership and verify they are the leader
            var leaderMembership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (leaderMembership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            if (!leaderMembership.IsTeamLeader)
                return Forbid();

            var team = leaderMembership.TournamentTeam;

            // Find the player to remove
            var playerMembership = await context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == team.Id && ttp.PlayerName == playerName)
                .FirstOrDefaultAsync();

            if (playerMembership == null)
                return NotFound(new { message = "Player not found on this team" });

            // Prevent removing the team leader
            if (playerMembership.IsTeamLeader)
                return BadRequest(new { message = "Cannot remove the team leader" });

            context.TournamentTeamPlayers.Remove(playerMembership);
            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} removed player {PlayerName} from team {TeamId} in tournament {TournamentId}",
                userEmail, playerName, team.Id, tournamentId);

            return Ok(new { message = "Player removed successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing player from team in tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error removing player" });
        }
    }


    /// <summary>
    /// Delete the team entirely (leader only). Team cannot have completed any matches.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteTeam(int tournamentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Find user's team membership and verify they are the leader
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not on a team for this tournament" });

            if (!membership.IsTeamLeader)
                return Forbid();

            var team = membership.TournamentTeam;

            // Check if team has completed any matches
            var hasCompletedMatches = await context.TournamentMatchResults
                .Where(tmr => tmr.Team1Id == team.Id || tmr.Team2Id == team.Id)
                .AnyAsync();

            if (hasCompletedMatches)
                return BadRequest(new { message = "Cannot delete team: team has completed matches" });

            // Check if team has any scheduled matches
            var hasScheduledMatches = await context.TournamentMatches
                .Where(tm => tm.Team1Id == team.Id || tm.Team2Id == team.Id)
                .AnyAsync();

            if (hasScheduledMatches)
                return BadRequest(new { message = "Cannot delete team: team has scheduled matches" });

            // Delete all team players first (cascade should handle this, but being explicit)
            var teamPlayers = await context.TournamentTeamPlayers
                .Where(ttp => ttp.TournamentTeamId == team.Id)
                .ToListAsync();

            context.TournamentTeamPlayers.RemoveRange(teamPlayers);

            // Delete the team
            context.TournamentTeams.Remove(team);

            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} deleted team {TeamId} ({TeamName}) from tournament {TournamentId}",
                userEmail, team.Id, team.Name, tournamentId);

            return Ok(new { message = "Team deleted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting team from tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error deleting team" });
        }
    }
}
