using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace PIYA_API.Middleware;

/// <summary>
/// This reserved identity is a production conference sandbox, not a licensed clinician.
/// Keep the ID synchronized with conference-doctor.sql. Identity is deliberately not
/// derived from an editable username, profile, client header, or configuration flag.
/// Every new endpoint is denied until explicitly reviewed for this sandbox.
/// </summary>
public static class ConferenceDoctorScope
{
    public static readonly Guid DoctorId = Guid.Parse("b3456a28-a834-46cc-9bc3-59a70569bead");
    public static readonly Guid PatientId = Guid.Parse("178934fc-4829-4b1b-b008-f21d68e2de6c");
    public static bool IsDoctor(Guid id) => id == DoctorId;
    public static bool CanAccessPatient(Guid doctorId, Guid patientId) =>
        !IsDoctor(doctorId) || patientId == PatientId;

    public static bool Allows(string controller, string action, string method) =>
        (controller, action, method) switch
        {
            ("Auth", "Me", "GET") => true,
            ("Auth", "Logout", "POST") => true,
            ("Auth", "RefreshToken", "POST") => true,
            ("EmergencyAccess", "RequestAccess", "POST") => true,
            ("EmergencyAccess", "GetGrant", "GET") => true,
            ("ClinicalCases", "Facilities" or "List" or "Get", "GET") => true,
            ("ClinicalCases", "Admit" or "AddEvent", "POST") => true,
            ("Medication", "GetAll" or "Search" or "GetById", "GET") => true,
            ("DoctorDashboard", "GetMyProfile" or "GetMyAppointments" or
                "GetMyPrescriptions" or "GetPatientRecords" or "GetPrescription", "GET") => true,
            ("DoctorDashboard", "StartAppointment" or "CompleteAppointment" or
                "CancelAppointment" or "CreatePrescription", "POST") => true,
            _ => false
        };
}

public sealed class ConferenceDoctorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
            ConferenceDoctorScope.IsDoctor(id))
        {
            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null || !ConferenceDoctorScope.Allows(
                    action.ControllerName, action.ActionName, context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Conference demo account: this action is unavailable. Only the fictional conference patient's care workflow is allowed."
                });
                return;
            }
        }
        await next(context);
    }
}
