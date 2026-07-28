using CinemaBooking.Domain.Entities;

namespace CinemaBooking.Application.Common.Interfaces;

public interface IRoomTypeRepository
{
    Task<List<RoomType>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<RoomType>> GetRoomTypesByIdsAsync(List<int> roomTypeIds, CancellationToken cancellationToken = default);
    Task<RoomType?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludingId = null, CancellationToken cancellationToken = default);
    Task<RoomType> AddAsync(RoomType roomType, CancellationToken cancellationToken = default);
    Task<RoomType?> UpdateAsync(int id, string name, decimal extraPrice, string? description, CancellationToken cancellationToken = default);
    Task<bool> IsUsedAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
