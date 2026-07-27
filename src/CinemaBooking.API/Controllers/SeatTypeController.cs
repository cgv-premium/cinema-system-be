using CinemaBooking.API.Contracts.SeatTypes;
using CinemaBooking.Application.SeatTypes;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CinemaBooking.Application.ActivityLogs;

namespace CinemaBooking.API.Controllers;

[ApiController]
[Route("api/seat-types")]
public sealed class SeatTypeController : ControllerBase
{
    private readonly ISeatTypeService _seatTypeService;
    private readonly IActivityLogService _activityLogs;

    public SeatTypeController(ISeatTypeService seatTypeService, IActivityLogService activityLogs)
    {
        _seatTypeService = seatTypeService;
        _activityLogs = activityLogs;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetSeatTypes(CancellationToken cancellationToken)
    {
        var seatTypes = await _seatTypeService.GetSeatTypesAsync(cancellationToken);
        return Ok(seatTypes.Select(ToResponse));
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSeatTypeById(
        int id,
        CancellationToken cancellationToken)
    {
        var seatType = await _seatTypeService.GetSeatTypeByIdAsync(id, cancellationToken);

        return seatType is null
            ? NotFound(new { success = false, message = "Seat type not found." })
            : Ok(ToResponse(seatType));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> CreateSeatType(
        [FromBody] SeatTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Invalid request." });
        }

        var result = await _seatTypeService.CreateSeatTypeAsync(
            request.TypeName,
            request.Capacity,
            request.ExtraPrice,
            cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { success = false, message = result.ErrorMessage });
        }

        var response = ToResponse(result.SeatType!);
        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.CreateSeatType, "SeatType", response.SeatTypeId, $"Created seat type {response.SeatTypeId}", this.AuditIpAddress(), cancellationToken);
        return CreatedAtAction(
            nameof(GetSeatTypeById),
            new { id = response.SeatTypeId },
            response);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateSeatType(
        int id,
        [FromBody] SeatTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Invalid request." });
        }

        var result = await _seatTypeService.UpdateSeatTypeAsync(
            id,
            request.TypeName,
            request.Capacity,
            request.ExtraPrice,
            cancellationToken);

        if (!result.Succeeded)
        {
            return result.ErrorMessage == "Seat type not found."
                ? NotFound(new { success = false, message = result.ErrorMessage })
                : BadRequest(new { success = false, message = result.ErrorMessage });
        }

        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.UpdateSeatType, "SeatType", id, $"Updated seat type {id}", this.AuditIpAddress(), cancellationToken);
        return Ok(ToResponse(result.SeatType!));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DeleteSeatType(
        int id,
        CancellationToken cancellationToken)
    {
        var result = await _seatTypeService.DeleteSeatTypeAsync(id, cancellationToken);

        if (!result.Succeeded)
        {
            return result.ErrorMessage == "Seat type not found."
                ? NotFound(new { success = false, message = result.ErrorMessage })
                : Conflict(new { success = false, message = result.ErrorMessage });
        }

        if (!this.TryAuditActorId(out var adminId)) return Unauthorized();
        await _activityLogs.RecordAsync(adminId, AdminActionTypes.DeleteSeatType, "SeatType", id, $"Deleted seat type {id}", this.AuditIpAddress(), cancellationToken);
        return NoContent();
    }

    private static SeatTypeResponse ToResponse(SeatType seatType)
    {
        return new SeatTypeResponse(
            seatType.SeatTypeID,
            seatType.TypeName,
            seatType.Capacity,
            seatType.ExtraPrice);
    }
}
