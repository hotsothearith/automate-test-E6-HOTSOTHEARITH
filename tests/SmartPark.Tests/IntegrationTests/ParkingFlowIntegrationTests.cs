using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests.IntegrationTests;

public class ParkingFlowIntegrationTests
{
    // ────────────────────────────────────────────────────────────
    //  INTEGRATION TEST SETUP
    //  Real:   ParkingFeeCalculator, InMemoryParkingRepository
    //  Mocked: IPaymentGateway, INotificationService,
    //          IDateTimeProvider, IMembershipService
    // ────────────────────────────────────────────────────────────

    private readonly ParkingFeeCalculator      _feeCalculator = new();
    private readonly InMemoryParkingRepository _repository    = new();
    private readonly Mock<IPaymentGateway>      _paymentStub      = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly ParkingSessionManager _manager;

    // Fake clock — set _currentTime in each test to control time
    private DateTime _currentTime = new(2026, 3, 16, 10, 0, 0); // Monday 10 AM

    public ParkingFlowIntegrationTests()
    {
        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>()))
                      .Returns(MembershipTier.Guest);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        _notificationStub.Setup(n => n.SendReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,        // real in-memory repo
            dateTimeStub.Object);
    }

    // ── Example test (already provided — keep it) ────────────────
    [Fact]
    public async Task FullFlow_CheckInAndCheckOut_CalculatesCorrectFee()
    {
        // Arrange — check in at 10:00 AM Monday
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("TEST-001", VehicleType.Car);

        // Act — check out at 12:30 PM (2.5 hours → 2 billable hours after grace)
        _currentTime = new DateTime(2026, 3, 16, 12, 30, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert — Car: 2 hours × 1,000 = 2,000 KHR
        Assert.Equal(2_000m, result.TotalFee);
    }

    // ════════════════════════════════════════════════════════════
    #region Full Parking Flow
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullFlow_GracePeriod_FeeIsZeroAndTicketClosed()
    {
        // Arrange — check in at 10:00 AM
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("GRACE-001", VehicleType.Car);

        // Act — check out only 20 minutes later (within grace period)
        _currentTime = new DateTime(2026, 3, 16, 10, 20, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012345678");

        // Assert — fee is 0, ticket is closed
        Assert.Equal(0m, result.TotalFee);
        Assert.False(ticket.IsActive); // CheckOutTime was set
    }

    [Fact]
    public async Task FullFlow_LostTicket_PenaltyAdded()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("LOST-001", VehicleType.Car);

        // Act — 2 hours later with lost ticket flag
        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);
        var result = await _manager.CheckOutAsync(
            ticket.TicketId, "012345678", isLostTicket: true);

        // Assert — 2,000 normal + 20,000 penalty = 22,000
        Assert.Equal(22_000m, result.TotalFee);
        Assert.Equal(20_000m, result.LostTicketPenalty);
    }

    [Fact]
    public async Task FullFlow_WeekendSurcharge_CorrectFeeCalculated()
    {
        // Arrange — Saturday check-in
        _currentTime = new DateTime(2026, 3, 21, 9, 0, 0); // Saturday
        var ticket = await _manager.CheckInAsync("SAT-001", VehicleType.Car);

        // Act — 2 hours later
        _currentTime = new DateTime(2026, 3, 21, 11, 0, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012345678");

        // Assert — 2,000 base + 20% weekend = 2,400
        Assert.Equal(2_400m, result.TotalFee);
        Assert.Equal(400m,   result.SurchargeAmount);
    }

    [Fact]
    public async Task FullFlow_HolidaySurcharge_CorrectFeeCalculated()
    {
        // Arrange — Monday + holiday flag
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("HOL-001", VehicleType.Car);

        // Act — 2 hours later with holiday flag
        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);
        var result = await _manager.CheckOutAsync(
            ticket.TicketId, "012345678", isHoliday: true);

        // Assert — 2,000 base + 50% holiday = 3,000
        Assert.Equal(3_000m, result.TotalFee);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Multiple Vehicles
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task MultipleVehicles_CheckIn3CheckOut1_2RemainActive()
    {
        // Arrange — check in 3 different vehicles
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var t1 = await _manager.CheckInAsync("CAR-001", VehicleType.Car);
        var t2 = await _manager.CheckInAsync("CAR-002", VehicleType.Car);
        var t3 = await _manager.CheckInAsync("CAR-003", VehicleType.Car);

        // Act — advance time and check out only t1
        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);
        await _manager.CheckOutAsync(t1.TicketId, "012000001");

        // Assert
        Assert.False(t1.IsActive); // CheckOutTime was set
        Assert.True(t2.IsActive);  // still parked
        Assert.True(t3.IsActive);  // still parked
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Error Recovery
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task DuplicateCheckIn_SamePlate_ThrowsInvalidOperationException()
    {
        // Arrange — first check-in succeeds
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        await _manager.CheckInAsync("DUP-001", VehicleType.Car);

        // Act & Assert — second check-in same plate throws
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CheckInAsync("DUP-001", VehicleType.Car));
    }

    [Fact]
    public async Task CheckOut_PaymentFails_TicketRemainsActive()
    {
        // Arrange
        _currentTime = new DateTime(2026, 3, 16, 9, 0, 0);
        var ticket = await _manager.CheckInAsync("FAIL-001", VehicleType.Car);

        // Override payment to fail for this test
        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ThrowsAsync(new Exception("Payment gateway down"));

        _currentTime = new DateTime(2026, 3, 16, 11, 0, 0);

        // Act — swallow the exception
        await Assert.ThrowsAsync<Exception>(
            () => _manager.CheckOutAsync(ticket.TicketId, "012345678"));

        // Assert — ticket must still be active (no state change on failure)
        Assert.True(ticket.IsActive);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Edge-to-Edge Scenarios
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task FullFlow_MorningCheckIn_EveningCheckOut_HitsDailyCap()
    {
        // Arrange — check in early morning
        _currentTime = new DateTime(2026, 3, 16, 8, 0, 0); // Monday 8 AM
        var ticket = await _manager.CheckInAsync("CAP-001", VehicleType.Car);

        // Act — check out 12 hours later (hits daily cap)
        _currentTime = new DateTime(2026, 3, 16, 20, 0, 0); // 8 PM
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012345678");

        // Assert — BaseFee capped at 8,000 KHR
        Assert.Equal(8_000m, result.BaseFee);
    }

    #endregion
}