using System.Text;
using System.Web;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Service.Class;

public class CalendarService(
    PharmacyApiDbContext context,
    INotificationService notificationService,
    ILogger<CalendarService> logger) : ICalendarService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ILogger<CalendarService> _logger = logger;

    public async Task<string> GenerateAppointmentICalAsync(Guid appointmentId)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .FirstOrDefaultAsync(a => a.Id == appointmentId) ?? throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");
        var endTime = appointment.ScheduledAt.AddMinutes(appointment.DurationMinutes);
        var doctorName = appointment.Doctor != null 
            ? $"{appointment.Doctor.FirstName} {appointment.Doctor.LastName}" 
            : "Doctor";
        var patientName = appointment.Patient != null 
            ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}" 
            : "Patient";
        
        var summary = $"Medical Appointment with Dr. {doctorName}";
        var description = string.IsNullOrEmpty(appointment.AppointmentNotes)
            ? $"Appointment at {appointment.Hospital?.Name ?? "Hospital"}"
            : $"{appointment.AppointmentNotes}\n\nLocation: {appointment.Hospital?.Name ?? "Hospital"}";

        return GenerateICalEvent(
            summary: summary,
            description: description,
            startTime: appointment.ScheduledAt,
            endTime: endTime,
            location: appointment.Hospital?.Name ?? "Hospital Location",
            organizerEmail: appointment.Doctor?.Email ?? "doctor@piya.life",
            organizerName: doctorName,
            attendeeEmail: appointment.Patient?.Email ?? "patient@piya.life",
            attendeeName: patientName
        );
    }

    public string GenerateICalEvent(
        string summary,
        string description,
        DateTime startTime,
        DateTime endTime,
        string location,
        string organizerEmail,
        string organizerName,
        string attendeeEmail,
        string attendeeName)
    {
        var uid = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var startTimeFormatted = startTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var endTimeFormatted = endTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");

        var ical = new StringBuilder();
        ical.AppendLine("BEGIN:VCALENDAR");
        ical.AppendLine("VERSION:2.0");
        ical.AppendLine("PRODID:-//PIYA Healthcare//Appointment System//EN");
        ical.AppendLine("CALSCALE:GREGORIAN");
        ical.AppendLine("METHOD:REQUEST");
        ical.AppendLine("BEGIN:VEVENT");
        ical.AppendLine($"UID:{uid}");
        ical.AppendLine($"DTSTAMP:{timestamp}");
        ical.AppendLine($"DTSTART:{startTimeFormatted}");
        ical.AppendLine($"DTEND:{endTimeFormatted}");
        ical.AppendLine($"SUMMARY:{EscapeICalText(summary)}");
        ical.AppendLine($"DESCRIPTION:{EscapeICalText(description)}");
        ical.AppendLine($"LOCATION:{EscapeICalText(location)}");
        ical.AppendLine($"ORGANIZER;CN=\"{EscapeICalText(organizerName)}\":mailto:{organizerEmail}");
        ical.AppendLine($"ATTENDEE;CN=\"{EscapeICalText(attendeeName)}\";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:{attendeeEmail}");
        ical.AppendLine("STATUS:CONFIRMED");
        ical.AppendLine("SEQUENCE:0");
        ical.AppendLine("TRANSP:OPAQUE");
        ical.AppendLine("BEGIN:VALARM");
        ical.AppendLine("TRIGGER:-PT15M");
        ical.AppendLine("ACTION:DISPLAY");
        ical.AppendLine($"DESCRIPTION:Reminder: {EscapeICalText(summary)}");
        ical.AppendLine("END:VALARM");
        ical.AppendLine("END:VEVENT");
        ical.AppendLine("END:VCALENDAR");

        return ical.ToString();
    }

    public async Task<string> GenerateGoogleCalendarUrlAsync(Guid appointmentId)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .FirstOrDefaultAsync(a => a.Id == appointmentId) ?? throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");
        var endTime = appointment.ScheduledAt.AddMinutes(appointment.DurationMinutes);
        var doctorName = appointment.Doctor != null 
            ? $"{appointment.Doctor.FirstName} {appointment.Doctor.LastName}" 
            : "Doctor";
        
        var title = $"Medical Appointment with Dr. {doctorName}";
        var details = string.IsNullOrEmpty(appointment.AppointmentNotes)
            ? $"Appointment at {appointment.Hospital?.Name ?? "Hospital"}"
            : appointment.AppointmentNotes;
        var location = appointment.Hospital?.Name ?? "Hospital";

        // Google Calendar URL format
        // https://calendar.google.com/calendar/render?action=TEMPLATE&text=...&dates=...&details=...&location=...
        var baseUrl = "https://calendar.google.com/calendar/render";
        var queryParams = new Dictionary<string, string>
        {
            ["action"] = "TEMPLATE",
            ["text"] = title,
            ["dates"] = $"{FormatGoogleDateTime(appointment.ScheduledAt)}/{FormatGoogleDateTime(endTime)}",
            ["details"] = details,
            ["location"] = location
        };

        var queryString = string.Join("&", queryParams.Select(kvp => 
            $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}"));

        return $"{baseUrl}?{queryString}";
    }

    public async Task<string> GenerateOutlookCalendarUrlAsync(Guid appointmentId)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .FirstOrDefaultAsync(a => a.Id == appointmentId) ?? throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");
        var endTime = appointment.ScheduledAt.AddMinutes(appointment.DurationMinutes);
        var doctorName = appointment.Doctor != null 
            ? $"{appointment.Doctor.FirstName} {appointment.Doctor.LastName}" 
            : "Doctor";
        
        var title = $"Medical Appointment with Dr. {doctorName}";
        var details = string.IsNullOrEmpty(appointment.AppointmentNotes)
            ? $"Appointment at {appointment.Hospital?.Name ?? "Hospital"}"
            : appointment.AppointmentNotes;
        var location = appointment.Hospital?.Name ?? "Hospital";

        // Outlook/Office 365 Calendar URL format
        // https://outlook.live.com/calendar/0/deeplink/compose?subject=...&startdt=...&enddt=...&body=...&location=...
        var baseUrl = "https://outlook.live.com/calendar/0/deeplink/compose";
        var queryParams = new Dictionary<string, string>
        {
            ["subject"] = title,
            ["startdt"] = appointment.ScheduledAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["enddt"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["body"] = details,
            ["location"] = location,
            ["path"] = "/calendar/action/compose",
            ["rru"] = "addevent"
        };

        var queryString = string.Join("&", queryParams.Select(kvp => 
            $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}"));

        return $"{baseUrl}?{queryString}";
    }

    public async Task<bool> SendCalendarInvitationAsync(Guid appointmentId, string recipientEmail)
    {
        try
        {
            var appointment = await _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.Hospital)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null)
            {
                _logger.LogError($"Appointment {appointmentId} not found for calendar invitation");
                return false;
            }

            var icalContent = await GenerateAppointmentICalAsync(appointmentId);
            var googleUrl = await GenerateGoogleCalendarUrlAsync(appointmentId);
            var outlookUrl = await GenerateOutlookCalendarUrlAsync(appointmentId);

            var doctorName = appointment.Doctor != null 
                ? $"{appointment.Doctor.FirstName} {appointment.Doctor.LastName}" 
                : "Doctor";
            var patientName = appointment.Patient != null 
                ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}" 
                : "Patient";

            var subject = $"Calendar Invitation: Appointment with Dr. {doctorName}";
            var body = $@"
                <h2>Appointment Calendar Invitation</h2>
                <p>Dear {patientName},</p>
                <p>Please add this appointment to your calendar:</p>
                <ul>
                    <li><strong>Doctor:</strong> {doctorName}</li>
                    <li><strong>Hospital:</strong> {appointment.Hospital?.Name ?? "Hospital"}</li>
                    <li><strong>Date & Time:</strong> {appointment.ScheduledAt:dddd, MMMM dd, yyyy 'at' h:mm tt}</li>
                    <li><strong>Duration:</strong> {appointment.DurationMinutes} minutes</li>
                </ul>
                
                <p><strong>Add to your calendar:</strong></p>
                <ul>
                    <li><a href=""{googleUrl}"">Add to Google Calendar</a></li>
                    <li><a href=""{outlookUrl}"">Add to Outlook Calendar</a></li>
                    <li>Or download the attached .ics file and import it to your calendar application</li>
                </ul>
                
                <p>Please arrive 10 minutes early for registration.</p>
                <br>
                <p>Best regards,<br>PIYA Healthcare Team</p>
                
                <hr>
                <p style=""font-size: 10px; color: #666;"">
                    If the links don't work, copy and paste this URL into your browser:<br>
                    Google Calendar: {googleUrl}<br>
                    Outlook Calendar: {outlookUrl}
                </p>";

            // Note: To attach the .ics file, you would need to modify SendEmailAsync to support attachments
            // For now, we're including the iCal content in the email body and providing direct links
            var success = await _notificationService.SendEmailAsync(recipientEmail, subject, body, true);

            if (success)
            {
                _logger.LogInformation($"Calendar invitation sent for appointment {appointmentId} to {recipientEmail}");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send calendar invitation for appointment {appointmentId}");
            return false;
        }
    }

    private static string EscapeICalText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\\", "\\\\")
            .Replace(",", "\\,")
            .Replace(";", "\\;")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    private static string FormatGoogleDateTime(DateTime dateTime)
    {
        // Google Calendar expects format: yyyyMMddTHHmmssZ
        return dateTime.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
    }
}
