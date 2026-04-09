using FluentValidation;
using PIYA_API.Controllers;
using PIYA_API.Model;

namespace PIYA_API.Validators;

/// <summary>
/// Validator for login requests.
/// </summary>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // At least one identifier is required (mobile sends Identifier; web may send Email/Username)
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Identifier)
                     || !string.IsNullOrWhiteSpace(x.Email)
                     || !string.IsNullOrWhiteSpace(x.Username))
            .WithMessage("Email or username is required.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Invalid email format.");

        // When the unified Identifier field contains an '@', validate as email
        RuleFor(x => x.Identifier)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Identifier) && x.Identifier.Contains('@'))
            .WithMessage("Invalid email format.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

/// <summary>
/// Validator for pharmacy inventory upsert requests.
/// </summary>
public class PharmacyInventoryRequestValidator : AbstractValidator<PharmacyInventoryRequest>
{
    public PharmacyInventoryRequestValidator()
    {
        RuleFor(x => x.MedicationId)
            .NotEmpty().WithMessage("MedicationId is required.");

        RuleFor(x => x.QuantityInStock)
            .GreaterThanOrEqualTo(0).WithMessage("QuantityInStock must be non-negative.");

        RuleFor(x => x.MinimumStockLevel)
            .GreaterThanOrEqualTo(0).WithMessage("MinimumStockLevel must be non-negative.");

        RuleFor(x => x.ReorderQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("ReorderQuantity must be non-negative.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than zero.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .MaximumLength(3).WithMessage("Currency code must be at most 3 characters.");
    }
}

/// <summary>
/// Validator for stock update (set exact quantity) requests.
/// </summary>
public class UpdateStockRequestValidator : AbstractValidator<UpdateStockRequest>
{
    public UpdateStockRequestValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0).WithMessage("Quantity must be non-negative.");

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes must not exceed 500 characters.")
            .When(x => x.Notes != null);
    }
}

/// <summary>
/// Validator for restocking requests.
/// </summary>
public class RestockRequestValidator : AbstractValidator<RestockRequest>
{
    public RestockRequestValidator()
    {
        RuleFor(x => x.MedicationId)
            .NotEmpty().WithMessage("MedicationId is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Restock quantity must be greater than zero.");

        RuleFor(x => x.ReferenceNumber)
            .MaximumLength(100).WithMessage("ReferenceNumber must not exceed 100 characters.")
            .When(x => x.ReferenceNumber != null);
    }
}

/// <summary>
/// Validator for stock decrease requests (dispensing).
/// </summary>
public class DecreaseStockRequestValidator : AbstractValidator<DecreaseStockRequest>
{
    public DecreaseStockRequestValidator()
    {
        RuleFor(x => x.MedicationId)
            .NotEmpty().WithMessage("MedicationId is required.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Decrease quantity must be greater than zero.")
            .LessThanOrEqualTo(10000).WithMessage("Decrease quantity exceeds reasonable limit.");

        RuleFor(x => x.ReferenceNumber)
            .MaximumLength(500).WithMessage("ReferenceNumber must not exceed 500 characters.")
            .When(x => x.ReferenceNumber != null);
    }
}

/// <summary>
/// Validator for pharmacy staff assignment requests.
/// </summary>
public class AssignStaffRequestValidator : AbstractValidator<AssignStaffRequest>
{
    public AssignStaffRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.");
    }
}

/// <summary>
/// Validator for updating staff role/status.
/// </summary>
public class UpdateStaffRequestValidator : AbstractValidator<UpdateStaffRequest>
{
    public UpdateStaffRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.")
            .When(x => x.Role != null);
    }
}

/// <summary>
/// Validator for reschedule appointment requests.
/// </summary>
public class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(x => x.NewScheduledAt)
            .NotEmpty().WithMessage("NewScheduledAt is required.")
            .GreaterThan(DateTime.UtcNow).WithMessage("New appointment time must be in the future.");
    }
}

/// <summary>
/// Validator for doctor profile creation requests.
/// </summary>
public class CreateDoctorProfileRequestValidator : AbstractValidator<CreateDoctorProfileRequest>
{
    public CreateDoctorProfileRequestValidator()
    {
        RuleFor(x => x.Specialization)
            .IsInEnum().WithMessage("Invalid specialization value.");

        RuleFor(x => x.LicenseNumber)
            .NotEmpty().WithMessage("License number is required.")
            .MaximumLength(50).WithMessage("License number must not exceed 50 characters.");

        RuleFor(x => x.YearsOfExperience)
            .GreaterThanOrEqualTo(0).WithMessage("Years of experience must be non-negative.")
            .LessThanOrEqualTo(70).WithMessage("Years of experience seems unreasonable.");

        RuleFor(x => x.ConsultationFee)
            .GreaterThanOrEqualTo(0).WithMessage("Consultation fee must be non-negative.")
            .When(x => x.ConsultationFee.HasValue);

        RuleFor(x => x.Biography)
            .MaximumLength(2000).WithMessage("Biography must not exceed 2000 characters.")
            .When(x => x.Biography != null);
    }
}

/// <summary>
/// Validator for granting individual permissions.
/// </summary>
public class GrantPermissionRequestValidator : AbstractValidator<GrantPermissionRequest>
{
    public GrantPermissionRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Permission)
            .NotEmpty().WithMessage("Permission is required.")
            .MaximumLength(200).WithMessage("Permission name must not exceed 200 characters.");
    }
}

/// <summary>
/// Validator for inventory batch requests.
/// </summary>
public class BatchRequestValidator : AbstractValidator<BatchRequest>
{
    public BatchRequestValidator()
    {
        RuleFor(x => x.InventoryId)
            .NotEmpty().WithMessage("InventoryId is required.");

        RuleFor(x => x.BatchNumber)
            .NotEmpty().WithMessage("BatchNumber is required.")
            .MaximumLength(100).WithMessage("BatchNumber must not exceed 100 characters.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Batch quantity must be greater than zero.");

        RuleFor(x => x.CostPerUnit)
            .GreaterThanOrEqualTo(0).WithMessage("CostPerUnit must be non-negative.");

        RuleFor(x => x.ExpirationDate)
            .GreaterThan(DateTime.UtcNow).WithMessage("Expiration date must be in the future.")
            .When(x => x.ExpirationDate.HasValue);

        RuleFor(x => x.Supplier)
            .MaximumLength(200).WithMessage("Supplier must not exceed 200 characters.")
            .When(x => x.Supplier != null);

        RuleFor(x => x.StorageLocation)
            .MaximumLength(200).WithMessage("StorageLocation must not exceed 200 characters.")
            .When(x => x.StorageLocation != null);

        RuleFor(x => x.Notes)
            .MaximumLength(500).WithMessage("Notes must not exceed 500 characters.")
            .When(x => x.Notes != null);
    }
}

/// <summary>
/// Validator for granting role-based permissions.
/// </summary>
public class GrantRolePermissionsRequestValidator : AbstractValidator<GrantRolePermissionsRequest>
{
    public GrantRolePermissionsRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid user role.");
    }
}
