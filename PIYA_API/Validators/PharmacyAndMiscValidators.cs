using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for creating a pharmacy company.</summary>
public class CreatePharmacyCompanyRequestValidator : AbstractValidator<CreatePharmacyCompanyRequest>
{
    public CreatePharmacyCompanyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name must not exceed 200 characters.");
    }
}

/// <summary>Validator for updating a pharmacy company.</summary>
public class UpdatePharmacyCompanyRequestValidator : AbstractValidator<UpdatePharmacyCompanyRequest>
{
    public UpdatePharmacyCompanyRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name must not exceed 200 characters.");
    }
}

/// <summary>Validator for pharmacy search requests.</summary>
public class MultipleMedicationsSearchRequestValidator : AbstractValidator<MultipleMedicationsSearchRequest>
{
    public MultipleMedicationsSearchRequestValidator()
    {
        RuleFor(x => x.MedicationIds)
            .NotEmpty().WithMessage("At least one medication ID is required.")
            .Must(ids => ids.Count <= 50).WithMessage("Cannot search for more than 50 medications at once.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Longitude.HasValue);

        RuleFor(x => x.RadiusKm)
            .InclusiveBetween(1, 500).WithMessage("RadiusKm must be between 1 and 500.")
            .When(x => x.RadiusKm.HasValue);
    }
}

/// <summary>Validator for smart pharmacy search requests.</summary>
public class SmartSearchRequestValidator : AbstractValidator<SmartSearchRequest>
{
    public SmartSearchRequestValidator()
    {
        RuleFor(x => x.MedicationIds)
            .NotEmpty().WithMessage("At least one medication ID is required.")
            .Must(ids => ids.Count <= 50).WithMessage("Cannot search for more than 50 medications at once.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Longitude.HasValue);

        RuleFor(x => x.MaxRadiusKm)
            .InclusiveBetween(1, 500).WithMessage("MaxRadiusKm must be between 1 and 500.")
            .When(x => x.MaxRadiusKm.HasValue);
    }
}

/// <summary>Validator for assigning a pharmacy manager.</summary>
public class AssignManagerRequestValidator : AbstractValidator<AssignManagerRequest>
{
    public AssignManagerRequestValidator()
    {
        RuleFor(x => x.PharmacyId)
            .NotEmpty().WithMessage("PharmacyId is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");
    }
}

/// <summary>Validator for transferring pharmacy management.</summary>
public class TransferManagementRequestValidator : AbstractValidator<TransferManagementRequest>
{
    public TransferManagementRequestValidator()
    {
        RuleFor(x => x.PharmacyId)
            .NotEmpty().WithMessage("PharmacyId is required.");

        RuleFor(x => x.NewManagerUserId)
            .NotEmpty().WithMessage("NewManagerUserId is required.");
    }
}

/// <summary>Validator for manager adding staff.</summary>
public class ManagerAddStaffDtoValidator : AbstractValidator<ManagerAddStaffDto>
{
    public ManagerAddStaffDtoValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid staff role.");
    }
}

/// <summary>Validator for push notification device registration.</summary>
public class RegisterDeviceRequestValidator : AbstractValidator<RegisterDeviceRequest>
{
    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "ios", "android", "web"
    };

    public RegisterDeviceRequestValidator()
    {
        RuleFor(x => x.DeviceToken)
            .NotEmpty().WithMessage("Device token is required.")
            .MaximumLength(500).WithMessage("Device token must not exceed 500 characters.");

        RuleFor(x => x.Platform)
            .NotEmpty().WithMessage("Platform is required.")
            .Must(p => ValidPlatforms.Contains(p)).WithMessage("Platform must be 'ios', 'android', or 'web'.");
    }
}

/// <summary>Validator for unregistering a device.</summary>
public class UnregisterDeviceRequestValidator : AbstractValidator<UnregisterDeviceRequest>
{
    public UnregisterDeviceRequestValidator()
    {
        RuleFor(x => x.DeviceToken)
            .NotEmpty().WithMessage("Device token is required.");
    }
}

/// <summary>Validator for doctor note revocation.</summary>
public class RevokeNoteRequestValidator : AbstractValidator<RevokeNoteRequest>
{
    public RevokeNoteRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(1000).WithMessage("Revocation reason must not exceed 1000 characters.")
            .When(x => x.Reason != null);
    }
}

/// <summary>Validator for email verification.</summary>
public class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Verification token is required.");
    }
}
