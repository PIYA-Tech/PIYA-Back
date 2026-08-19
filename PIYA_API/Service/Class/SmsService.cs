using PIYA_API.Service.Interface;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace PIYA_API.Service.Class;

public class SmsService : ISmsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmsService> _logger;
    private readonly bool _isEnabled;
    private readonly string? _accountSid;
    private readonly string? _authToken;
    private readonly string? _fromPhoneNumber;

    public SmsService(IConfiguration configuration, ILogger<SmsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        _isEnabled = configuration.GetValue<bool>("ExternalApis:SmsService:Enabled");
        _accountSid = configuration["ExternalApis:SmsService:AccountSid"];
        _authToken = configuration["ExternalApis:SmsService:AuthToken"];
        _fromPhoneNumber = configuration["ExternalApis:SmsService:FromPhoneNumber"];

        if (_isEnabled && !string.IsNullOrEmpty(_accountSid) && !string.IsNullOrEmpty(_authToken))
        {
            TwilioClient.Init(_accountSid, _authToken);
        }
    }

    public async Task<bool> SendSmsAsync(string toPhoneNumber, string message)
    {
        if (!_isEnabled)
        {
            _logger.LogWarning("SMS service is disabled; message was not sent");
            return false;
        }

        if (string.IsNullOrEmpty(_fromPhoneNumber))
        {
            _logger.LogError("FROM phone number not configured");
            return false;
        }

        try
        {
            var messageResource = await MessageResource.CreateAsync(
                body: message,
                from: new PhoneNumber(_fromPhoneNumber),
                to: new PhoneNumber(toPhoneNumber)
            );

            _logger.LogInformation("SMS sent successfully. SID: {MessageSid}, Status: {Status}", 
                messageResource.Sid, messageResource.Status);

            return messageResource.Status != MessageResource.StatusEnum.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "SMS delivery failed with {ExceptionType}",
                ex.GetType().Name);
            return false;
        }
    }

    public async Task<bool> SendVerificationCodeAsync(string toPhoneNumber, string code)
    {
        var message = $"PIYA Healthcare: Your verification code is {code}. Valid for 15 minutes. Do not share this code.";
        return await SendSmsAsync(toPhoneNumber, message);
    }

    public async Task<bool> SendAppointmentReminderAsync(string toPhoneNumber, DateTime appointmentTime, 
        string doctorName, string hospitalName)
    {
        var message = $"PIYA Healthcare: Reminder - You have an appointment with Dr. {doctorName} at {hospitalName} " +
                     $"on {appointmentTime:MMM dd, yyyy} at {appointmentTime:HH:mm}. Reply CONFIRM to confirm.";
        return await SendSmsAsync(toPhoneNumber, message);
    }

    public async Task<bool> SendPrescriptionReadyAsync(string toPhoneNumber, string pharmacyName, string pharmacyAddress)
    {
        var message = $"PIYA Healthcare: Your prescription is ready for pickup at {pharmacyName}, {pharmacyAddress}. " +
                     $"Bring your ID and insurance card.";
        return await SendSmsAsync(toPhoneNumber, message);
    }

    public async Task<bool> SendRefillReminderAsync(string toPhoneNumber, string medicationName, DateTime refillDate)
    {
        var message = $"PIYA Healthcare: Reminder - Your {medicationName} prescription refill is due on {refillDate:MMM dd}. " +
                     $"Contact your doctor or pharmacy to schedule a refill.";
        return await SendSmsAsync(toPhoneNumber, message);
    }

    public async Task<bool> Send2FACodeAsync(string toPhoneNumber, string code)
    {
        var message = $"PIYA Healthcare: Your 2FA code is {code}. Valid for 5 minutes. " +
                     $"If you didn't request this, contact support immediately.";
        return await SendSmsAsync(toPhoneNumber, message);
    }

    public async Task<bool> SendPasswordResetCodeAsync(string toPhoneNumber, string code)
    {
        var message = $"PIYA Healthcare: Your password reset code is {code}. Valid for 30 minutes. " +
                     $"If you didn't request this, ignore this message.";
        return await SendSmsAsync(toPhoneNumber, message);
    }
}
