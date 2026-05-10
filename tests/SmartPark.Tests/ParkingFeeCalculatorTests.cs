using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows the naming convention and AAA pattern.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var checkOut = checkIn; // same time = 0 duration

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    // ════════════════════════════════════════════════════════════
    #region Basic Fee Calculation
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(VehicleType.Motorcycle, 2,  1_000)]
    [InlineData(VehicleType.Car,        3,  3_000)]
    [InlineData(VehicleType.SUV,        1,  1_500)]
    public void CalculateFee_BasicRate_ReturnsCorrectFee(
        VehicleType vehicleType, int hours, decimal expectedFee)
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(
            vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Grace Period
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_GracePeriod_Exactly30Min_ReturnsFree()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(30);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
        Assert.Equal(0m, result.BaseFee);
    }

    [Fact]
    public void CalculateFee_GracePeriod_31Min_ChargesOneHour()
    {
        // Arrange — 1 min past grace = 1 billable hour
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(31);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(1_000m, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Duration Rounding
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_DurationRounding_PartialHourCeilsUp()
    {
        // Arrange — 1h 31min total − 30 grace = 61min → ceil(61/60) = 2 hours
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(1).AddMinutes(31);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(2_000m, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Daily Cap
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(VehicleType.Motorcycle, 10,  4_000)]
    [InlineData(VehicleType.Car,        12,  8_000)]
    [InlineData(VehicleType.SUV,        24, 12_000)]
    public void CalculateFee_DailyCap_BaseFeeDoesNotExceedCap(
        VehicleType vehicleType, int hours, decimal expectedCap)
    {
        // Arrange — Monday so no surcharge interferes
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(
            vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedCap, result.BaseFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Overnight Fee
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_Overnight_SessionCrosses10PM_AddsOvernightFee()
    {
        // Arrange — 8 PM to 11 PM crosses 22:00
        var checkIn  = new DateTime(2026, 3, 16, 20, 0, 0); // Monday 8 PM
        var checkOut = new DateTime(2026, 3, 16, 23, 0, 0); // Monday 11 PM

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert — ceil((180-30)/60) = 3h × 1000 = 3000 + 2000 overnight = 5000
        Assert.Equal(5_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_Overnight_DaytimeSession_NoOvernightFee()
    {
        // Arrange — 9 AM to 5 PM, never reaches 22:00
        var checkIn  = new DateTime(2026, 3, 16, 9,  0, 0);
        var checkOut = new DateTime(2026, 3, 16, 17, 0, 0);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert — capped at daily cap 8,000, no overnight
        Assert.Equal(8_000m, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Weekend Surcharge
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_WeekendSurcharge_Saturday_AddsTwentyPercent()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 21, 9, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert — 2,000 base + 20% = 2,400
        Assert.Equal(2_400m, result.TotalFee);
        Assert.Equal(400m,   result.SurchargeAmount);
    }

    [Fact]
    public void CalculateFee_Weekday_Monday_NoSurcharge()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(2_000m, result.TotalFee);
        Assert.Equal(0m,     result.SurchargeAmount);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Holiday Surcharge
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_HolidaySurcharge_AddsFiftyPercent()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday + holiday
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isHoliday: true);

        // Assert — 2,000 base + 50% = 3,000
        Assert.Equal(3_000m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_HolidaySurcharge_TakesPriorityOverWeekend()
    {
        // Arrange — Saturday AND holiday — must be 50% only, NOT 20%+50%
        var checkIn  = new DateTime(2026, 3, 21, 9, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isHoliday: true);

        // Assert
        Assert.Equal(3_000m, result.TotalFee);
        Assert.Equal(1_000m, result.SurchargeAmount);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Membership Discounts
    // ════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(MembershipTier.Silver,   1_800)] // 2,000 − 10% = 1,800
    [InlineData(MembershipTier.Gold,     1_500)] // 2,000 − 25% = 1,500
    [InlineData(MembershipTier.Platinum, 1_200)] // 2,000 − 40% = 1,200
    public void CalculateFee_MembershipDiscount_ReducesFee(
        MembershipTier tier, decimal expectedFee)
    {
        // Arrange — Monday 2 hours, no surcharge
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, tier, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_GuestMembership_NoDiscount()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m,     result.DiscountAmount);
        Assert.Equal(2_000m, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Lost Ticket
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_LostTicket_AddsPenalty()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isLostTicket: true);

        // Assert — 2,000 normal + 20,000 penalty = 22,000
        Assert.Equal(22_000m, result.TotalFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    [Fact]
    public void CalculateFee_LostTicket_DuringGracePeriod_OnlyPenalty()
    {
        // Arrange — grace period: base = 0, penalty still applies
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddMinutes(30);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isLostTicket: true);

        // Assert
        Assert.Equal(20_000m, result.TotalFee);
        Assert.Equal(0m,      result.BaseFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    [Fact]
    public void CalculateFee_LostTicket_DiscountDoesNotReducePenalty()
    {
        // Arrange — Platinum gets 40% off base, but NOT off penalty
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(2);

        // Act
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Platinum, checkIn, checkOut,
            isLostTicket: true);

        // Assert — 2,000 base − 40% = 1,200 + 20,000 penalty = 21,200
        Assert.Equal(21_200m, result.TotalFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Edge Cases
    // ════════════════════════════════════════════════════════════

    [Fact]
    public void CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException()
    {
        // Arrange
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = new DateTime(2026, 3, 16,  8, 0, 0); // before checkIn

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _calculator.CalculateFee(
                VehicleType.Car, MembershipTier.Guest, checkIn, checkOut));
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Property-Based Tests
    // ════════════════════════════════════════════════════════════

    // Custom generator — produces valid (CheckIn, CheckOut) pairs.
    // NOTE: tuple fields named CheckIn/CheckOut so pair.CheckIn works everywhere.
    private static Arbitrary<(DateTime CheckIn, DateTime CheckOut)> ValidDatePairs()
    {
        var gen =
            from startMins in Gen.Choose(
                (int)(new DateTime(2024, 1, 1).Ticks / TimeSpan.TicksPerMinute),
                (int)(new DateTime(2026, 12, 31).Ticks / TimeSpan.TicksPerMinute))
            from durationMins in Gen.Choose(1, 2880) // 1 min to 48 hours
            let CheckIn  = new DateTime((long)startMins * TimeSpan.TicksPerMinute)
            let CheckOut = CheckIn.AddMinutes(durationMins)
            select (CheckIn, CheckOut);
        return gen.ToArbitrary();
    }

    // Grace-period-only generator — duration always 0–30 min
    private static Arbitrary<(DateTime CheckIn, DateTime CheckOut)> GracePeriodPairs()
    {
        var gen =
            from startMins in Gen.Choose(
                (int)(new DateTime(2024, 1, 1).Ticks / TimeSpan.TicksPerMinute),
                (int)(new DateTime(2026, 12, 31).Ticks / TimeSpan.TicksPerMinute))
            from mins in Gen.Choose(0, 30)
            let CheckIn  = new DateTime((long)startMins * TimeSpan.TicksPerMinute)
            let CheckOut = CheckIn.AddMinutes(mins)
            select (CheckIn, CheckOut);
        return gen.ToArbitrary();
    }

    // Property 1: TotalFee is NEVER negative
    [Property]
    public Property FeeIsNeverNegative()
    {
        return Prop.ForAll(ValidDatePairs(), pair =>
        {
            var result = _calculator.CalculateFee(
                VehicleType.Car, MembershipTier.Guest,
                pair.CheckIn, pair.CheckOut);
            return result.TotalFee >= 0m;
        });
    }

    // Property 2: Grace period (≤30 min) → BaseFee is always 0
    [Property]
    public Property GracePeriodAlwaysFree()
    {
        return Prop.ForAll(GracePeriodPairs(), pair =>
        {
            var result = _calculator.CalculateFee(
                VehicleType.Car, MembershipTier.Guest,
                pair.CheckIn, pair.CheckOut);
            return result.BaseFee == 0m;
        });
    }

    // Property 3: Member fee is always <= guest fee
    [Property]
    public Property MemberPaysLessOrEqualToGuest()
    {
        return Prop.ForAll(ValidDatePairs(), pair =>
        {
            var guest    = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest,    pair.CheckIn, pair.CheckOut).TotalFee;
            var platinum = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Platinum, pair.CheckIn, pair.CheckOut).TotalFee;
            return platinum <= guest;
        });
    }

    // Property 4: Lost ticket adds EXACTLY 20,000 KHR
    [Property]
    public Property LostTicketAddsExactPenalty()
    {
        return Prop.ForAll(ValidDatePairs(), pair =>
        {
            var normal = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, pair.CheckIn, pair.CheckOut, isLostTicket: false).TotalFee;
            var lost   = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, pair.CheckIn, pair.CheckOut, isLostTicket: true).TotalFee;
            return (lost - normal) == 20_000m;
        });
    }

    // Property 5: BaseFee never exceeds daily cap for any vehicles
    [Property]
    public Property DailyCapNeverExceeded()
    {
        return Prop.ForAll(ValidDatePairs(), pair =>
        {
            var car  = _calculator.CalculateFee(VehicleType.Car,        MembershipTier.Guest, pair.CheckIn, pair.CheckOut).BaseFee;
            var moto = _calculator.CalculateFee(VehicleType.Motorcycle,  MembershipTier.Guest, pair.CheckIn, pair.CheckOut).BaseFee;
            var suv  = _calculator.CalculateFee(VehicleType.SUV,         MembershipTier.Guest, pair.CheckIn, pair.CheckOut).BaseFee;
            return car <= 8_000m && moto <= 4_000m && suv <= 12_000m;
        });
    }

    #endregion
}
