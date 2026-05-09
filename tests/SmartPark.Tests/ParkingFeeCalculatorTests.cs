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
