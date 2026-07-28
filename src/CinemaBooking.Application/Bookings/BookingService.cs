using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CinemaBooking.Application.Common.Interfaces;
using CinemaBooking.Application.Vouchers.RuleEngine;
using CinemaBooking.Domain.Entities;
using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Bookings;

public sealed class BookingService : IBookingService
{
    private const int HoldDurationMinutes = 15;

    private readonly IBookingRepository _bookingRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVoucherRuleEngine _voucherRuleEngine;
    private readonly IUserVoucherRepository _userVoucherRepository;

    public BookingService(
        IBookingRepository bookingRepository,
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IVoucherRuleEngine voucherRuleEngine,
        IUserVoucherRepository userVoucherRepository)
    {
        _bookingRepository = bookingRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _voucherRuleEngine = voucherRuleEngine;
        _userVoucherRepository = userVoucherRepository;
    }

    public async Task<(bool Succeeded, string? ErrorMessage, List<int>? HoldIds, DateTime? ExpiresAt, SeatValidationErrors? SeatErrors)> HoldSeatsAsync(
        int userId,
        int showtimeId,
        List<int> seatIds,
        CancellationToken cancellationToken = default)
    {
        if (seatIds.Count == 0)
            return (false, "Please select at least one seat.", null, null, null);

        var showtime = await _bookingRepository.GetShowtimeAsync(showtimeId, cancellationToken);
        if (showtime is null)
            return (false, "Showtime not found.", null, null, null);

        if (showtime.Status != "scheduled")
            return (false, "This showtime is not scheduled and is unavailable for seat holds.", null, null, null);

        if (showtime.StartTime <= DateTime.UtcNow.AddMinutes(15))
            return (false, "Seat holds close 15 minutes before the showtime starts.", null, null, null);

        if (showtime.Room.Status != "active")
            return (false, "The showtime room is inactive.", null, null, null);

        if (showtime.Room.Cinema.Status != "active")
            return (false, "The showtime cinema is inactive.", null, null, null);

        seatIds = seatIds.Distinct().ToList();

        var selectedSeats = await _bookingRepository.GetSeatsByIdsAsync(seatIds, cancellationToken);
        var seatErrors = GetSeatValidationErrors(seatIds, selectedSeats, showtime.RoomID);
        if (seatErrors is not null)
            return (false, "Some selected seats are invalid.", null, null, seatErrors);

        var unavailableSeatIds = await _bookingRepository.GetUnavailableSeatIdsAsync(
            showtimeId, seatIds, userId, cancellationToken);

        if (unavailableSeatIds.Count > 0)
            return (false, $"Seats with IDs {string.Join(", ", unavailableSeatIds)} are booked or held by another user.", null, null, null);

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(HoldDurationMinutes);

        var holds = seatIds.Select(seatId => new SeatHold
        {
            SeatID = seatId,
            ShowtimeID = showtimeId,
            UserID = userId,
            HeldAt = now,
            ExpiresAt = expiresAt,
            Status = "holding"
        }).ToList();

        if (!await _bookingRepository.TryAddSeatHoldsAsync(holds, cancellationToken))
            return (false, "One or more seats are already booked or being held.", null, null, null);

        return (true, null, holds.Select(h => h.HoldID).ToList(), expiresAt, null);
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> ReleaseSeatHoldsAsync(
        int userId,
        int showtimeId,
        List<int> seatIds,
        CancellationToken cancellationToken = default)
    {
        var requestedSeatIds = seatIds.Distinct().ToHashSet();
        if (requestedSeatIds.Count == 0)
            return (false, "Please select at least one seat.");

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var activeHolds = await _bookingRepository.GetMyActiveHoldsForUpdateAsync(
                userId, showtimeId, DateTime.UtcNow, cancellationToken);
            var requestedHolds = activeHolds
                .Where(hold => requestedSeatIds.Contains(hold.SeatID))
                .ToList();

            if (requestedHolds.Count != requestedSeatIds.Count)
                return (false, "One or more seats are not actively held by the current user.");

            await _bookingRepository.ReleaseSeatHoldsAsync(requestedHolds, cancellationToken);
            return (true, (string?)null);
        }, cancellationToken);
    }

    public async Task<(bool Succeeded, string? ErrorMessage, Booking? Booking, SeatValidationErrors? SeatErrors)> CreateBookingAsync(
        int actorUserId,
        int? customerId,
        bool isStaff,
        int? showtimeId,
        List<int> seatIds,
        List<BookingFnBItemDto> fnbItems,
        string? voucherCode,
        int? staffCinemaId = null,
        CancellationToken cancellationToken = default)
    {
        var isFnbOnly = !showtimeId.HasValue && seatIds.Count == 0;

        if (!isFnbOnly && seatIds.Count == 0)
            return (false, "Please select at least one seat.", null, null);

        Showtime? showtime = null;
        if (!isFnbOnly)
        {
            showtime = await _bookingRepository.GetShowtimeAsync(showtimeId!.Value, cancellationToken);
            if (showtime is null)
                return (false, "Showtime not found.", null, null);

            if (showtime.Status != "scheduled")
                return (false, "This showtime is not scheduled and is unavailable for booking.", null, null);

            if (showtime.StartTime <= DateTime.UtcNow)
                return (false, "This showtime has already started.", null, null);

            if (showtime.Room.Status != "active")
                return (false, "The showtime room is inactive.", null, null);

            if (showtime.Room.Cinema.Status != "active")
                return (false, "The showtime cinema is inactive.", null, null);
        }

        seatIds = seatIds.Distinct().ToList();

        List<SeatHold> myHolds = new();
        if (!isFnbOnly)
        {
            myHolds = await _bookingRepository.GetMyActiveHoldsAsync(
                actorUserId, showtimeId!.Value, seatIds, cancellationToken);

            if (myHolds.Count != seatIds.Count)
                return (false, "Some seats are not held or the holds have expired. Please select them again.", null, null);
        }

        var pricingResult = await CalculatePricingAsync(
            customerId, showtimeId, seatIds, fnbItems, voucherCode, staffCinemaId, cancellationToken);

        if (!pricingResult.Succeeded)
            return (false, pricingResult.ErrorMessage, null, null);

        if (!isStaff && customerId != actorUserId)
            return (false, "Customers can only create bookings for their own account.", null, null);

        var bookingSeats = new List<BookingSeat>();
        if (!isFnbOnly)
        {
            bookingSeats = pricingResult.Result!.SeatDetails
                .Select(s => new BookingSeat
                {
                    SeatID = s.SeatId,
                    TicketPrice = s.Price
                }).ToList();
        }

        var bookingFnBs = pricingResult.Result?.FnBDetails
            .Select(f => new BookingFnB
            {
                ItemID = f.ItemId,
                Quantity = f.Quantity,
                UnitPrice = f.UnitPrice,
                SubTotal = f.SubTotal
            }).ToList() ?? [];

        var productQuantities = pricingResult.Result!.FnBDetails
            .ToDictionary(f => f.ItemId, f => f.Quantity);

        BookingVoucher? bookingVoucher = null;
        int? redeemableVoucherId = null;
        if (pricingResult.Result.VoucherDetails is not null)
        {
            var voucher = await _bookingRepository.GetVoucherByCodeAsync(
                pricingResult.Result.VoucherDetails.VoucherCode, cancellationToken);

            if (voucher is null)
                return (false, "Voucher code does not exist.", null, null);

            bookingVoucher = new BookingVoucher
            {
                VoucherID = voucher.VoucherID,
                DiscountApplied = pricingResult.Result.VoucherDetails.DiscountApplied,
                UsedAt = DateTime.UtcNow
            };

            if (voucher.IsRedeemable)
                redeemableVoucherId = voucher.VoucherID;
        }

        var booking = new Booking
        {
            BookingCode = GenerateBookingCode(),
            UserID = customerId,
            CreatedByStaffID = isStaff ? actorUserId : null,
            ShowtimeID = showtimeId,
            SubTotal = pricingResult.Result!.TotalBeforeDiscount,
            DiscountAmount = pricingResult.Result.TotalDiscount,
            FinalAmount = pricingResult.Result.FinalAmount,
            Status = "pending",
            BookingDate = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            BookingSeats = bookingSeats,
            BookingFnBs = bookingFnBs
        };

        if (bookingVoucher is not null)
        {
            booking.BookingVoucher = bookingVoucher;
        }

        var bookingCreated = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!isFnbOnly)
            {
                var lockedHolds = await _bookingRepository.GetMyActiveHoldsForUpdateAsync(
                    actorUserId, showtimeId!.Value, DateTime.UtcNow, cancellationToken);
                var lockedRequestedHolds = lockedHolds
                    .Where(hold => seatIds.Contains(hold.SeatID))
                    .ToList();

                if (lockedRequestedHolds.Count != seatIds.Count)
                    return false;
            }

            if (productQuantities.Count > 0)
            {
                var lockedProducts = await _bookingRepository.GetProductsByIdsAsync(
                    productQuantities.Keys.ToList(), cancellationToken);

                foreach (var item in productQuantities)
                {
                    var product = lockedProducts.FirstOrDefault(p => p.ItemID == item.Key);
                    if (product is null)
                        throw new InvalidOperationException($"Product with ID {item.Key} not found.");

                    if (product.Status != "active")
                        throw new InvalidOperationException($"Product '{product.ItemName}' is inactive.");
                }
            }

            Voucher? lockedVoucher = null;
            if (bookingVoucher is not null)
            {
                lockedVoucher = await _bookingRepository.GetVoucherByCodeWithLockAsync(
                    voucherCode!.Trim(), cancellationToken);

                if (lockedVoucher is null)
                    throw new InvalidOperationException("Voucher no longer exists.");

                if (!lockedVoucher.IsActive)
                    throw new InvalidOperationException("Voucher is no longer active.");

                var now = DateTime.UtcNow;
                if (now < lockedVoucher.ValidFrom)
                    throw new InvalidOperationException("Voucher is not active yet.");

                if (now > lockedVoucher.ValidUntil)
                    throw new InvalidOperationException("Voucher has expired.");

                if (!lockedVoucher.IsRedeemable
                    && lockedVoucher.MaxUses.HasValue
                    && lockedVoucher.UsedCount >= lockedVoucher.MaxUses.Value)
                    throw new InvalidOperationException("Voucher usage limit has been reached.");
            }

            UserVoucher? reservedUserVoucher = null;
            if (redeemableVoucherId.HasValue && customerId.HasValue)
            {
                reservedUserVoucher = await _userVoucherRepository.GetAvailableForUpdateAsync(
                    customerId.Value, redeemableVoucherId.Value, cancellationToken);

                if (reservedUserVoucher is null)
                    throw new InvalidOperationException(
                        "This voucher must be redeemed to your account and be available before use.");

                if (reservedUserVoucher.ExpiredAt < DateTime.UtcNow)
                    throw new InvalidOperationException("Your redeemed voucher has expired.");
            }

            await _bookingRepository.AddBookingAsync(booking, cancellationToken);

            if (!isFnbOnly)
            {
                var lockedHolds = await _bookingRepository.GetMyActiveHoldsForUpdateAsync(
                    actorUserId, showtimeId!.Value, DateTime.UtcNow, cancellationToken);
                var lockedRequestedHolds = lockedHolds
                    .Where(hold => seatIds.Contains(hold.SeatID))
                    .ToList();

                await _bookingRepository.MarkHoldsAsConfirmedAsync(
                    lockedRequestedHolds, booking.BookingID, cancellationToken);
            }

            if (reservedUserVoucher is not null)
            {
                reservedUserVoucher.BookingID = booking.BookingID;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

        if (!bookingCreated)
            return (false, "Some seats are not held or the holds have expired. Please select them again.", null, null);

        var savedBooking = await _bookingRepository.GetBookingByIdAsync(booking.BookingID, cancellationToken);

        return (true, null, savedBooking, null);
    }

    public async Task<Booking?> GetBookingByIdAsync(
        int bookingId,
        CancellationToken cancellationToken = default)
    {
        return await _bookingRepository.GetBookingByIdAsync(bookingId, cancellationToken);
    }

    public async Task<List<Booking>> GetMyBookingsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _bookingRepository.GetBookingsByUserAsync(userId, cancellationToken);
    }

    public async Task<(bool Succeeded, string? ErrorMessage, PricingCalculationResult? Result)> CalculatePricingAsync(
        int? userId,
        int? showtimeId,
        List<int> seatIds,
        List<BookingFnBItemDto> fnbItems,
        string? voucherCode,
        int? staffCinemaId = null,
        CancellationToken cancellationToken = default)
    {
        var isFnbOnly = !showtimeId.HasValue && seatIds.Count == 0;

        if (!isFnbOnly && seatIds.Count == 0)
            return (false, "Please select at least one seat.", null);

        Showtime? showtime = null;
        if (!isFnbOnly)
        {
            showtime = await _bookingRepository.GetShowtimeAsync(showtimeId!.Value, cancellationToken);
            if (showtime is null)
                return (false, "Showtime not found.", null);

            if (showtime.Status != "scheduled")
                return (false, "Cannot calculate pricing for showtimes that are not scheduled.", null);

            if (showtime.StartTime <= DateTime.UtcNow)
                return (false, "Cannot calculate pricing for showtimes that have already started.", null);

            if (showtime.Room.Cinema.Status != "active")
                return (false, "Cinema is not active.", null);
        }

        seatIds = seatIds.Distinct().ToList();

        var seatDetails = new List<SeatPricingDetail>();
        var seatsSubTotal = 0m;

        if (!isFnbOnly)
        {
            var seats = await _bookingRepository.GetSeatsByIdsAsync(seatIds, cancellationToken);

            if (seats.Count != seatIds.Count || seats.Any(seat =>
                    seat.RoomID != showtime!.RoomID || seat.Status != "active"
                    || seat.IsGap || seat.SeatType is null))
                return (false, "One or more seats do not belong to the showtime room or are inactive.", null);

            seatDetails = seats.Select(seat => new SeatPricingDetail
            {
                SeatId = seat.SeatID,
                SeatRow = seat.SeatRow,
                SeatCol = seat.SeatCol,
                SeatTypeName = seat.SeatType!.TypeName,
                Price = showtime!.BasePrice + showtime!.RoomExtraPrice + seat.SeatType.ExtraPrice
            }).ToList();

            seatsSubTotal = seatDetails.Sum(s => s.Price);
        }

        var fnbDetails = new List<FnBPricingDetail>();
        var fnbSubTotal = 0m;

        var normalizedFnbItems = fnbItems
            .GroupBy(item => item.ItemId)
            .Select(group => new BookingFnBItemDto(group.Key, group.Sum(item => item.Quantity)))
            .ToList();

        List<Product> products = new();
        if (normalizedFnbItems.Any())
        {
            var productIds = normalizedFnbItems.Select(f => f.ItemId).ToList();
            products = await _bookingRepository.GetProductsByIdsAsync(productIds, cancellationToken);

            if (products.Count != productIds.Count)
            {
                var missingIds = productIds.Except(products.Select(p => p.ItemID)).ToList();
                return (false, $"Products with IDs {string.Join(", ", missingIds)} were not found.", null);
            }

            foreach (var fnbItem in normalizedFnbItems)
            {
                var product = products.First(p => p.ItemID == fnbItem.ItemId);

                if (product.Status != "active")
                    return (false, $"Product '{product.ItemName}' is inactive.", null);

                var itemSubTotal = product.Price * fnbItem.Quantity;
                fnbSubTotal += itemSubTotal;

                fnbDetails.Add(new FnBPricingDetail
                {
                    ItemId = product.ItemID,
                    ItemName = product.ItemName,
                    Quantity = fnbItem.Quantity,
                    UnitPrice = product.Price,
                    SubTotal = itemSubTotal
                });
            }
        }

        if (isFnbOnly && fnbSubTotal == 0)
            return (false, "F&B only booking requires at least one F&B item.", null);

        var totalBeforeDiscount = seatsSubTotal + fnbSubTotal;

        User? user = null;
        if (userId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
            if (user is null || user.Role != "customer")
                return (false, "Customer account was not found.", null);
        }

        var membershipDiscount = 0m;
        if (user?.LoyaltyTier is not null)
        {
            membershipDiscount = Math.Round(totalBeforeDiscount * user.LoyaltyTier.DiscountRate, 0);
        }

        var voucherDiscount = 0m;
        VoucherPricingDetail? voucherDetails = null;

        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            if (!userId.HasValue)
                return (false, "Guest bookings cannot use vouchers.", null);

            var voucher = await _bookingRepository.GetVoucherByCodeAsync(voucherCode.Trim(), cancellationToken);

            if (voucher is null)
                return (false, "Voucher code does not exist.", null);

            if (!voucher.IsActive)
                return (false, "Voucher is not available.", null);

            var now = DateTime.UtcNow;
            if (now < voucher.ValidFrom)
                return (false, "Voucher is not active yet.", null);

            if (now > voucher.ValidUntil)
                return (false, "Voucher has expired.", null);

            if (!voucher.IsRedeemable
                && voucher.MaxUses.HasValue
                && voucher.UsedCount >= voucher.MaxUses.Value)
                return (false, "Voucher usage limit has been reached.", null);

            if (voucher.MinOrderValue.HasValue && totalBeforeDiscount < voucher.MinOrderValue.Value)
                return (false, $"A minimum order value of {voucher.MinOrderValue.Value:N0} VND is required to use this voucher.", null);

            if (voucher.IsRedeemable)
            {
                var ownedVoucher = await _userVoucherRepository.GetAvailableOwnedAsync(
                    userId.Value, voucher.VoucherID, now, cancellationToken);
                if (ownedVoucher is null)
                    return (false, "This voucher must be redeemed to your account before use, and must be available and not expired.", null);
            }

            var validationContext = new Vouchers.RuleEngine.VoucherValidationContext
            {
                BookingId = 0,
                CustomerId = userId,
                CinemaId = showtime?.Room.CinemaID ?? staffCinemaId ?? 0,
                MovieId = showtime?.MovieID ?? 0,
                RoomId = showtime?.RoomID ?? 0,
                RoomTypeId = showtime?.Room.RoomTypeID ?? 0,
                ShowtimeDateTime = showtime?.StartTime ?? now,
                MembershipTier = user?.LoyaltyTier?.TierName,
                Seats = !isFnbOnly ? seatDetails.Select(s => new Vouchers.RuleEngine.SeatValidationData
                {
                    SeatID = s.SeatId,
                    SeatType = s.SeatTypeName,
                    Price = s.Price
                }).ToList() : new List<Vouchers.RuleEngine.SeatValidationData>(),
                Products = normalizedFnbItems.Select(fnbItem =>
                {
                    var product = products.First(p => p.ItemID == fnbItem.ItemId);
                    return new Vouchers.RuleEngine.ProductValidationData
                    {
                        ProductID = fnbItem.ItemId,
                        Category = product.ItemType,
                        Quantity = fnbItem.Quantity,
                        Price = product.Price
                    };
                }).ToList(),
                PaymentMethod = string.Empty,
                BookingTotal = totalBeforeDiscount,
                TicketTotal = seatsSubTotal,
                FoodTotal = fnbSubTotal,
                Voucher = voucher,
                ValidationTime = now
            };

            var validationResult = await _voucherRuleEngine.ValidateAsync(validationContext, cancellationToken);

            if (!validationResult.IsValid)
                return (false, validationResult.ErrorMessage ?? "Voucher validation failed.", null);

            var applicableAmount = validationResult.ApplicableAmount;

            voucherDiscount = voucher.DiscountType == "percent"
                ? Math.Round(applicableAmount * voucher.DiscountValue / 100, 0)
                : voucher.DiscountValue;
            voucherDiscount = Math.Min(voucherDiscount, applicableAmount);

            voucherDetails = new VoucherPricingDetail
            {
                VoucherCode = voucher.VoucherCode,
                DiscountType = voucher.DiscountType,
                DiscountApplied = voucherDiscount
            };
        }

        var totalDiscount = membershipDiscount + voucherDiscount;
        if (totalDiscount > totalBeforeDiscount)
            totalDiscount = totalBeforeDiscount;

        var finalAmount = totalBeforeDiscount - totalDiscount;

        var result = new PricingCalculationResult
        {
            SeatsSubTotal = seatsSubTotal,
            FnBSubTotal = fnbSubTotal,
            TotalBeforeDiscount = totalBeforeDiscount,
            MembershipDiscount = membershipDiscount,
            VoucherDiscount = voucherDiscount,
            TotalDiscount = totalDiscount,
            FinalAmount = finalAmount,
            SeatDetails = seatDetails,
            FnBDetails = fnbDetails,
            VoucherDetails = voucherDetails
        };

        return (true, null, result);
    }

    public async Task<(bool Succeeded, string? ErrorMessage, LookupBookingFnbResult? Result)> LookupBookingFnbAsync(
        string bookingCode,
        int staffId,
        CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetBookingByCodeAsync(bookingCode, cancellationToken);

        if (booking is null)
            return (false, "Booking not found.", null);

        if (booking.Status == BookingStatus.Cancelled)
            return (false, "Booking has been cancelled.", null);

        if (booking.Payment?.Status != PaymentStatus.Completed)
            return (false, "Booking has not been paid.", null);

        if (!booking.BookingFnBs.Any())
            return (false, "This booking does not contain F&B items.", null);

        var result = new LookupBookingFnbResult
        {
            BookingId = booking.BookingID,
            BookingCode = booking.BookingCode,
            CustomerName = booking.User?.FullName ?? string.Empty,
            CustomerPhone = booking.User?.Phone ?? string.Empty,
            CustomerAvatarURL = booking.User?.AvatarURL,
            PaymentStatus = booking.Payment?.Status ?? "pending",
            TotalAmount = booking.FinalAmount,
            FnbItems = booking.BookingFnBs.Select(fnb => new LookupBookingFnbResult.LookupBookingFnbItem
            {
                ItemId = fnb.ItemID,
                ItemName = fnb.Product.ItemName,
                ImageURL = fnb.Product.ImageURL,
                Quantity = fnb.Quantity,
                PickedUp = fnb.PickedUp
            }).ToList()
        };

        return (true, null, result);
    }

    private static string GenerateBookingCode()
    {
        return $"BK{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
    }

    private static SeatValidationErrors? GetSeatValidationErrors(
        IReadOnlyCollection<int> requestedSeatIds,
        IReadOnlyCollection<Seat> seats,
        int showtimeRoomId)
    {
        var foundSeatIds = seats.Select(seat => seat.SeatID).ToHashSet();
        var notFoundSeatIds = requestedSeatIds.Where(id => !foundSeatIds.Contains(id)).Order().ToList();
        var wrongRoomSeatIds = seats.Where(seat => seat.RoomID != showtimeRoomId)
            .Select(seat => seat.SeatID).Order().ToList();
        var inactiveSeatIds = seats.Where(seat => seat.RoomID == showtimeRoomId && seat.Status != "active")
            .Select(seat => seat.SeatID).Order().ToList();

        return notFoundSeatIds.Count == 0 && wrongRoomSeatIds.Count == 0 && inactiveSeatIds.Count == 0
            ? null
            : new SeatValidationErrors(notFoundSeatIds, wrongRoomSeatIds, inactiveSeatIds);
    }
}
