using CinemaBooking.API.Contracts.Showtimes;
using CinemaBooking.Application.Showtimes;
using CinemaBooking.API.Contracts.Cinemas;
using CinemaBooking.Application.Common.Enums;
using CinemaBooking.Application.Common.Security;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CinemaBooking.Application.ActivityLogs;

namespace CinemaBooking.API.Controllers;

[ApiController]
[Route("api")]
public sealed class ShowtimeController : ControllerBase
{
    private readonly IShowtimeService _showtimeService;
    private readonly IManagerCinemaScopeService _managerCinemaScopeService;
    private readonly IActivityLogService _activityLogs;

    public ShowtimeController(
        IShowtimeService showtimeService,
        IManagerCinemaScopeService managerCinemaScopeService,
        IActivityLogService activityLogs)
    {
        _showtimeService = showtimeService;
        _managerCinemaScopeService = managerCinemaScopeService;
        _activityLogs = activityLogs;
    }

    [HttpGet("showtimes")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShowtimes(
        [FromQuery] int? movieId, [FromQuery] int? cinemaId,
        [FromQuery] string? movieName, [FromQuery] string? roomName,
        [FromQuery] DateOnly? date, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = "startTime", [FromQuery] string? sortDir = "asc",
        CancellationToken cancellationToken = default)
    {
        var scope = await GetViewerCinemaIdAsync(cancellationToken);
        if (scope.Forbidden) return CinemaScopeForbidden();
        if (scope.IsStaff)
        {
            if (cinemaId.HasValue && cinemaId != scope.CinemaId)
                return CinemaScopeForbidden();
            cinemaId = scope.CinemaId;
        }
        var result = await _showtimeService.GetShowtimesAsync(
            movieId, cinemaId, movieName, roomName, date, status,
            page, pageSize, sortBy, sortDir,
            scope.IsManager ? scope.CinemaId : null, cancellationToken);
        if (!result.Succeeded)
            return result.ErrorMessage == CinemaScopeMessages.AccessDenied
                ? CinemaScopeForbidden()
                : BadRequest(new { success = false, message = result.ErrorMessage });
        var data = result.Page!;
        var items = new List<ShowtimeListItemResponse>();
        foreach (var showtime in data.Items)
            items.Add(ToListResponse(showtime, data.SoldOutShowtimeIds.Contains(showtime.ShowtimeID)));
        return Ok(new PagedShowtimeResponse(items, data.Page, data.PageSize, data.TotalItems,
            (int)Math.Ceiling(data.TotalItems / (double)data.PageSize)));
    }

    [HttpPost("showtimes")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> CreateShowtime(
        CreateShowtimeRequest request, CancellationToken cancellationToken)
    {
        var scope = await GetRequiredManagerCinemaIdAsync(cancellationToken);
        if (!scope.HasValue) return CinemaScopeForbidden();
        var result = await _showtimeService.CreateShowtimeAsync(
            request.MovieId, request.RoomId, request.StartTime.UtcDateTime,
            request.BasePrice, scope, cancellationToken);
        if (!result.Succeeded) return MapWriteError(result.ErrorMessage);
        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.CreateShowtime,
            "Showtime", result.Showtime!.ShowtimeID, $"Created showtime {result.Showtime.ShowtimeID}", this.AuditIpAddress(), cancellationToken);
        var response = await ToManagementResponseAsync(result.Showtime!, cancellationToken);
        return CreatedAtAction(nameof(GetShowtimeById), new { id = response.ShowtimeId }, response);
    }

    [HttpGet("showtimes/range")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShowtimesByRange(
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] int? cinemaId,
        CancellationToken cancellationToken = default)
    {
        if (!startDate.HasValue)
            return BadRequest(new { success = false, message = "startDate is required" });
        if (!endDate.HasValue)
            return BadRequest(new { success = false, message = "endDate is required" });

        var scope = await GetViewerCinemaIdAsync(cancellationToken);
        if (scope.Forbidden) return CinemaScopeForbidden();
        if (scope.IsStaff)
        {
            if (cinemaId.HasValue && cinemaId != scope.CinemaId)
                return CinemaScopeForbidden();
            cinemaId = scope.CinemaId;
        }

        var result = await _showtimeService.GetShowtimesByRangeAsync(
            startDate.Value,
            endDate.Value,
            cinemaId,
            scope.IsManager ? scope.CinemaId : null,
            cancellationToken);

        if (!result.Succeeded)
            return result.ErrorMessage == CinemaScopeMessages.AccessDenied
                ? CinemaScopeForbidden()
                : BadRequest(new { success = false, message = result.ErrorMessage });

        var items = result.Items
            .Select(showtime => ToListResponse(
                showtime,
                result.SoldOutShowtimeIds.Contains(showtime.ShowtimeID)))
            .ToList();
        return Ok(new ShowtimeRangeResponse(items));
    }

    [HttpPut("showtimes/{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> UpdateShowtime(
        int id, UpdateShowtimeRequest request, CancellationToken cancellationToken)
    {
        var scope = await GetRequiredManagerCinemaIdAsync(cancellationToken);
        if (!scope.HasValue) return CinemaScopeForbidden();
        var previous = await _showtimeService.GetManagedShowtimeByIdAsync(id, cancellationToken);
        var result = await _showtimeService.UpdateShowtimeAsync(
            id, request.MovieId, request.RoomId, request.StartTime.UtcDateTime,
            request.BasePrice, request.Status, scope, cancellationToken);
        if (!result.Succeeded) return MapWriteError(result.ErrorMessage);
        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.UpdateShowtime,
            "Showtime", id, $"Updated showtime {id}", this.AuditIpAddress(), cancellationToken);
        if (previous is not null && previous.BasePrice != request.BasePrice)
            await _activityLogs.RecordAsync(adminId, AdminActionTypes.UpdateTicketPrice,
                "Showtime", id, $"Updated ticket price for showtime {id}", this.AuditIpAddress(), cancellationToken);
        return Ok(await ToManagementResponseAsync(result.Showtime!, cancellationToken));
    }

    [HttpDelete("showtimes/{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> DeleteShowtime(int id, CancellationToken cancellationToken)
    {
        var scope = await GetRequiredManagerCinemaIdAsync(cancellationToken);
        if (!scope.HasValue) return CinemaScopeForbidden();
        var result = await _showtimeService.DeleteShowtimeAsync(id, scope, cancellationToken);
        if (!result.Succeeded) return MapWriteError(result.ErrorMessage);
        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.DeleteShowtime,
            "Showtime", id, $"Deleted showtime {id}", this.AuditIpAddress(), cancellationToken);
        return NoContent();
    }

    [HttpGet("showtimes/{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShowtimeById(
        int id,
        CancellationToken cancellationToken)
    {
        Showtime? showtime;
        var scope = await GetViewerCinemaIdAsync(cancellationToken);
        if (scope.Forbidden) return CinemaScopeForbidden();
        if (scope.CinemaId.HasValue)
        {
            showtime = await _showtimeService.GetManagedShowtimeByIdAsync(id, cancellationToken);
            if (showtime is not null && showtime.Room.CinemaID != scope.CinemaId.Value)
                return CinemaScopeForbidden();
        }
        else
        {
            showtime = await _showtimeService.GetShowtimeByIdAsync(id, cancellationToken);
        }

        if (showtime is null)
            return NotFound(new { success = false, message = "Showtime not found." });

        return Ok(await ToManagementResponseAsync(showtime, cancellationToken));
    }

    [HttpGet("showtimes/{showtimeId:int}/seats")]
    [AllowAnonymous]
    public async Task<IActionResult> GetShowtimeSeats(
        int showtimeId,
        CancellationToken cancellationToken)
    {
        var scope = await GetViewerCinemaIdAsync(cancellationToken);
        if (scope.Forbidden) return CinemaScopeForbidden();

        var result = await _showtimeService.GetSeatMapAsync(
            showtimeId, scope.CinemaId, cancellationToken);

        if (result.SeatMap is null)
            return result.ErrorMessage == "Showtime not found."
                ? NotFound(new { success = false, message = result.ErrorMessage })
                : result.ErrorMessage == CinemaScopeMessages.AccessDenied
                    ? CinemaScopeForbidden()
                : BadRequest(new { success = false, message = result.ErrorMessage });

        return Ok(result.SeatMap);
    }

    private async Task<ShowtimeManagementResponse> ToManagementResponseAsync(
        Showtime showtime, CancellationToken cancellationToken) =>
        new(showtime.ShowtimeID,
            new(showtime.MovieID, showtime.Movie.Title, showtime.Movie.AgeRating,
                showtime.Movie.DurationMin, showtime.Movie.PosterURL),
            new(showtime.RoomID, showtime.Room.RoomName, showtime.Room.RoomType.TypeName, showtime.Room.Capacity),
            AsUtc(showtime.StartTime), AsUtc(showtime.EndTime), showtime.BasePrice,
            EnumValueMapper.ToApiValue(_showtimeService.GetDisplayStatus(showtime, DateTime.UtcNow)),
            _showtimeService.IsActive(showtime, DateTime.UtcNow),
            await _showtimeService.IsSoldOutAsync(showtime, cancellationToken));

    private static ShowtimeListItemResponse ToListResponse(
        Showtime showtime, bool isSoldOut) =>
        new(showtime.ShowtimeID,
            new(showtime.MovieID, showtime.Movie.Title, showtime.Movie.AgeRating,
                showtime.Movie.DurationMin, showtime.Movie.PosterURL),
            new(showtime.RoomID, showtime.Room.RoomName, showtime.Room.RoomType.TypeName, showtime.Room.Capacity),
            new CinemaSummaryResponse(
                showtime.Room.CinemaID,
                showtime.Room.Cinema.CinemaName,
                showtime.Room.Cinema.Address,
                showtime.Room.Cinema.Latitude.HasValue ? decimal.ToDouble(showtime.Room.Cinema.Latitude.Value) : null,
                showtime.Room.Cinema.Longitude.HasValue ? decimal.ToDouble(showtime.Room.Cinema.Longitude.Value) : null,
                EnumValueMapper.ToApiValue(showtime.Room.Cinema.Status)),
            AsUtc(showtime.StartTime), AsUtc(showtime.EndTime), showtime.BasePrice,
            EnumValueMapper.ToApiValue(showtime.Status == "cancelled"
                ? "cancelled"
                : DateTime.UtcNow < showtime.EndTime ? "scheduled" : "completed"),
            showtime.Status != "cancelled" && DateTime.UtcNow < showtime.EndTime,
            isSoldOut);

    private IActionResult MapWriteError(string? message) => message switch
    {
        "Showtime not found" or "Movie not found" or "Room not found" or "Cinema not found" =>
            NotFound(new { success = false, message }),
        "Showtime conflicts with another showtime in the same room"
            or "Another showtime with the same room type already starts at this time in the cinema"
            or         "Showtime has active bookings or seat holds"
            or "Showtime has booking or seat hold history" =>
            Conflict(new { success = false, message }),
        CinemaScopeMessages.AccessDenied => CinemaScopeForbidden(),
        _ => BadRequest(new { success = false, message })
    };

    private async Task<int?> GetRequiredManagerCinemaIdAsync(CancellationToken cancellationToken)
    {
        return int.TryParse(User.FindFirst("userId")?.Value, out var userId)
            ? await _managerCinemaScopeService.GetAssignedCinemaIdAsync(userId, cancellationToken)
            : null;
    }

    private async Task<(bool Forbidden, int? CinemaId, bool IsManager, bool IsStaff)> GetViewerCinemaIdAsync(
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
            return (false, null, false, false);

        if (User.IsInRole(Roles.Manager))
        {
            var scope = await GetRoleCinemaIdAsync(Roles.Manager, cancellationToken);
            return (scope.Forbidden, scope.CinemaId, true, false);
        }

        if (User.IsInRole(Roles.Staff))
        {
            var scope = await GetRoleCinemaIdAsync(Roles.Staff, cancellationToken);
            return (scope.Forbidden, scope.CinemaId, false, true);
        }

        return (false, null, false, false);
    }

    private async Task<(bool Forbidden, int? CinemaId)> GetRoleCinemaIdAsync(
        string role,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirst("userId")?.Value, out var userId))
            return (true, null);

        var cinemaId = await _managerCinemaScopeService.GetAssignedCinemaIdAsync(
            userId, role, cancellationToken);
        return (!cinemaId.HasValue, cinemaId);
    }

    private ObjectResult CinemaScopeForbidden() =>
        StatusCode(StatusCodes.Status403Forbidden,
            new { success = false, message = CinemaScopeMessages.AccessDenied });

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

}
