using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Seats;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class SeatRepository : ISeatRepository
{
    private readonly CinemaBookingDbContext _dbContext;

    public SeatRepository(CinemaBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Seat>> GetSeatsByRoomAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Seats
            .AsNoTracking()
            .Include(seat => seat.SeatType)
            .Where(seat => seat.RoomID == roomId && seat.IsCurrentLayout)
            .OrderBy(seat => seat.SeatRow)
            .ThenBy(seat => seat.SeatCol)
            .ToListAsync(cancellationToken);
    }

    public Task<Room?> GetRoomByIdAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(room => room.RoomID == roomId, cancellationToken);
    }

    public Task<Seat?> GetSeatByIdAsync(
        int roomId,
        int seatId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Seats
            .AsNoTracking()
            .Include(seat => seat.SeatType)
            .FirstOrDefaultAsync(
                seat => seat.RoomID == roomId && seat.SeatID == seatId && seat.IsCurrentLayout,
                cancellationToken);
    }

    public Task<Seat?> GetSeatByIdAsync(
        int seatId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Seats
            .AsNoTracking()
            .Include(seat => seat.SeatType)
            .FirstOrDefaultAsync(
                seat => seat.SeatID == seatId && seat.IsCurrentLayout,
                cancellationToken);
    }

    public Task<SeatType?> GetSeatTypeByNameAsync(
        string typeName,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SeatTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(type => type.TypeName == typeName, cancellationToken);
    }

    public Task<SeatType?> GetSeatTypeByIdAsync(
        int seatTypeId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.SeatTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(type => type.SeatTypeID == seatTypeId, cancellationToken);
    }

    public Task<int> CountSeatsByRoomAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Seats
            .AsNoTracking()
            .CountAsync(seat => seat.RoomID == roomId && seat.IsCurrentLayout, cancellationToken);
    }

    public Task<bool> SeatPositionExistsAsync(
        int roomId,
        string rowLabel,
        int seatNumber,
        int? excludingSeatId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Seats
            .AsNoTracking()
            .Where(seat => seat.RoomID == roomId
                && seat.IsCurrentLayout
                && seat.SeatRow == rowLabel
                && seat.SeatCol == seatNumber);

        if (excludingSeatId.HasValue)
        {
            query = query.Where(seat => seat.SeatID != excludingSeatId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<List<Seat>> GetSeatsBySelectorAsync(
        int roomId,
        IReadOnlyCollection<SeatSelector> selectors,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Seat> query = _dbContext.Seats
            .AsNoTracking()
            .Include(seat => seat.SeatType)
            .Where(seat => seat.RoomID == roomId && seat.IsCurrentLayout);

        foreach (var selector in selectors)
        {
            var normalizedMode = selector.Mode.Trim().ToUpperInvariant();
            if (normalizedMode == "IDS")
            {
                var ids = selector.Target.Select(int.Parse).ToArray();
                query = query.Where(seat => ids.Contains(seat.SeatID));
            }
            else if (normalizedMode == "ROWS")
            {
                var rows = selector.Target
                    .Select(value => value.Trim().ToUpperInvariant())
                    .ToArray();
                query = query.Where(seat => rows.Contains(seat.SeatRow));
            }
            else if (normalizedMode == "COLS")
            {
                var cols = selector.Target.Select(int.Parse).ToArray();
                query = query.Where(seat => cols.Contains(seat.SeatCol));
            }
        }

        return query
            .OrderBy(seat => seat.SeatRow)
            .ThenBy(seat => seat.SeatCol)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasActiveOrUpcomingShowtimesAsync(
        int roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Showtimes
            .AsNoTracking()
            .AnyAsync(showtime => showtime.RoomID == roomId
                && showtime.Status == "scheduled", cancellationToken);
    }

    public async Task<bool> HasSeatRelationsAsync(
        int seatId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.BookingSeats
            .AsNoTracking()
            .AnyAsync(bookingSeat => bookingSeat.SeatID == seatId, cancellationToken)
            || await _dbContext.Tickets
                .AsNoTracking()
                .AnyAsync(ticket => ticket.BookingSeat.SeatID == seatId, cancellationToken)
            || await _dbContext.SeatHolds
                .AsNoTracking()
                .AnyAsync(hold => hold.SeatID == seatId, cancellationToken);
    }

    public async Task<Seat?> AddAsync(
        Seat seat,
        CancellationToken cancellationToken = default)
    {
        _dbContext.Seats.Add(seat);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _dbContext.ChangeTracker.Clear();
            return null;
        }

        await RecalculateRoomCapacityAsync(seat.RoomID, cancellationToken);

        await _dbContext.Entry(seat)
            .Reference(s => s.SeatType)
            .LoadAsync(cancellationToken);

        return seat;
    }

    public async Task<Seat?> UpdateAsync(
        int roomId,
        int seatId,
        int? seatTypeId,
        string status,
        bool isGap,
        CancellationToken cancellationToken = default)
    {
        var seat = await _dbContext.Seats
            .Include(s => s.SeatType)
            .FirstOrDefaultAsync(
                s => s.RoomID == roomId && s.SeatID == seatId && s.IsCurrentLayout,
                cancellationToken);

        if (seat is null)
        {
            return null;
        }

        seat.SeatTypeID = seatTypeId;
        seat.Status = status;
        seat.IsGap = isGap;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecalculateRoomCapacityAsync(roomId, cancellationToken);

        await _dbContext.Entry(seat)
            .Reference(s => s.SeatType)
            .LoadAsync(cancellationToken);

        return seat;
    }

    public async Task<List<Seat>> UpdateRangeAsync(
        int roomId,
        IReadOnlyCollection<Seat> seats,
        CancellationToken cancellationToken = default)
    {
        if (seats.Count == 0) return [];

        foreach (var seat in seats)
        {
            seat.SeatType = null;
            _dbContext.Attach(seat);
            var entry = _dbContext.Entry(seat);
            entry.Property(item => item.SeatTypeID).IsModified = true;
            entry.Property(item => item.Status).IsModified = true;
            entry.Property(item => item.IsGap).IsModified = true;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateRoomCapacityAsync(roomId, cancellationToken);

        var ids = seats.Select(seat => seat.SeatID).ToArray();
        return await _dbContext.Seats.AsNoTracking()
            .Include(seat => seat.SeatType)
            .Where(seat => ids.Contains(seat.SeatID))
            .OrderBy(seat => seat.SeatRow)
            .ThenBy(seat => seat.SeatCol)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        int seatId,
        CancellationToken cancellationToken = default)
    {
        var seat = await _dbContext.Seats
            .FirstOrDefaultAsync(s => s.SeatID == seatId, cancellationToken);

        if (seat is null)
        {
            return false;
        }

        var roomId = seat.RoomID;
        _dbContext.Seats.Remove(seat);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecalculateRoomCapacityAsync(roomId, cancellationToken);

        return true;
    }

    public async Task<List<Seat>> ReplaceLayoutAsync(
        int roomId,
        IReadOnlyCollection<Seat> seats,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.Seats
            .Where(seat => seat.RoomID == roomId
                && seat.IsCurrentLayout)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(seat => seat.Status, "inactive")
                .SetProperty(seat => seat.IsCurrentLayout, false), cancellationToken);

        foreach (var seat in seats)
        {
            seat.IsCurrentLayout = true;
        }

        _dbContext.Seats.AddRange(seats);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecalculateRoomCapacityAsync(roomId, cancellationToken);

        return await GetSeatsByRoomAsync(roomId, cancellationToken);
    }

    internal static void ApplyLayoutValues(Seat existingSeat, Seat requestedSeat)
    {
        existingSeat.SeatTypeID = requestedSeat.SeatTypeID;
        existingSeat.Status = requestedSeat.Status;
        existingSeat.IsGap = requestedSeat.IsGap;
    }

    private async Task<HashSet<int>> GetRelatedSeatIdsAsync(
        int roomId,
        CancellationToken cancellationToken)
    {
        var bookingSeatIds = await _dbContext.BookingSeats
            .AsNoTracking()
            .Where(bookingSeat => bookingSeat.Seat.RoomID == roomId)
            .Select(bookingSeat => bookingSeat.SeatID)
            .ToListAsync(cancellationToken);

        var holdSeatIds = await _dbContext.SeatHolds
            .AsNoTracking()
            .Where(hold => hold.Seat.RoomID == roomId)
            .Select(hold => hold.SeatID)
            .ToListAsync(cancellationToken);

        return bookingSeatIds
            .Concat(holdSeatIds)
            .ToHashSet();
    }

    private async Task RecalculateRoomCapacityAsync(
        int roomId,
        CancellationToken cancellationToken)
    {
        var capacity = await _dbContext.Seats
            .Where(seat => seat.RoomID == roomId
                && seat.IsCurrentLayout
                && seat.Status == "active"
                && !seat.IsGap)
            .Join(
                _dbContext.SeatTypes,
                seat => seat.SeatTypeID,
                seatType => seatType.SeatTypeID,
                (_, seatType) => seatType.Capacity)
            .SumAsync(cancellationToken);

        await _dbContext.Rooms
            .Where(item => item.RoomID == roomId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(room => room.Capacity, capacity), cancellationToken);
    }
}
