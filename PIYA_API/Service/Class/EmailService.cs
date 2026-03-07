using PIYA_API.Service.Interface;
using System.Net;
using System.Net.Mail;

namespace PIYA_API.Service.Class;

/// <summary>
/// Email service implementation using SMTP
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly bool _isEnabled;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string? _replyToEmail;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly bool _enableSsl;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Load SMTP settings from configuration — canonical path: ExternalApis:EmailService:*
        // Env-var override (Docker/Linux): PIYA__ExternalApis__EmailService__SmtpPassword etc.
        _isEnabled    = configuration.GetValue<bool>("ExternalApis:EmailService:Enabled");
        _smtpHost     = _configuration["ExternalApis:EmailService:SmtpHost"]     ?? "smtp.gmail.com";
        _smtpPort     = int.Parse(_configuration["ExternalApis:EmailService:SmtpPort"] ?? "587");
        _fromEmail    = _configuration["ExternalApis:EmailService:FromEmail"]     ?? "noreply@piya.health";
        _fromName     = _configuration["ExternalApis:EmailService:FromName"]      ?? "PIYA Health";
        _replyToEmail = _configuration["ExternalApis:EmailService:ReplyToEmail"];
        _smtpUsername = _configuration["ExternalApis:EmailService:SmtpUsername"]  ?? "";
        _smtpPassword = _configuration["ExternalApis:EmailService:SmtpPassword"]  ?? "";
        _enableSsl    = bool.Parse(_configuration["ExternalApis:EmailService:EnableSsl"] ?? "true");
    }

    public async Task SendEmailVerificationAsync(string toEmail, string userName, string verificationToken, string verificationUrl)
    {
        var subject = "Verify Your PIYA Account";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #2c3e50;'>Welcome to PIYA Health, {userName}!</h2>
                    <p>Thank you for registering. Please verify your email address to activate your account.</p>
                    <div style='margin: 30px 0;'>
                        <a href='{verificationUrl}' 
                           style='background-color: #3498db; color: white; padding: 12px 24px; 
                                  text-decoration: none; border-radius: 4px; display: inline-block;'>
                            Verify Email Address
                        </a>
                    </div>
                    <p style='color: #7f8c8d; font-size: 14px;'>
                        Or copy and paste this link into your browser:<br>
                        <a href='{verificationUrl}'>{verificationUrl}</a>
                    </p>
                    <p style='color: #7f8c8d; font-size: 12px;'>
                        This link will expire in 24 hours. If you didn't create this account, please ignore this email.
                    </p>
                    <hr style='border: 1px solid #ecf0f1; margin: 30px 0;'>
                    <p style='color: #95a5a6; font-size: 12px;'>
                        PIYA Health - Digital Healthcare Platform<br>
                        Azerbaijan
                    </p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task SendPasswordResetAsync(string toEmail, string userName, string resetToken, string resetUrl)
    {
        var subject = "Reset Your PIYA Password";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #2c3e50;'>Password Reset Request</h2>
                    <p>Hello {userName},</p>
                    <p>We received a request to reset your PIYA account password.</p>
                    <div style='margin: 30px 0;'>
                        <a href='{resetUrl}' 
                           style='background-color: #e74c3c; color: white; padding: 12px 24px; 
                                  text-decoration: none; border-radius: 4px; display: inline-block;'>
                            Reset Password
                        </a>
                    </div>
                    <p style='color: #7f8c8d; font-size: 14px;'>
                        Or copy and paste this link into your browser:<br>
                        <a href='{resetUrl}'>{resetUrl}</a>
                    </p>
                    <p style='color: #e74c3c; font-size: 14px; font-weight: bold;'>
                        ⚠️ This link will expire in 1 hour.
                    </p>
                    <p style='color: #7f8c8d; font-size: 12px;'>
                        If you didn't request a password reset, please ignore this email or contact support if you have concerns.
                    </p>
                    <hr style='border: 1px solid #ecf0f1; margin: 30px 0;'>
                    <p style='color: #95a5a6; font-size: 12px;'>
                        PIYA Health - Digital Healthcare Platform<br>
                        Azerbaijan
                    </p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task SendAppointmentConfirmationAsync(string toEmail, string patientName, DateTime appointmentDate, string doctorName, string hospitalName)
    {
        var subject = "Appointment Confirmed - PIYA Health";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #27ae60;'>✓ Appointment Confirmed</h2>
                    <p>Dear {patientName},</p>
                    <p>Your appointment has been successfully scheduled.</p>
                    <div style='background-color: #ecf0f1; padding: 20px; border-radius: 4px; margin: 20px 0;'>
                        <p><strong>Date & Time:</strong> {appointmentDate:dddd, MMMM dd, yyyy 'at' HH:mm}</p>
                        <p><strong>Doctor:</strong> {doctorName}</p>
                        <p><strong>Location:</strong> {hospitalName}</p>
                    </div>
                    <p style='color: #7f8c8d; font-size: 14px;'>
                        Please arrive 10 minutes early. If you need to cancel or reschedule, please do so at least 24 hours in advance.
                    </p>
                    <hr style='border: 1px solid #ecf0f1; margin: 30px 0;'>
                    <p style='color: #95a5a6; font-size: 12px;'>
                        PIYA Health - Digital Healthcare Platform
                    </p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task SendAppointmentReminderAsync(string toEmail, string patientName, DateTime appointmentDate, string doctorName)
    {
        var subject = "Appointment Reminder - Tomorrow";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #f39c12;'>⏰ Appointment Reminder</h2>
                    <p>Dear {patientName},</p>
                    <p>This is a reminder about your upcoming appointment:</p>
                    <div style='background-color: #fff3cd; padding: 20px; border-radius: 4px; margin: 20px 0; border-left: 4px solid #f39c12;'>
                        <p><strong>Tomorrow at {appointmentDate:HH:mm}</strong></p>
                        <p><strong>Doctor:</strong> {doctorName}</p>
                    </div>
                    <p>See you soon!</p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task SendPrescriptionReadyAsync(string toEmail, string patientName, string pharmacyName)
    {
        var subject = "Your Prescription is Ready";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #27ae60;'>✓ Prescription Ready for Pickup</h2>
                    <p>Dear {patientName},</p>
                    <p>Your prescription is now ready for pickup at <strong>{pharmacyName}</strong>.</p>
                    <p>Please bring your QR code and a valid ID when picking up your medication.</p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task Send2FACodeAsync(string toEmail, string code)
    {
        var subject = "Your PIYA Verification Code";
        var htmlBody = $@"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
                    <h2 style='color: #2c3e50;'>Verification Code</h2>
                    <p>Your PIYA verification code is:</p>
                    <div style='background-color: #ecf0f1; padding: 20px; border-radius: 4px; margin: 20px 0; text-align: center;'>
                        <h1 style='color: #2c3e50; font-size: 36px; letter-spacing: 8px; margin: 0;'>{code}</h1>
                    </div>
                    <p style='color: #e74c3c; font-size: 14px;'>
                        This code will expire in 5 minutes.
                    </p>
                    <p style='color: #7f8c8d; font-size: 12px;'>
                        If you didn't request this code, please ignore this email.
                    </p>
                </div>
            </body>
            </html>";

        await SendEmailAsync(toEmail, subject, htmlBody);
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody, string? plainTextBody = null)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("Email service is disabled. Email not sent to {ToEmail} — subject: {Subject}", toEmail, subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_smtpUsername) || _smtpUsername.Contains("REPLACE"))
        {
            _logger.LogError("Email service is enabled but SMTP credentials are not configured. Set ExternalApis__EmailService__SmtpUsername / SmtpPassword env vars.");
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_fromEmail, _fromName),
                Sender = new MailAddress(_fromEmail, _fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            // Explicitly set Sender header to prevent iCloud from injecting the
            // authenticated account address when using a custom-domain alias.
            message.Headers.Set("Sender", _fromEmail);
            // Optional Reply-To (e.g. noreply@piya.life) so replies don't go to the SMTP account
            if (!string.IsNullOrWhiteSpace(_replyToEmail))
                message.ReplyToList.Add(new MailAddress(_replyToEmail));
            
            message.To.Add(toEmail);

            if (!string.IsNullOrEmpty(plainTextBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(plainTextBody, null, "text/plain");
                message.AlternateViews.Add(plainView);
            }

            using var smtpClient = new SmtpClient(_smtpHost, _smtpPort)
            {
                EnableSsl = _enableSsl,
                Credentials = new NetworkCredential(_smtpUsername, _smtpPassword)
            };

            await smtpClient.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
            throw;
        }
    }
}
