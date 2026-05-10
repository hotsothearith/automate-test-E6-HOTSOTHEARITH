using SmartPark.Core.Models;

namespace SmartPark.Core.Services;

/// <summary>
/// Core pricing engine. Pure calculation service with no external dependencies.
/// Students: implement this class using TDD (Red-Green-Refactor).
/// </summary>
public class ParkingFeeCalculator
{
    // ── Pricing constants (from spec §4) ────────────────────────

    // Hourly rates (KHR)
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour = 1_000m;
    private const decimal SuvRatePerHour = 1_500m;

    // Daily caps (KHR)
    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap = 8_000m;
    private const decimal SuvDailyCap = 12_000m;

    // Time-based rules
    private const int GracePeriodMinutes = 30;
    private const decimal OvernightFlatFee = 2_000m;
    private const int OvernightHourThreshold = 22; // 10 PM

    // Surcharges
    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    // Membership discounts
    private const decimal SilverDiscountRate = 0.10m;
    private const decimal GoldDiscountRate = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    // Penalties
    private const decimal LostTicketPenalty = 20_000m;

    /// <summary>
    /// Calculates the parking fee following the 9-step flow in the spec.
    /// </summary>
    /// <remarks>
    /// Steps:
    ///   1. Validate: checkOut before checkIn → ArgumentException
    ///   2. Grace period: total ≤ 30 min → free (lost-ticket penalty still applies)
    ///   3. Duration: billableHours = ⌈(totalMinutes − 30) / 60⌉, min 1
    ///   4. Base fee: billableHours × hourlyRate, capped at dailyCap
    ///   5. Overnight: +2,000 KHR if session spans past 22:00
    ///   6. Surcharge: weekend +20% OR holiday +50% on baseFee (not both)
    ///   7. Discount: (baseFee + surcharge) × membershipRate
    ///   8. Lost ticket: +20,000 KHR (not subject to discounts)
    ///   9. Total: baseFee + surcharge − discount + overnight + penalty (min 0)
    /// </remarks>
    public ParkingFeeResult CalculateFee(
    VehicleType vehicleType,
    MembershipTier membership,
    DateTime checkIn,
    DateTime checkOut,
    bool isLostTicket = false,
    bool isHoliday = false)
{
    // Step 1: Validate
    if (checkOut < checkIn)
        throw new ArgumentException(
            "checkOut cannot be before checkIn", nameof(checkOut));

    // Step 2: Grace period
    var totalMinutes = (checkOut - checkIn).TotalMinutes;

    if (totalMinutes <= GracePeriodMinutes)
    {
        var penalty = isLostTicket ? LostTicketPenalty : 0m;

        return new ParkingFeeResult
        {
            TotalFee = penalty,
            BaseFee = 0m,
            SurchargeAmount = 0m,
            DiscountAmount = 0m,
            LostTicketPenalty = penalty,
            Breakdown = $"Grace period. Penalty: {penalty:N0} KHR"
        };
    }

    // Step 3: Billable hours
    var billableHours = Math.Ceiling(
        (totalMinutes - GracePeriodMinutes) / 60.0);

    if (billableHours < 1)
        billableHours = 1;

    // Step 4: Hourly rate
    var hourlyRate = vehicleType switch
    {
        VehicleType.Motorcycle => MotorcycleRatePerHour,
        VehicleType.Car => CarRatePerHour,
        VehicleType.SUV => SuvRatePerHour,
        _ => throw new ArgumentException("Unknown vehicle type")
    };

    // Step 4: Daily cap
    var dailyCap = vehicleType switch
    {
        VehicleType.Motorcycle => MotorcycleDailyCap,
        VehicleType.Car => CarDailyCap,
        VehicleType.SUV => SuvDailyCap,
        _ => throw new ArgumentException("Unknown vehicle type")
    };

    var baseFee = Math.Min((decimal)billableHours * hourlyRate, dailyCap);

    // Step 5: Overnight fee
    var overnightFee = GoesPastOvernightThreshold(checkIn, checkOut)
        ? OvernightFlatFee
        : 0m;

    
    return new ParkingFeeResult
    {
        TotalFee = baseFee + overnightFee,
        BaseFee = baseFee,
        SurchargeAmount = 0m,
        DiscountAmount = 0m,
        LostTicketPenalty = 0m,
        Breakdown = $"Base Fee: {baseFee:N0} KHR; Overnight Fee: {overnightFee:N0} KHR"
    };
}


    private static bool GoesPastOvernightThreshold(DateTime checkIn, DateTime checkOut)
    {
        var currentDate = checkIn.Date;

        while (currentDate <= checkOut.Date)
        {
            var tenPm = currentDate.AddHours(OvernightHourThreshold);

            if (checkIn < tenPm && checkOut > tenPm)
                return true;

            currentDate = currentDate.AddDays(1);
        }

        return false;
    }
}

