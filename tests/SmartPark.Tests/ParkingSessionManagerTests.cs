using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests;

public class ParkingSessionManagerTests
{
    private readonly Mock<IPaymentGateway>      _paymentStub      = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly Mock<IMembershipService>   _membershipStub   = new();
    private readonly Mock<IParkingRepository>   _repoStub         = new();
    private readonly Mock<IDateTimeProvider>    _dateTimeStub     = new();
    private readonly ParkingFeeCalculator       _feeCalculator    = new();
    private readonly ParkingSessionManager      _manager;

    public ParkingSessionManagerTests()
    {
        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            _membershipStub.Object,
            _repoStub.Object,
            _dateTimeStub.Object);

        // Default clock — Monday 9 AM
        _dateTimeStub.Setup(d => d.Now)
                     .Returns(new DateTime(2026, 3, 16, 9, 0, 0));
    }

    // ── Example test (already provided — keep it) ────────────────
    [Fact]
    public async Task CheckInAsync_NewVehicle_LookUpMembership()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("PP-9999"))
                       .Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-9999"))
                 .ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now)
                     .Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        // Act
        var ticket = await _manager.CheckInAsync("PP-9999", VehicleType.Car);

        // Assert
        _membershipStub.Verify(m => m.GetMembershipTier("PP-9999"), Times.Once);
        Assert.Equal("PP-9999", ticket.Vehicle.LicensePlate);
    }

    // ════════════════════════════════════════════════════════════
    #region CheckIn — Happy Path
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckInAsync_NewVehicle_SavesTicketAndLooksUpMembership()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("PP-1234"))
                       .Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-1234"))
                 .ReturnsAsync((ParkingTicket?)null);

        // Act
        var ticket = await _manager.CheckInAsync("PP-1234", VehicleType.Car);

        // Assert
        Assert.NotNull(ticket);
        Assert.Equal("PP-1234", ticket.Vehicle.LicensePlate);
        Assert.Equal(VehicleType.Car, ticket.Vehicle.Type);

        // Verify membership was looked up exactly once
        _membershipStub.Verify(
            m => m.GetMembershipTier("PP-1234"), Times.Once);

        // Verify ticket was saved exactly once
        _repoStub.Verify(
            r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region CheckIn — Validation
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckInAsync_DuplicatePlate_ThrowsAndDoesNotSave()
    {
        // Arrange — plate already has an active session
        _membershipStub.Setup(m => m.GetMembershipTier("PP-1234"))
                       .Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-1234"))
                 .ReturnsAsync(new ParkingTicket
                 {
                     Vehicle     = new Vehicle { LicensePlate = "PP-1234" },
                     CheckInTime = new DateTime(2026, 3, 16, 8, 0, 0),
                     // CheckOutTime = null → IsActive = true
                 });

        // Act & Assert — must throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CheckInAsync("PP-1234", VehicleType.Car));

        // Verify SaveTicketAsync was NEVER called
        _repoStub.Verify(
            r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region CheckOut — Happy Path
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckOutAsync_ValidTicket_ProcessesPaymentAndSendsReceipt()
    {
        // Arrange
        var existingTicket = new ParkingTicket
        {
            TicketId    = "T-001",
            Vehicle     = new Vehicle
            {
                LicensePlate = "PP-1234",
                Type         = VehicleType.Car,
                Membership   = MembershipTier.Guest
            },
            CheckInTime  = new DateTime(2026, 3, 16, 9, 0, 0),
            CheckOutTime = null  // IsActive = true
        };

        // Clock: 2 hours after check-in
        _dateTimeStub.Setup(d => d.Now)
                     .Returns(new DateTime(2026, 3, 16, 11, 0, 0));

        _repoStub.Setup(r => r.GetTicketByIdAsync("T-001"))
                 .ReturnsAsync(existingTicket);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        // Act
        var result = await _manager.CheckOutAsync("T-001", "012345678");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2_000m, result.TotalFee); // 2h × 1,000 KHR = 2,000

        // Payment processed once
        _paymentStub.Verify(
            p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()), Times.Once);

        // Receipt sent once — SendReceiptAsync(string phone, string content)
        _notificationStub.Verify(
            n => n.SendReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        // Ticket updated in repo
        _repoStub.Verify(
            r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Once);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region CheckOut — Payment Failure
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckOutAsync_PaymentFails_ThrowsAndNoReceiptSent()
    {
        // Arrange
        var existingTicket = new ParkingTicket
        {
            TicketId    = "T-002",
            Vehicle     = new Vehicle
            {
                Type       = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime  = new DateTime(2026, 3, 16, 9, 0, 0),
            CheckOutTime = null
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync("T-002"))
                 .ReturnsAsync(existingTicket);

        // Payment throws
        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ThrowsAsync(new Exception("Payment gateway error"));

        // Act & Assert — exception must propagate
        await Assert.ThrowsAsync<Exception>(
            () => _manager.CheckOutAsync("T-002", "012345678"));

        // Receipt must NEVER be sent
        _notificationStub.Verify(
            n => n.SendReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // Ticket must NOT be updated
        _repoStub.Verify(
            r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region CheckOut — Notification Failure
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckOutAsync_NotificationFails_CheckoutStillSucceeds()
    {
        // Arrange
        var existingTicket = new ParkingTicket
        {
            TicketId    = "T-003",
            Vehicle     = new Vehicle
            {
                Type       = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime  = new DateTime(2026, 3, 16, 9, 0, 0),
            CheckOutTime = null
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync("T-003"))
                 .ReturnsAsync(existingTicket);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        // Notification is down — throws
        _notificationStub.Setup(n => n.SendReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP server down"));

        // Act — must NOT throw even though notification failed
        var result = await _manager.CheckOutAsync("T-003", "012345678");

        // Assert — checkout still completed successfully
        Assert.NotNull(result);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region CheckOut — Validation
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckOutAsync_TicketNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange — repo returns null for this ID
        _repoStub.Setup(r => r.GetTicketByIdAsync("MISSING-999"))
                 .ReturnsAsync((ParkingTicket?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _manager.CheckOutAsync("MISSING-999", "012345678"));
    }

    [Fact]
    public async Task CheckOutAsync_AlreadyCheckedOut_ThrowsAndNoPaymentAttempted()
    {
        // Arrange — CheckOutTime is set → IsActive = false
        var checkedOutTicket = new ParkingTicket
        {
            TicketId     = "T-004",
            CheckInTime  = new DateTime(2026, 3, 16, 9,  0, 0),
            CheckOutTime = new DateTime(2026, 3, 16, 11, 0, 0) // already done
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync("T-004"))
                 .ReturnsAsync(checkedOutTicket);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.CheckOutAsync("T-004", "012345678"));

        // No payment should ever be attempted
        _paymentStub.Verify(
            p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()), Times.Never);
    }

    #endregion

    // ════════════════════════════════════════════════════════════
    #region Verify Interaction Order
    // ════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckOutAsync_SuccessfulFlow_PaymentBeforeNotification()
    {
        // Arrange
        var callOrder = new List<string>();

        var existingTicket = new ParkingTicket
        {
            TicketId    = "T-005",
            Vehicle     = new Vehicle
            {
                Type       = VehicleType.Car,
                Membership = MembershipTier.Guest
            },
            CheckInTime  = new DateTime(2026, 3, 16, 9, 0, 0),
            CheckOutTime = null
        };

        _repoStub.Setup(r => r.GetTicketByIdAsync("T-005"))
                 .ReturnsAsync(existingTicket);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(
                It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true)
            .Callback(() => callOrder.Add("payment"));

        _notificationStub.Setup(n => n.SendReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask)
            .Callback(() => callOrder.Add("notification"));

        // Act
        await _manager.CheckOutAsync("T-005", "012345678");

        // Assert — payment must come before notification
        Assert.Equal(new[] { "payment", "notification" }, callOrder);
    }

    #endregion
}