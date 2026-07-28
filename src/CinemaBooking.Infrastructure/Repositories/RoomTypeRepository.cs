using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Infrastructure.Repositories;

public sealed class RoomTypeRepository(CinemaBookingDbContext dbContext) : IRoomTypeRepository
{
    public Task<List<RoomType>> GetAllAsync(CancellationToken cancellationToken = default) =>
        dbContext.RoomTypes.AsNoTracking().OrderBy(x => x.TypeName).ToListAsync(cancellationToken);
    public Task<List<RoomType>> GetRoomTypesByIdsAsync(List<int> roomTypeIds, CancellationToken cancellationToken = default) =>
        dbContext.RoomTypes.AsNoTracking().Where(x => roomTypeIds.Contains(x.RoomTypeID)).ToListAsync(cancellationToken);
    public Task<RoomType?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.RoomTypes.AsNoTracking().FirstOrDefaultAsync(x => x.RoomTypeID == id, cancellationToken);
    public Task<bool> NameExistsAsync(string name, int? excludingId = null, CancellationToken cancellationToken = default) =>
        dbContext.RoomTypes.AnyAsync(x => x.TypeName == name && (!excludingId.HasValue || x.RoomTypeID != excludingId), cancellationToken);
    public async Task<RoomType> AddAsync(RoomType roomType, CancellationToken cancellationToken = default)
    { dbContext.Add(roomType); await dbContext.SaveChangesAsync(cancellationToken); return roomType; }
    public async Task<RoomType?> UpdateAsync(int id, string name, decimal extraPrice, string? description, CancellationToken cancellationToken = default)
    { var value = await dbContext.RoomTypes.FirstOrDefaultAsync(x => x.RoomTypeID == id, cancellationToken); if (value is null) return null; value.TypeName = name; value.ExtraPrice = extraPrice; value.Description = description; value.UpdatedAt = DateTime.UtcNow; await dbContext.SaveChangesAsync(cancellationToken); return value; }
    public Task<bool> IsUsedAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.Rooms.AnyAsync(x => x.RoomTypeID == id, cancellationToken);
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    { var value = await dbContext.RoomTypes.FirstOrDefaultAsync(x => x.RoomTypeID == id, cancellationToken); if (value is null) return false; dbContext.Remove(value); await dbContext.SaveChangesAsync(cancellationToken); return true; }
}
