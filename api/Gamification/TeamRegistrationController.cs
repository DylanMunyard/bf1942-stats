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
[Route("stats/tournament/{tournamentId}/registration")]
[Authorize]
public class TeamRegistrationController(
    PlayerTrackerDbContext context,
    IClock clock,
    ILogger<TeamRegistrationController> logger) : ControllerBase
{
    /// <summary>
    /// Get user's registration status for this tournament
    /// </summary>
    [HttpGet("my-status")]
    public async Task<ActionResult<RegistrationStatusResponse>> GetMyStatus(int tournamentId)
    {
        try
        {
            var userEmail = User.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrEmpty(userEmail))
                return Unauthorized(new { message = "User email not found" });

            // Get user
            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Email == userEmail);

            if (user == null)
                return Unauthorized(new { message = "User not found" });

            // Check tournament exists and status
            var tournament = await context.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            var isRegistrationOpen = tournament.Status == "registration";

            // Get user's linked player names
            var linkedPlayerNames = await context.UserPlayerNames
                .Where(upn => upn.UserId == user.Id)
                .Select(upn => upn.PlayerName)
                .ToListAsync();

            // Check if user is already on a team for this tournament
            TeamMembershipInfo? teamMembership = null;
            var existingMembership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (existingMembership != null)
            {
                teamMembership = new TeamMembershipInfo
                {
                    TeamId = existingMembership.TournamentTeamId,
                    TeamName = existingMembership.TournamentTeam.Name,
                    Tag = existingMembership.TournamentTeam.Tag,
                    IsLeader = existingMembership.IsTeamLeader,
                    PlayerName = existingMembership.PlayerName,
                    JoinedAt = existingMembership.JoinedAt,
                    MembershipStatus = existingMembership.MembershipStatus
                };
            }

            return Ok(new RegistrationStatusResponse
            {
                IsRegistrationOpen = isRegistrationOpen,
                LinkedPlayerNames = linkedPlayerNames,
                TeamMembership = teamMembership
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting registration status for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error retrieving registration status" });
        }
    }

    /// <summary>
    /// Get available teams for joining
    /// </summary>
    [HttpGet("teams")]
    public async Task<ActionResult<List<AvailableTeamResponse>>> GetAvailableTeams(int tournamentId)
    {
        try
        {
            // Check tournament exists
            var tournament = await context.Tournaments
                .FirstOrDefaultAsync(t => t.Id == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Tournament not found" });

            // Get all teams for this tournament with player counts and leader info
            var teamsData = await context.TournamentTeams
                .Where(tt => tt.TournamentId == tournamentId)
                .Select(tt => new
                {
                    tt.Id,
                    tt.Name,
                    tt.Tag,
                    PlayerCount = tt.TeamPlayers.Count,
                    tt.RecruitmentStatus,
                    LeaderPlayerName = tt.TeamPlayers
                        .Where(tp => tp.IsTeamLeader)
                        .Select(tp => tp.PlayerName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var teams = teamsData.Select(tt => new AvailableTeamResponse(
                tt.Id,
                tt.Name,
                tt.Tag,
                tt.PlayerCount,
                tt.RecruitmentStatus,
                tt.LeaderPlayerName
            )).ToList();

            return Ok(teams);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting available teams for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error retrieving teams" });
        }
    }

    /// <summary>
    /// Create a new team (user becomes leader)
    /// </summary>
    [HttpPost("teams")]
    public async Task<ActionResult<CreateTeamResponse>> CreateTeam(
        int tournamentId,
        [FromBody] CreateTeamRequest request)
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
                return BadRequest(new { message = "Tournament is not open for registration" });

            // Validate rules acknowledgment
            if (!request.RulesAcknowledged)
                return BadRequest(new { message = "You must acknowledge the tournament rules" });

            // Validate player name is linked to user
            var linkedPlayerNames = await context.UserPlayerNames
                .Where(upn => upn.UserId == user.Id)
                .Select(upn => upn.PlayerName)
                .ToListAsync();

            if (!linkedPlayerNames.Contains(request.PlayerName))
                return BadRequest(new { message = "Selected player name is not linked to your account" });

            // Verify player exists in Players table
            var playerExists = await context.Players.AnyAsync(p => p.Name == request.PlayerName);
            if (!playerExists)
                return BadRequest(new { message = "Player name not found in database" });

            // Check user is not already on a team
            var existingMembership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (existingMembership != null)
                return BadRequest(new { message = "You are already on a team for this tournament" });

            // Check player name is not already on a team in this tournament
            var playerOnTeam = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.PlayerName == request.PlayerName && ttp.TournamentTeam.TournamentId == tournamentId)
                .AnyAsync();

            if (playerOnTeam)
                return BadRequest(new { message = "This player name is already registered on a team" });

            // Check team name is unique in tournament
            var teamNameExists = await context.TournamentTeams
                .Where(tt => tt.TournamentId == tournamentId && tt.Name == request.TeamName)
                .AnyAsync();

            if (teamNameExists)
                return BadRequest(new { message = "A team with this name already exists" });

            var now = clock.GetCurrentInstant();

            // Create team
            var team = new TournamentTeam
            {
                TournamentId = tournamentId,
                Name = request.TeamName,
                Tag = request.Tag,
                LeaderUserId = user.Id,
                CreatedAt = now
            };

            context.TournamentTeams.Add(team);
            await context.SaveChangesAsync();

            // Add creator as team player (leader) - leaders are auto-approved
            var teamPlayer = new TournamentTeamPlayer
            {
                TournamentTeamId = team.Id,
                PlayerName = request.PlayerName,
                UserId = user.Id,
                IsTeamLeader = true,
                RulesAcknowledged = true,
                RulesAcknowledgedAt = now,
                JoinedAt = now,
                MembershipStatus = TeamMembershipStatus.Approved
            };

            context.TournamentTeamPlayers.Add(teamPlayer);
            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} created team {TeamName} for tournament {TournamentId}",
                userEmail, request.TeamName, tournamentId);

            return Created($"/stats/tournament/{tournamentId}/registration/teams/{team.Id}",
                new CreateTeamResponse
                {
                    TeamId = team.Id,
                    TeamName = team.Name,
                    Tag = team.Tag,
                    CreatedAt = team.CreatedAt
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating team for tournament {TournamentId}", tournamentId);
            return StatusCode(500, new { message = "Error creating team" });
        }
    }

    /// <summary>
    /// Join an existing team
    /// </summary>
    [HttpPost("teams/{teamId}/join")]
    public async Task<IActionResult> JoinTeam(
        int tournamentId,
        int teamId,
        [FromBody] JoinTeamRequest request)
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
                return BadRequest(new { message = "Tournament is not open for registration" });

            // Validate rules acknowledgment
            if (!request.RulesAcknowledged)
                return BadRequest(new { message = "You must acknowledge the tournament rules" });

            // Validate team exists and belongs to this tournament
            var team = await context.TournamentTeams
                .FirstOrDefaultAsync(t => t.Id == teamId && t.TournamentId == tournamentId);

            if (team == null)
                return NotFound(new { message = "Team not found" });

            // Validate team is accepting new members
            if (team.RecruitmentStatus == TeamRecruitmentStatus.Closed)
                return BadRequest(new { message = "This team is not currently recruiting new members" });

            if (team.RecruitmentStatus == TeamRecruitmentStatus.LookingForBTeam)
                return BadRequest(new { message = "This team is looking to start a second team. Please contact the team leader on Discord to discuss." });

            // Validate player name is linked to user
            var linkedPlayerNames = await context.UserPlayerNames
                .Where(upn => upn.UserId == user.Id)
                .Select(upn => upn.PlayerName)
                .ToListAsync();

            if (!linkedPlayerNames.Contains(request.PlayerName))
                return BadRequest(new { message = "Selected player name is not linked to your account" });

            // Verify player exists in Players table
            var playerExists = await context.Players.AnyAsync(p => p.Name == request.PlayerName);
            if (!playerExists)
                return BadRequest(new { message = "Player name not found in database" });

            // Check user is not already on a team
            var existingMembership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeam.TournamentId == tournamentId)
                .FirstOrDefaultAsync();

            if (existingMembership != null)
                return BadRequest(new { message = "You are already on a team for this tournament" });

            // Check player name is not already on a team in this tournament
            var playerOnTeam = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.PlayerName == request.PlayerName && ttp.TournamentTeam.TournamentId == tournamentId)
                .AnyAsync();

            if (playerOnTeam)
                return BadRequest(new { message = "This player name is already registered on a team" });

            var now = clock.GetCurrentInstant();

            // Determine membership status: Pending if team has a leader, Approved if no leader
            var membershipStatus = team.LeaderUserId.HasValue
                ? TeamMembershipStatus.Pending
                : TeamMembershipStatus.Approved;

            // Add player to team
            var teamPlayer = new TournamentTeamPlayer
            {
                TournamentTeamId = teamId,
                PlayerName = request.PlayerName,
                UserId = user.Id,
                IsTeamLeader = false,
                RulesAcknowledged = true,
                RulesAcknowledgedAt = now,
                JoinedAt = now,
                MembershipStatus = membershipStatus
            };

            context.TournamentTeamPlayers.Add(teamPlayer);
            await context.SaveChangesAsync();

            var statusMessage = membershipStatus == TeamMembershipStatus.Pending
                ? "Join request submitted. Awaiting team leader approval."
                : "Successfully joined team";

            logger.LogInformation("User {UserEmail} joined team {TeamId} for tournament {TournamentId} with status {Status}",
                userEmail, teamId, tournamentId, membershipStatus);

            return Ok(new { message = statusMessage, membershipStatus = (int)membershipStatus });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error joining team {TeamId} in tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error joining team" });
        }
    }

    /// <summary>
    /// Leave a team (non-leaders only)
    /// </summary>
    [HttpDelete("teams/{teamId}/leave")]
    public async Task<IActionResult> LeaveTeam(int tournamentId, int teamId)
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

            // Find user's membership on this team
            var membership = await context.TournamentTeamPlayers
                .Include(ttp => ttp.TournamentTeam)
                .Where(ttp => ttp.UserId == user.Id && ttp.TournamentTeamId == teamId)
                .FirstOrDefaultAsync();

            if (membership == null)
                return NotFound(new { message = "You are not a member of this team" });

            // Prevent leader from leaving their own team
            if (membership.IsTeamLeader)
                return BadRequest(new { message = "Team leaders cannot leave their team. You must delete the team instead." });

            context.TournamentTeamPlayers.Remove(membership);
            await context.SaveChangesAsync();

            logger.LogInformation("User {UserEmail} left team {TeamId} in tournament {TournamentId}",
                userEmail, teamId, tournamentId);

            return Ok(new { message = "Successfully left team" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error leaving team {TeamId} in tournament {TournamentId}", teamId, tournamentId);
            return StatusCode(500, new { message = "Error leaving team" });
        }
    }
}
