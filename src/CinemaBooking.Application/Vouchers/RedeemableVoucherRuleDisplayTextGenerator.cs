using CinemaBooking.Shared.Constants;

namespace CinemaBooking.Application.Vouchers;

public static class RedeemableVoucherRuleDisplayTextGenerator
{
    public static string GenerateDisplayText(
        string ruleType,
        string ruleValue,
        Dictionary<int, string>? movieNames = null,
        Dictionary<int, string>? cinemaNames = null,
        Dictionary<int, string>? seatTypeNames = null,
        Dictionary<int, string>? tierNames = null,
        Dictionary<int, string>? roomNames = null)
    {
        return ruleType switch
        {
            "MinimumSpend" => $"Đơn hàng từ {FormatCurrency(ruleValue)}",
            "MaximumSpend" => $"Đơn hàng tối đa {FormatCurrency(ruleValue)}",
            "Movie" => GetMovieDisplayText(ruleValue, movieNames),
            "Cinema" => GetCinemaDisplayText(ruleValue, cinemaNames),
            "SeatType" => GetSeatTypeDisplayText(ruleValue, seatTypeNames),
            "Room" => GetRoomDisplayText(ruleValue, roomNames),
            "TicketQuantity" => $"Mua tối thiểu {ruleValue} vé",
            "Membership" => GetMembershipDisplayText(ruleValue, tierNames),
            "FoodAndDrink" => "Chỉ áp dụng khi mua F&B",
            "PaymentMethod" => $"Chỉ áp dụng cho thanh toán bằng {ruleValue}",
            "DayOfWeek" => $"Chỉ áp dụng vào {ruleValue}",
            "Product" => $"Chỉ áp dụng cho sản phẩm {ruleValue}",
            "FoodCategory" => $"Chỉ áp dụng cho danh mục F&B {ruleValue}",
            "ApplyScope" => $"Áp dụng cho {ruleValue}",
            _ => $"{ruleType}: {ruleValue}"
        };
    }

    private static string GetMovieDisplayText(string ruleValue, Dictionary<int, string>? movieNames)
    {
        if (int.TryParse(ruleValue, out var movieId) && movieNames?.TryGetValue(movieId, out var name) == true)
            return $"Chỉ áp dụng cho phim {name}";
        return $"Chỉ áp dụng cho phim ID {ruleValue}";
    }

    private static string GetCinemaDisplayText(string ruleValue, Dictionary<int, string>? cinemaNames)
    {
        if (int.TryParse(ruleValue, out var cinemaId) && cinemaNames?.TryGetValue(cinemaId, out var name) == true)
            return $"Chỉ áp dụng tại {name}";
        return $"Chỉ áp dụng tại Cinema ID {ruleValue}";
    }

    private static string GetSeatTypeDisplayText(string ruleValue, Dictionary<int, string>? seatTypeNames)
    {
        if (int.TryParse(ruleValue, out var seatTypeId) && seatTypeNames?.TryGetValue(seatTypeId, out var name) == true)
            return $"Chỉ áp dụng cho ghế {name}";
        return $"Chỉ áp dụng cho ghế loại {ruleValue}";
    }

    private static string GetMembershipDisplayText(string ruleValue, Dictionary<int, string>? tierNames)
    {
        if (int.TryParse(ruleValue, out var tierId) && tierNames?.TryGetValue(tierId, out var name) == true)
            return $"Chỉ áp dụng cho thành viên {name}";
        return $"Chỉ áp dụng cho thành viên loại {ruleValue}";
    }

    private static string GetRoomDisplayText(string ruleValue, Dictionary<int, string>? roomNames)
    {
        if (int.TryParse(ruleValue, out var roomId) && roomNames?.TryGetValue(roomId, out var name) == true)
            return $"Chỉ áp dụng trong phòng {name}";
        return $"Chỉ áp dụng trong phòng ID {ruleValue}";
    }

    private static string FormatCurrency(string value)
    {
        if (decimal.TryParse(value, out var amount))
            return $"{amount:N0}đ";
        return value;
    }
}


