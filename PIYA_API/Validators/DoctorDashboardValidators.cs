using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for updating a doctor profile.</summary>
public class UpdateDoctorProfileRequestValidator : AbstractValidator<UpdateDoctorProfileRequest>
{
    public UpdateDoctorProfileRequestValidator()
    {
        RuleFor(x => x.LicenseAuthority)
            .MaximumLength(200).WithMessage("LicenseAuthority must not exceed 200 characters.")
            .When(x => x.LicenseAuthority != null);

        RuleFor(x => x.LicenseExpiryDate)
            .GreaterThan(DateTime.UtcNow).WithMessage("License expiry date must be in the future.")
            .When(x => x.LicenseExpiryDate.HasValue);

        RuleFor(x => x.YearsOfExperience)
            .GreaterThanOrEqualTo(0).WithMessage("Years of experience must be non-negative.")
            .LessThanOrEqualTo(70).WithMessage("Years of experience seems unreasonable.")
            .When(x => x.YearsOfExperience.HasValue);

        RuleFor(x => x.Biography)
            .MaximumLength(2000).WithMessage("Biography must not exceed 2000 characters.")
            .When(x => x.Biography != null);

        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee must be non-negative.")
            .When(x => x.ConsultationFee.HasValue);

        RuleFor(x => x.Education)
            .Must(e => e!.Count <= 20).WithMessage("Education list cannot exceed 20 entries.")
            .When(x => x.Education != null);

        RuleFor(x => x.Certifications)
            .Must(c => c!.Count <= 30).WithMessage("Certifications list cannot exceed 30 entries.")
            .When(x => x.Certifications != null);

        RuleFor(x => x.Languages)
            .Must(l => l!.Count <= 20).WithMessage("Languages list cannot exceed 20 entries.")
            .When(x => x.Languages != null);
    }
}

/// <summary>Validator for completing an appointment.</summary>
public class CompleteAppointmentRequestValidator : AbstractValidator<CompleteAppointmentRequest>
{
    public CompleteAppointmentRequestValidator()
    {
        RuleFor(x => x.Notes)
            .MaximumLength(5000).WithMessage("Notes must not exceed 5000 characters.")
            .When(x => x.Notes != null);
    }
}

/// <summary>Validator for cancelling an appointment.</summary>
public class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
{
    public CancelAppointmentRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(1000).WithMessage("Cancellation reason must not exceed 1000 characters.")
            .When(x => x.Reason != null);
    }
}

/// <summary>Validator for doctor-initiated prescription creation.</summary>
public class CreatePrescriptionRequestValidator : AbstractValidator<CreatePrescriptionRequest>
{
    public CreatePrescriptionRequestValidator()
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
            .When(x => x.ExpiresAt.HasValue);

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one prescription item is required.")
            .Must(items => items!.Count <= 20).WithMessage("A prescription may not contain more than 20 items.");

        RuleForEach(x => x.Items).SetValidator(new PrescriptionItemRequestValidator());
    }
}

/// <summary>Validator for prescription items in doctor dashboard flow.</summary>
public class PrescriptionItemRequestValidator : AbstractValidator<PrescriptionItemRequest>
{
    public PrescriptionItemRequestValidator()
    {
        RuleFor(x => x.MedicationId)
            .NotEmpty().WithMessage("MedicationId is required.");

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

        RuleFor(x => x.Instructions)
            .MaximumLength(500).WithMessage("Instructions must not exceed 500 characters.")
            .When(x => x.Instructions != null);
    }
}

/// <summary>Validator for cancelling a prescription.</summary>
public class CancelPrescriptionRequestValidator : AbstractValidator<CancelPrescriptionRequest>
{
    public CancelPrescriptionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(1000).WithMessage("Cancellation reason must not exceed 1000 characters.")
            .When(x => x.Reason != null);
    }
}
