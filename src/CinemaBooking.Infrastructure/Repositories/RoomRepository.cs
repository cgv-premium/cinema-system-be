using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class RoomRepository : IRoomRepository
{
    private readonly CinemaBookingDbContext _dbContext;

    public RoomRepository(CinemaBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Room>> GetRoomsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsNoTracking()
            .Include(r => r.RoomType)
            .OrderBy(r => r.CinemaID)
            .ThenBy(r => r.RoomName)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Room>> GetRoomsByCinemaIdAsync(
        int cinemaId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsNoTracking()
            .Include(r => r.RoomType)
            .Where(r => r.CinemaID == cinemaId)
            .OrderBy(r => r.RoomName)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Room>> GetRoomsByIdsAsync(
        List<int> roomIds,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsNoTracking()
            .Where(r => roomIds.Contains(r.RoomID))
            .ToListAsync(cancellationToken);
    }

    public Task<Room?> GetByIdAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsNoTracking()
            .Include(r => r.RoomType)
            .FirstOrDefaultAsync(r => r.RoomID == roomId, cancellationToken);
    }

    public Task<bool> CinemaExistsAsync(
        int cinemaId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Cinemas
            .AsNoTracking()
            .AnyAsync(c => c.CinemaID == cinemaId, cancellationToken);
    }

    public Task<bool> RoomTypeExistsAsync(int roomTypeId, CancellationToken cancellationToken = default) =>
        _dbContext.RoomTypes.AsNoTracking().AnyAsync(x => x.RoomTypeID == roomTypeId, cancellationToken);

    public Task<bool> NameExistsInCinemaAsync(
        int cinemaId,
        string roomName,
        int? excludingRoomId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoomName = roomName.ToUpper();
        var query = _dbContext.Rooms
            .AsNoTracking()
            .Where(r => r.CinemaID == cinemaId && r.RoomName.ToUpper() == normalizedRoomName);

        if (excludingRoomId.HasValue)
        {
            query = query.Where(r => r.RoomID != excludingRoomId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<int> CountSeatsAsync(
        int roomId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Seats.CountAsync(
            seat => seat.RoomID == roomId && seat.IsCurrentLayout,
            cancellationToken);

    public async Task<Room> AddAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await GetByIdAsync(room.RoomID, cancellationToken))!;
    }

    public async Task<Room?> UpdateAsync(
        int roomId,
        int cinemaId,
        string roomName,
        int roomTypeId,
        string status,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms
            .FirstOrDefaultAsync(r => r.RoomID == roomId, cancellationToken);

        if (room is null)
        {
            return null;
        }

        room.CinemaID = cinemaId;
        room.RoomName = roomName;
        room.RoomTypeID = roomTypeId;
        room.Status = status;
        room.Description = description;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(roomId, cancellationToken);
    }

    public Task<bool> HasActiveOrUpcomingShowtimesAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Showtimes
            .AsNoTracking()
            .AnyAsync(s => s.RoomID == roomId
                && s.Status == "scheduled", cancellationToken);
    }

    public Task<bool> HasUpcomingShowtimesAsync(
        int roomId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Showtimes
            .AsNoTracking()
            .AnyAsync(s => s.RoomID == roomId
                && s.Status == "scheduled"
                && s.StartTime > now, cancellationToken);
    }

    public Task<bool> HasAnyShowtimesAsync(
        int roomId,
        CancellationToken cancellationToken = default) =>
        _dbContext.Showtimes.AsNoTracking()
            .AnyAsync(showtime => showtime.RoomID == roomId, cancellationToken);

    public async Task<bool> DeleteAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        var room = await _dbContext.Rooms
            .FirstOrDefaultAsync(r => r.RoomID == roomId, cancellationToken);

        if (room is null)
        {
            return false;
        }

        _dbContext.Rooms.Remove(room);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
