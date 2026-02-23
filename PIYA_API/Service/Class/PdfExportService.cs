using Microsoft.EntityFrameworkCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PdfExportService(PharmacyApiDbContext context, ILogger<PdfExportService> logger) : IPdfExportService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<PdfExportService> _logger = logger;

    public async Task<byte[]> GeneratePrescriptionPdfAsync(Guid prescriptionId)
    {
        var prescription = await _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .Include(p => p.Items).ThenInclude(i => i.Medication)
            .FirstOrDefaultAsync(p => p.Id == prescriptionId) ?? throw new ArgumentException("Prescription not found", nameof(prescriptionId));

        // Get doctor profile
        var doctorProfile = await _context.DoctorProfiles
            .FirstOrDefaultAsync(dp => dp.UserId == prescription.DoctorId);

        var document = new PdfDocument();
        document.Info.Title = "Medical Prescription";
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        
        var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 14, XFontStyle.Bold);
        var regularFont = new XFont("Arial", 11, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 9, XFontStyle.Regular);
        
        double yPos = 40;
        double leftMargin = 40;
        double pageWidth = page.Width - 80;

        // Title
        gfx.DrawString("MEDICAL PRESCRIPTION", titleFont, XBrushes.DarkBlue, 
            new XRect(leftMargin, yPos, pageWidth, 30), XStringFormats.TopCenter);
        yPos += 50;

        // Prescription Info
        DrawSectionHeader(gfx, "Prescription Information", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Prescription #: {prescription.Id.ToString().Substring(0, 8).ToUpper()}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Issue Date: {prescription.IssuedAt:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        gfx.DrawString($"Expires: {prescription.ExpiresAt:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin + 300, yPos);
        yPos += 20;
        gfx.DrawString($"Status: {prescription.Status}", 
            regularFont, prescription.Status == PrescriptionStatus.Active ? XBrushes.Green : XBrushes.Gray, 
            leftMargin, yPos);
        yPos += 30;

        // Patient Information
        DrawSectionHeader(gfx, "Patient Information", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Name: {prescription.Patient.FirstName} {prescription.Patient.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Date of Birth: {prescription.Patient.DateOfBirth:MM/dd/yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 30;

        // Doctor Information
        DrawSectionHeader(gfx, "Prescribing Physician", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Dr. {prescription.Doctor.FirstName} {prescription.Doctor.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        if (doctorProfile != null)
        {
            gfx.DrawString($"License #: {doctorProfile.LicenseNumber}", 
                regularFont, XBrushes.Black, leftMargin, yPos);
            yPos += 20;
            gfx.DrawString($"Specialization: {doctorProfile.Specialization}", 
                regularFont, XBrushes.Black, leftMargin, yPos);
            yPos += 20;
        }
        yPos += 10;

        // Medications
        DrawSectionHeader(gfx, "Medications", headerFont, leftMargin, ref yPos);
        foreach (var item in prescription.Items)
        {
            if (yPos > page.Height - 100)
            {
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                yPos = 40;
            }

            gfx.DrawRectangle(XPens.LightGray, leftMargin, yPos, pageWidth, 2);
            yPos += 10;
            
            gfx.DrawString($"{item.Medication.BrandName} ({item.Medication.GenericName})", 
                new XFont("Arial", 11, XFontStyle.Bold), XBrushes.Black, leftMargin, yPos);
            yPos += 18;
            gfx.DrawString($"Strength: {item.Medication.Strength} | Form: {item.Medication.Form}", 
                regularFont, XBrushes.Black, leftMargin + 10, yPos);
            yPos += 18;
            gfx.DrawString($"Quantity: {item.Quantity} | Dosage: {item.Dosage}", 
                regularFont, XBrushes.Black, leftMargin + 10, yPos);
            yPos += 18;
            gfx.DrawString($"Frequency: {item.Frequency} | Duration: {item.Duration}", 
                regularFont, XBrushes.Black, leftMargin + 10, yPos);
            yPos += 18;
            
            if (!string.IsNullOrEmpty(item.Instructions))
            {
                gfx.DrawString($"Instructions: {item.Instructions}", 
                    new XFont("Arial", 10, XFontStyle.Italic), XBrushes.DarkGray, 
                    leftMargin + 10, yPos);
                yPos += 18;
            }
            yPos += 10;
        }

        // Diagnosis
        if (!string.IsNullOrEmpty(prescription.Diagnosis))
        {
            yPos += 10;
            DrawSectionHeader(gfx, "Diagnosis", headerFont, leftMargin, ref yPos);
            DrawWrappedText(gfx, prescription.Diagnosis, regularFont, leftMargin, ref yPos, pageWidth);
            yPos += 20;
        }

        // Instructions
        if (!string.IsNullOrEmpty(prescription.Instructions))
        {
            yPos += 10;
            DrawSectionHeader(gfx, "General Instructions", headerFont, leftMargin, ref yPos);
            DrawWrappedText(gfx, prescription.Instructions, regularFont, leftMargin, ref yPos, pageWidth);
        }

        // Footer
        gfx.DrawString($"Generated on: {DateTime.Now:MMMM dd, yyyy HH:mm} | PIYA Healthcare Platform", 
            smallFont, XBrushes.Gray, new XRect(leftMargin, page.Height - 30, pageWidth, 20), 
            XStringFormats.Center);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    public async Task<byte[]> GenerateDoctorNotePdfAsync(Guid doctorNoteId)
    {
        var doctorNote = await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .FirstOrDefaultAsync(n => n.Id == doctorNoteId) ?? throw new ArgumentException("Doctor note not found", nameof(doctorNoteId));
        var doctorProfile = await _context.DoctorProfiles
            .FirstOrDefaultAsync(dp => dp.UserId == doctorNote.DoctorId);

        var document = new PdfDocument();
        document.Info.Title = "Medical Certificate";
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        
        var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 14, XFontStyle.Bold);
        var regularFont = new XFont("Arial", 11, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 9, XFontStyle.Regular);
        
        double yPos = 40;
        double leftMargin = 40;
        double pageWidth = page.Width - 80;

        // Title
        gfx.DrawString("MEDICAL CERTIFICATE", titleFont, XBrushes.DarkBlue, 
            new XRect(leftMargin, yPos, pageWidth, 30), XStringFormats.TopCenter);
        yPos += 40;
        gfx.DrawString($"Certificate #: {doctorNote.Id.ToString().Substring(0, 8).ToUpper()}", 
            smallFont, XBrushes.Gray, new XRect(leftMargin, yPos, pageWidth, 20), XStringFormats.TopCenter);
        yPos += 40;

        // Patient Information
        DrawSectionHeader(gfx, "Patient Information", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Name: {doctorNote.Patient.FirstName} {doctorNote.Patient.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Date of Birth: {doctorNote.Patient.DateOfBirth:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 30;

        // Certificate Details
        DrawSectionHeader(gfx, "Certificate Details", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Issue Date: {doctorNote.IssuedAt:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Valid From: {doctorNote.ValidFrom:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Valid To: {doctorNote.ValidTo:MMMM dd, yyyy}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Note Number: {doctorNote.NoteNumber}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 30;

        // Medical Summary
        DrawSectionHeader(gfx, "Medical Summary", headerFont, leftMargin, ref yPos);
        if (doctorNote.IncludeSummaryInPublicView && !string.IsNullOrEmpty(doctorNote.Summary))
        {
            DrawWrappedText(gfx, doctorNote.Summary, regularFont, leftMargin, ref yPos, pageWidth);
        }
        else
        {
            DrawWrappedText(gfx, "This patient has been under medical care. Details are confidential as per patient privacy regulations.", 
                new XFont("Arial", 11, XFontStyle.Italic), leftMargin, ref yPos, pageWidth);
        }
        yPos += 30;

        // Doctor Information
        DrawSectionHeader(gfx, "Certifying Physician", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Dr. {doctorNote.Doctor.FirstName} {doctorNote.Doctor.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        if (doctorProfile != null)
        {
            gfx.DrawString($"License #: {doctorProfile.LicenseNumber}", 
                regularFont, XBrushes.Black, leftMargin, yPos);
            yPos += 20;
            gfx.DrawString($"Specialization: {doctorProfile.Specialization}", 
                regularFont, XBrushes.Black, leftMargin, yPos);
            yPos += 20;
        }
        yPos += 40;

        // Signature Section
        gfx.DrawLine(XPens.Black, leftMargin, yPos, leftMargin + 200, yPos);
        gfx.DrawLine(XPens.Black, leftMargin + 280, yPos, leftMargin + 480, yPos);
        yPos += 5;
        gfx.DrawString("Doctor's Signature", new XFont("Arial", 9, XFontStyle.Italic), 
            XBrushes.Gray, leftMargin, yPos);
        gfx.DrawString("Date", new XFont("Arial", 9, XFontStyle.Italic), 
            XBrushes.Gray, leftMargin + 280, yPos);
        yPos += 40;

        // Verification
        gfx.DrawString($"Note Number: {doctorNote.NoteNumber}", 
            new XFont("Arial", 10, XFontStyle.Bold), XBrushes.DarkGray, 
            new XRect(leftMargin, yPos, pageWidth, 20), XStringFormats.Center);
        yPos += 15;
        gfx.DrawString("This number can be used to verify the authenticity of this document", 
            new XFont("Arial", 8, XFontStyle.Italic), XBrushes.Gray, 
            new XRect(leftMargin, yPos, pageWidth, 20), XStringFormats.Center);

        // Footer
        gfx.DrawString($"Certificate #: {doctorNote.Id.ToString().Substring(0, 8).ToUpper()} | Generated: {DateTime.Now:MMMM dd, yyyy}", 
            smallFont, XBrushes.Gray, new XRect(leftMargin, page.Height - 30, pageWidth, 20), 
            XStringFormats.Center);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    public async Task<byte[]> GenerateAppointmentConfirmationPdfAsync(Guid appointmentId)
    {
        var appointment = await _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .FirstOrDefaultAsync(a => a.Id == appointmentId) ?? throw new ArgumentException("Appointment not found", nameof(appointmentId));
        var doctorProfile = await _context.DoctorProfiles
            .FirstOrDefaultAsync(dp => dp.UserId == appointment.DoctorId);

        var document = new PdfDocument();
        document.Info.Title = "Appointment Confirmation";
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        
        var titleFont = new XFont("Arial", 20, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 14, XFontStyle.Bold);
        var regularFont = new XFont("Arial", 11, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 9, XFontStyle.Regular);
        
        double yPos = 40;
        double leftMargin = 40;
        double pageWidth = page.Width - 80;

        // Title
        gfx.DrawString("APPOINTMENT CONFIRMATION", titleFont, XBrushes.DarkBlue, 
            new XRect(leftMargin, yPos, pageWidth, 30), XStringFormats.TopCenter);
        yPos += 60;

        // Appointment Details (Highlighted Box)
        gfx.DrawRectangle(new XPen(XColors.DarkBlue, 2), XBrushes.LightBlue, leftMargin, yPos, pageWidth, 80);
        yPos += 15;
        gfx.DrawString("Appointment Details", new XFont("Arial", 14, XFontStyle.Bold), 
            XBrushes.DarkBlue, leftMargin + 10, yPos);
        yPos += 25;
        gfx.DrawString($"Date & Time: {appointment.ScheduledAt:MMMM dd, yyyy HH:mm}", 
            new XFont("Arial", 12, XFontStyle.Bold), XBrushes.Black, leftMargin + 10, yPos);
        yPos += 20;
        gfx.DrawString($"Duration: {appointment.DurationMinutes} minutes | Status: {appointment.Status}", 
            regularFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 40;

        // Patient Information
        DrawSectionHeader(gfx, "Patient Information", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Name: {appointment.Patient.FirstName} {appointment.Patient.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Phone: {appointment.Patient.PhoneNumber}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Email: {appointment.Patient.Email}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 30;

        // Doctor Information
        DrawSectionHeader(gfx, "Doctor", headerFont, leftMargin, ref yPos);
        gfx.DrawString($"Dr. {appointment.Doctor.FirstName} {appointment.Doctor.LastName}", 
            regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        if (doctorProfile != null)
        {
            gfx.DrawString($"Specialization: {doctorProfile.Specialization}", 
                regularFont, XBrushes.Black, leftMargin, yPos);
            yPos += 20;
        }
        yPos += 10;

        // Hospital Information
        DrawSectionHeader(gfx, "Location", headerFont, leftMargin, ref yPos);
        gfx.DrawString(appointment.Hospital.Name, new XFont("Arial", 11, XFontStyle.Bold), 
            XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString(appointment.Hospital.Address, regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 20;
        gfx.DrawString($"Phone: {appointment.Hospital.PhoneNumber}", regularFont, XBrushes.Black, leftMargin, yPos);
        yPos += 30;

        // Reason for Visit
        if (!string.IsNullOrEmpty(appointment.Reason))
        {
            DrawSectionHeader(gfx, "Reason for Visit", headerFont, leftMargin, ref yPos);
            DrawWrappedText(gfx, appointment.Reason, regularFont, leftMargin, ref yPos, pageWidth);
            yPos += 20;
        }

        // Important Notes
        yPos += 10;
        gfx.DrawRectangle(new XPen(XColors.Orange, 1), XBrushes.LightYellow, leftMargin, yPos, pageWidth, 90);
        yPos += 15;
        gfx.DrawString("Important Notes", new XFont("Arial", 12, XFontStyle.Bold), 
            XBrushes.DarkOrange, leftMargin + 10, yPos);
        yPos += 20;
        gfx.DrawString("• Please arrive 15 minutes before your scheduled appointment", 
            regularFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 18;
        gfx.DrawString("• Bring a valid ID and insurance card", 
            regularFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 18;
        gfx.DrawString("• If you need to cancel or reschedule, please notify us at least 24 hours in advance", 
            regularFont, XBrushes.Black, leftMargin + 10, yPos);

        // Footer
        gfx.DrawString($"Confirmation #: {appointment.Id.ToString().Substring(0, 8).ToUpper()} | Generated: {DateTime.Now:MMMM dd, yyyy HH:mm}", 
            smallFont, XBrushes.Gray, new XRect(leftMargin, page.Height - 30, pageWidth, 20), 
            XStringFormats.Center);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    public async Task<byte[]> GeneratePatientMedicalSummaryPdfAsync(Guid userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId) ?? throw new ArgumentException("User not found", nameof(userId));
        var appointments = await _context.Appointments
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .Where(a => a.PatientId == userId)
            .OrderByDescending(a => a.ScheduledAt)
            .Take(10)
            .ToListAsync();

        var appointmentDoctorIds = appointments.Select(a => a.DoctorId).Distinct().ToList();
        var appointmentDoctorProfiles = await _context.DoctorProfiles
            .Where(dp => appointmentDoctorIds.Contains(dp.UserId))
            .ToDictionaryAsync(dp => dp.UserId, dp => dp);

        var prescriptions = await _context.Prescriptions
            .Include(p => p.Doctor)
            .Include(p => p.Items).ThenInclude(i => i.Medication)
            .Where(p => p.PatientId == userId)
            .OrderByDescending(p => p.IssuedAt)
            .Take(10)
            .ToListAsync();

        var prescriptionDoctorIds = prescriptions.Select(p => p.DoctorId).Distinct().ToList();
        var prescriptionDoctorProfiles = await _context.DoctorProfiles
            .Where(dp => prescriptionDoctorIds.Contains(dp.UserId))
            .ToDictionaryAsync(dp => dp.UserId, dp => dp);

        var document = new PdfDocument();
        document.Info.Title = "Patient Medical Summary";
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        
        var titleFont = new XFont("Arial", 18, XFontStyle.Bold);
        var headerFont = new XFont("Arial", 12, XFontStyle.Bold);
        var regularFont = new XFont("Arial", 10, XFontStyle.Regular);
        var smallFont = new XFont("Arial", 8, XFontStyle.Regular);
        
        double yPos = 40;
        double leftMargin = 40;
        double pageWidth = page.Width - 80;

        // Title
        gfx.DrawString("PATIENT MEDICAL SUMMARY", titleFont, XBrushes.DarkBlue, 
            new XRect(leftMargin, yPos, pageWidth, 25), XStringFormats.TopCenter);
        yPos += 35;
        gfx.DrawString($"Generated: {DateTime.Now:MMMM dd, yyyy}", smallFont, XBrushes.Gray, 
            new XRect(leftMargin, yPos, pageWidth, 15), XStringFormats.TopCenter);
        yPos += 35;

        // Patient Information
        gfx.DrawRectangle(XPens.LightGray, leftMargin, yPos, pageWidth, 80);
        yPos += 12;
        gfx.DrawString("Patient Information", headerFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 18;
        gfx.DrawString($"Name: {user.FirstName} {user.LastName}", regularFont, XBrushes.Black, leftMargin + 10, yPos);
        gfx.DrawString($"DOB: {user.DateOfBirth:MM/dd/yyyy}", regularFont, XBrushes.Black, leftMargin + 300, yPos);
        yPos += 16;
        gfx.DrawString($"Email: {user.Email}", regularFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 16;
        gfx.DrawString($"Phone: {user.PhoneNumber}", regularFont, XBrushes.Black, leftMargin + 10, yPos);
        yPos += 35;

        // Recent Appointments
        DrawSectionHeader(gfx, "Recent Appointments (Last 10)", headerFont, leftMargin, ref yPos);
        
        if (appointments.Any())
        {
            foreach (var appointment in appointments)
            {
                if (yPos > page.Height - 120)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    yPos = 40;
                }

                var doctorProfile = appointmentDoctorProfiles.GetValueOrDefault(appointment.DoctorId);
                
                gfx.DrawRectangle(XPens.LightGray, leftMargin, yPos, pageWidth, 65);
                yPos += 12;
                gfx.DrawString($"{appointment.ScheduledAt:MMMM dd, yyyy HH:mm}", 
                    new XFont("Arial", 10, XFontStyle.Bold), XBrushes.Black, leftMargin + 5, yPos);
                yPos += 15;
                gfx.DrawString($"Doctor: Dr. {appointment.Doctor.FirstName} {appointment.Doctor.LastName}", 
                    regularFont, XBrushes.Black, leftMargin + 5, yPos);
                yPos += 13;
                if (doctorProfile != null)
                {
                    gfx.DrawString($"Specialization: {doctorProfile.Specialization}", 
                        regularFont, XBrushes.Black, leftMargin + 5, yPos);
                    yPos += 13;
                }
                gfx.DrawString($"Hospital: {appointment.Hospital.Name} | Status: {appointment.Status}", 
                    regularFont, XBrushes.Black, leftMargin + 5, yPos);
                yPos += 20;
            }
        }
        else
        {
            gfx.DrawString("No appointments found", new XFont("Arial", 10, XFontStyle.Italic), 
                XBrushes.Gray, leftMargin, yPos);
            yPos += 20;
        }
        yPos += 15;

        // Check if we need a new page
        if (yPos > page.Height - 200)
        {
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            yPos = 40;
        }

        // Recent Prescriptions
        DrawSectionHeader(gfx, "Recent Prescriptions (Last 10)", headerFont, leftMargin, ref yPos);
        
        if (prescriptions.Any())
        {
            foreach (var prescription in prescriptions)
            {
                if (yPos > page.Height - 140)
                {
                    page = document.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    yPos = 40;
                }

                var doctorProfile = prescriptionDoctorProfiles.GetValueOrDefault(prescription.DoctorId);
                
                var boxHeight = 70 + (prescription.Items.Count * 13);
                gfx.DrawRectangle(XPens.LightGray, leftMargin, yPos, pageWidth, boxHeight);
                yPos += 12;
                gfx.DrawString($"Issued: {prescription.IssuedAt:MMMM dd, yyyy}", 
                    new XFont("Arial", 10, XFontStyle.Bold), XBrushes.Black, leftMargin + 5, yPos);
                yPos += 15;
                gfx.DrawString($"Doctor: Dr. {prescription.Doctor.FirstName} {prescription.Doctor.LastName}", 
                    regularFont, XBrushes.Black, leftMargin + 5, yPos);
                yPos += 13;
                if (doctorProfile != null)
                {
                    gfx.DrawString($"Specialization: {doctorProfile.Specialization}", 
                        regularFont, XBrushes.Black, leftMargin + 5, yPos);
                    yPos += 13;
                }
                gfx.DrawString($"Status: {prescription.Status} | Medications: {prescription.Items.Count}", 
                    regularFont, XBrushes.Black, leftMargin + 5, yPos);
                yPos += 15;
                
                if (prescription.Items.Any())
                {
                    gfx.DrawString("Medications:", new XFont("Arial", 9, XFontStyle.Italic), 
                        XBrushes.DarkGray, leftMargin + 5, yPos);
                    yPos += 13;
                    foreach (var item in prescription.Items)
                    {
                        gfx.DrawString($"• {item.Medication.BrandName} ({item.Dosage}, {item.Frequency})", 
                            regularFont, XBrushes.Black, leftMargin + 15, yPos);
                        yPos += 13;
                    }
                }
                yPos += 10;
            }
        }
        else
        {
            gfx.DrawString("No prescriptions found", new XFont("Arial", 10, XFontStyle.Italic), 
                XBrushes.Gray, leftMargin, yPos);
            yPos += 20;
        }

        // Confidentiality Notice
        yPos += 20;
        if (yPos > page.Height - 80)
        {
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            yPos = 40;
        }
        
        gfx.DrawRectangle(new XPen(XColors.Red, 1), XBrushes.LightPink, leftMargin, yPos, pageWidth, 50);
        yPos += 12;
        gfx.DrawString("CONFIDENTIALITY NOTICE", new XFont("Arial", 11, XFontStyle.Bold), 
            XBrushes.DarkRed, new XRect(leftMargin, yPos, pageWidth, 20), XStringFormats.TopCenter);
        yPos += 20;
        gfx.DrawString("This document contains confidential medical information.", 
            new XFont("Arial", 9, XFontStyle.Italic), XBrushes.Black, 
            new XRect(leftMargin + 10, yPos, pageWidth - 20, 30), XStringFormats.TopCenter);

        // Footer
        gfx.DrawString($"Generated: {DateTime.Now:MMMM dd, yyyy HH:mm} | PIYA Healthcare Platform", 
            smallFont, XBrushes.Gray, new XRect(leftMargin, page.Height - 30, pageWidth, 20), 
            XStringFormats.Center);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    #region Helper Methods

    private static void DrawSectionHeader(XGraphics gfx, string text, XFont font, double x, ref double y)
    {
        gfx.DrawLine(XPens.LightGray, x, y, x + 500, y);
        y += 5;
        gfx.DrawString(text, font, XBrushes.Black, x, y);
        y += 25;
    }

    private static void DrawWrappedText(XGraphics gfx, string text, XFont font, double x, ref double y, double maxWidth)
    {
        var words = text.Split(' ');
        var line = "";
        
        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
            var size = gfx.MeasureString(testLine, font);
            
            if (size.Width > maxWidth && !string.IsNullOrEmpty(line))
            {
                gfx.DrawString(line, font, XBrushes.Black, x, y);
                y += 18;
                line = word;
            }
            else
            {
                line = testLine;
            }
        }
        
        if (!string.IsNullOrEmpty(line))
        {
            gfx.DrawString(line, font, XBrushes.Black, x, y);
            y += 18;
        }
    }

    #endregion
}
