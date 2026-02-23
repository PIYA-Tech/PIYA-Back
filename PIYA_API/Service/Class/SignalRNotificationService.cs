using Microsoft.AspNetCore.SignalR;
using PIYA_API.Hubs;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class SignalRNotificationService(IHubContext<NotificationHub> hubContext) : ISignalRNotificationService
{
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;

    public async Task SendToUserAsync(Guid userId, string notificationType, object notification)
    {
        await _hubContext.Clients
            .Group($"user_{userId}")
            .SendAsync("ReceiveNotification", notificationType, notification, DateTime.UtcNow);
    }

    public async Task SendToGroupAsync(string groupName, string notificationType, object notification)
    {
        await _hubContext.Clients
            .Group(groupName)
            .SendAsync("ReceiveNotification", notificationType, notification, DateTime.UtcNow);
    }

    public async Task SendToAllAsync(string notificationType, object notification)
    {
        await _hubContext.Clients.All
            .SendAsync("ReceiveNotification", notificationType, notification, DateTime.UtcNow);
    }

    public async Task SendAppointmentNotificationAsync(Guid userId, string message, Guid appointmentId)
    {
        var notification = new
        {
            Message = message,
            AppointmentId = appointmentId,
            Timestamp = DateTime.UtcNow
        };

        await SendToUserAsync(userId, "AppointmentUpdate", notification);
    }

    public async Task SendPrescriptionNotificationAsync(Guid userId, string message, Guid prescriptionId)
    {
        var notification = new
        {
            Message = message,
            PrescriptionId = prescriptionId,
            Timestamp = DateTime.UtcNow
        };

        await SendToUserAsync(userId, "PrescriptionUpdate", notification);
    }

    public async Task SendQRCodeScanNotificationAsync(Guid userId, string message, string qrCode)
    {
        var notification = new
        {
            Message = message,
            QRCode = qrCode,
            Timestamp = DateTime.UtcNow
        };

        await SendToUserAsync(userId, "QRCodeScanned", notification);
    }
}
