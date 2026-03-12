using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>
/// Validator for user registration
/// </summary>
public class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters")
            .MaximumLength(50).WithMessage("Username must not exceed 50 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number")
            .Matches(@"[!@#$%^&*(),.?""':{}|<>]").WithMessage("Password must contain at least one special character");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(50).WithMessage("First name must not exceed 50 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(50).WithMessage("Last name must not exceed 50 characters");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Invalid phone number format");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required")
            .LessThan(DateTime.Now.AddYears(-18)).WithMessage("You must be at least 18 years old");
    }
}

public record RegisterUserRequest(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string PhoneNumber,
    DateTime DateOfBirth,
    string Role);

/// <summary>
/// Validator for the controller's RegisterRequest DTO (accepts dateOfBirth as string and optional username)
///</summary>
public class RegisterRequestValidator : AbstractValidator<PIYA_API.Controllers.RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username)
            .Cascade(CascadeMode.Stop)
            .MinimumLength(3).When(x => !string.IsNullOrWhiteSpace(x.Username)).WithMessage("Username must be at least 3 characters")
            .MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.Username)).WithMessage("Username must not exceed 50 characters");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one number")
            .Matches(@"[!@#$%^&*(),.?""':{}|<>]").WithMessage("Password must contain at least one special character");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(50).WithMessage("First name must not exceed 50 characters");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(50).WithMessage("Last name must not exceed 50 characters");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .Matches(@"^\+?[1-9]\d{1,14}$").WithMessage("Invalid phone number format");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required")
            .Must(BeAValidDate).WithMessage("Date of birth must be a valid date in yyyy-MM-dd or similar format")
            .Must(BeAtLeast18).WithMessage("You must be at least 18 years old");
    }

    private bool BeAValidDate(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString)) return false;
        return DateTime.TryParse(dateString, out _);
    }

    private bool BeAtLeast18(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString)) return false;
        if (!DateTime.TryParse(dateString, out var dob)) return false;
        return dob <= DateTime.UtcNow.AddYears(-18);
    }
}

/// <summary>
/// Validator for booking a new appointment.
/// </summary>
public class AppointmentRequestValidator : AbstractValidator<AppointmentRequest>
{
    public AppointmentRequestValidator()
    {
        RuleFor(x => x.DoctorId)
            .NotEmpty().WithMessage("DoctorId is required.");

        RuleFor(x => x.HospitalId)
            .NotEmpty().WithMessage("HospitalId is required.");

        RuleFor(x => x.ScheduledAt)
            .NotEmpty().WithMessage("ScheduledAt is required.")
            .GreaterThan(DateTime.UtcNow).WithMessage("Appointment must be scheduled in the future.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason for the appointment is required.")
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
    }
}

/// <summary>
/// Validator for individual prescription line items.
/// </summary>
public class CreatePrescriptionItemDtoValidator : AbstractValidator<CreatePrescriptionItemDto>
{
    public CreatePrescriptionItemDtoValidator()
    {
        RuleFor(x => x.MedicationId)
            .NotEmpty().WithMessage("MedicationId is required for each prescription item.");

        RuleFor(x => x.Dosage)
            .NotEmpty().WithMessage("Dosage is required.")
            .MaximumLength(100).WithMessage("Dosage must not exceed 100 characters.");

        RuleFor(x => x.Frequency)
            .NotEmpty().WithMessage("Frequency is required.")
            .MaximumLength(100).WithMessage("Frequency must not exceed 100 characters.");

        RuleFor(x => x.Duration)
            .NotEmpty().WithMessage("Duration is required.")
            .MaximumLength(100).WithMessage("Duration must not exceed 100 characters.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than zero.")
            .LessThanOrEqualTo(1000).WithMessage("Quantity cannot exceed 1000.");
    }
}

/// <summary>
/// Validator for creating a new prescription.
/// </summary>
public class CreatePrescriptionDtoValidator : AbstractValidator<CreatePrescriptionDto>
{
    public CreatePrescriptionDtoValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.Diagnosis)
            .MaximumLength(1000).WithMessage("Diagnosis must not exceed 1000 characters.")
            .When(x => x.Diagnosis != null);

        RuleFor(x => x.Instructions)
            .MaximumLength(2000).WithMessage("Instructions must not exceed 2000 characters.")
            .When(x => x.Instructions != null);

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTime.UtcNow).WithMessage("ExpiresAt must be in the future.")
            .When(x => x.ExpiresAt != default);

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one prescription item is required.")
            .Must(items => items!.Count <= 20).WithMessage("A prescription may not contain more than 20 items.");

        RuleForEach(x => x.Items).SetValidator(new CreatePrescriptionItemDtoValidator());
    }
}

/// <summary>
/// Validator for creating a new doctor note / medical certificate.
/// </summary>
public class CreateDoctorNoteRequestValidator : AbstractValidator<CreateDoctorNoteRequest>
{
    public CreateDoctorNoteRequestValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.Summary)
            .MaximumLength(2000).WithMessage("Summary must not exceed 2000 characters.")
            .When(x => x.Summary != null);

        RuleFor(x => x.ClinicName)
            .MaximumLength(200).WithMessage("ClinicName must not exceed 200 characters.")
            .When(x => x.ClinicName != null);

        RuleFor(x => x.ValidFrom)
            .NotEmpty().WithMessage("ValidFrom is required.");

        RuleFor(x => x.ValidTo)
            .NotEmpty().WithMessage("ValidTo is required.")
            .GreaterThan(x => x.ValidFrom).WithMessage("ValidTo must be after ValidFrom.")
            .GreaterThan(DateTime.UtcNow).WithMessage("ValidTo cannot be in the past.");
    }
}
