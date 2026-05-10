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
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);  // Monday
        var checkOut = checkIn; // same time = 0 duration

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_CheckOutBeforeCheckIn_ThrowsArgumentException()
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = new DateTime(2026, 3, 16,  8, 0, 0);

        Assert.Throws<ArgumentException>(() =>
            _calculator.CalculateFee(
                VehicleType.Car, MembershipTier.Guest, checkIn, checkOut));
    }

    [Fact]
    public void CalculateFee_GracePeriod_Exactly30Min_ReturnsFree()
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(30);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(0m, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_GracePeriod_31Min_ChargesOneHour()
    {
        var checkIn  = new DateTime(2026, 3, 16, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(31);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(1_000m, result.TotalFee);
    }

    [Theory]
    [InlineData(VehicleType.Motorcycle, 2,  1_000)]
    [InlineData(VehicleType.Car,        3,  3_000)]
    [InlineData(VehicleType.SUV,        1,  1_500)]
    public void CalculateFee_BasicRate_ReturnsCorrectFee(
        VehicleType vehicleType, int hours, decimal expectedFee)
    {
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday
        var checkOut = checkIn.AddHours(hours);
        var result = _calculator.CalculateFee(
            vehicleType, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(expectedFee, result.TotalFee);
    }

    [Theory]
    [InlineData(VehicleType.Motorcycle, 10,  4_000)]
    [InlineData(VehicleType.Car,        12,  8_000)]
    [InlineData(VehicleType.SUV,        24, 12_000)]
    public void CalculateFee_DailyCap_BaseFeeDoesNotExceedCap(
        VehicleType vehicleType, int hours, decimal expectedCap)
    {
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday
        var checkOut = checkIn.AddHours(hours);
        var result = _calculator.CalculateFee(
            vehicleType, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(expectedCap, result.BaseFee);
    }

    [Fact]
    public void CalculateFee_Overnight_SessionCrosses10PM_AddsOvernightFee()
    {
        var checkIn  = new DateTime(2026, 3, 16, 20, 0, 0); // 8 PM
        var checkOut = new DateTime(2026, 3, 16, 23, 0, 0); // 11 PM
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(5_000m, result.TotalFee); // 3h base + 2000 overnight
    }

    [Fact]
    public void CalculateFee_Overnight_DaytimeSession_NoOvernightFee()
    {
        var checkIn  = new DateTime(2026, 3, 16, 9,  0, 0);
        var checkOut = new DateTime(2026, 3, 16, 17, 0, 0);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(8_000m, result.TotalFee); // capped at daily cap
    }
    [Fact]
    public void CalculateFee_WeekendSurcharge_Saturday_AddsTwentyPercent()
    {
        var checkIn  = new DateTime(2026, 3, 21, 9, 0, 0); // Saturday
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);
        Assert.Equal(2_400m, result.TotalFee);
        Assert.Equal(400m,   result.SurchargeAmount);
    }

    [Fact]
    public void CalculateFee_HolidaySurcharge_TakesPriorityOverWeekend()
    {
        var checkIn  = new DateTime(2026, 3, 21, 9, 0, 0); // Saturday + holiday
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isHoliday: true);
        Assert.Equal(3_000m, result.TotalFee); // 50% only, NOT 20%+50%
    }
    [Theory]
    [InlineData(MembershipTier.Silver,   1_800)]
    [InlineData(MembershipTier.Gold,     1_500)]
    [InlineData(MembershipTier.Platinum, 1_200)]
    public void CalculateFee_MembershipDiscount_ReducesFee(
        MembershipTier tier, decimal expectedFee)
    {
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0); // Monday
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(
            VehicleType.Car, tier, checkIn, checkOut);
        Assert.Equal(expectedFee, result.TotalFee);
    }

    [Fact]
    public void CalculateFee_LostTicket_AddsPenalty()
    {
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddHours(2);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isLostTicket: true);
        Assert.Equal(22_000m, result.TotalFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    [Fact]
    public void CalculateFee_LostTicket_DuringGracePeriod_OnlyPenalty()
    {
        var checkIn  = new DateTime(2026, 3, 16, 9, 0, 0);
        var checkOut = checkIn.AddMinutes(30);
        var result = _calculator.CalculateFee(
            VehicleType.Car, MembershipTier.Guest, checkIn, checkOut,
            isLostTicket: true);
        Assert.Equal(20_000m, result.TotalFee);
        Assert.Equal(0m,      result.BaseFee);
    }
    #region Basic Fee Calculation
    // Test basic hourly rates for each vehicle type
    // Consider using [Theory] with [InlineData] for multiple scenarios
    #endregion

    #region Grace Period
    // Test the free parking window and its boundaries
    #endregion

    #region Duration Rounding
    // Test how partial hours are rounded for billing
    #endregion

    #region Daily Cap
    // Test that fees respect maximum daily limits per vehicle type
    #endregion

    #region Overnight Fee
    // Test the flat fee applied for sessions that extend into late hours
    #endregion

    #region Weekend Surcharge
    // Test the percentage-based surcharge on specific days
    #endregion

    #region Holiday Surcharge
    // Test holiday pricing and its interaction with weekend pricing
    #endregion

    #region Membership Discounts
    // Test discount tiers and what amounts they apply to
    #endregion

    #region Lost Ticket
    // Test the penalty and how it interacts with other fee modifiers
    #endregion

    #region Edge Cases
    // Test invalid inputs and boundary conditions
    #endregion

    #region Property-Based Tests
    // Write at least 5 FsCheck properties that must hold for ALL valid inputs
    // You may need custom Arbitrary<T> for generating valid DateTime pairs
    #endregion
}
