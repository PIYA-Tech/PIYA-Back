namespace PIYA_API.Service.Interface;

/// <summary>
/// Email service for sending transactional emails
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send email verification email
    /// </summary>
    Task SendEmailVerificationAsync(string toEmail, string userName, string verificationToken, string verificationUrl);
    
    /// <summary>
    /// Send password reset email
    /// </summary>
    Task SendPasswordResetAsync(string toEmail, string userName, string resetToken, string resetUrl);
    
    /// <summary>
    /// Send appointment confirmation email
    /// </summary>
    Task SendAppointmentConfirmationAsync(string toEmail, string patientName, DateTime appointmentDate, string doctorName, string hospitalName);
    
    /// <summary>
    /// Send appointment cancellation email (sent to patient when doctor or admin cancels)
    /// </summary>
    Task SendAppointmentCancelledAsync(string toEmail, string patientName, DateTime appointmentDate, string doctorName, string cancelledBy, string? reason);

    /// <summary>
    /// Send appointment rescheduled email (sent to patient when doctor reschedules)
    /// </summary>
    Task SendAppointmentRescheduledAsync(string toEmail, string patientName, DateTime oldDate, DateTime newDate, string doctorName);
    
    /// <summary>
    /// Send appointment reminder email
    /// </summary>
    Task SendAppointmentReminderAsync(string toEmail, string patientName, DateTime appointmentDate, string doctorName);
    
    /// <summary>
    /// Send prescription ready notification
    /// </summary>
    Task SendPrescriptionReadyAsync(string toEmail, string patientName, string pharmacyName);
    
    /// <summary>
    /// Send two-factor authentication code
    /// </summary>
    Task Send2FACodeAsync(string toEmail, string code);
    
    /// <summary>
    /// Send generic email
    /// </summary>
    Task SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextBody = null);
}
