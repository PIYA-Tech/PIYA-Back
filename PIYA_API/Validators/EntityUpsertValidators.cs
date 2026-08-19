using FluentValidation;
using PIYA_API.DTOs;

namespace PIYA_API.Validators;

/// <summary>Validator for hospital create/update requests.</summary>
public class HospitalUpsertDtoValidator : AbstractValidator<HospitalUpsertDto>
{
    public HospitalUpsertDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Hospital name is required.")
            .MaximumLength(200).WithMessage("Hospital name must not exceed 200 characters.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(500).WithMessage("Address must not exceed 500 characters.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100).WithMessage("City must not exceed 100 characters.");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(100).WithMessage("Country must not exceed 100 characters.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[0-9\s\-()]{6,20}$").WithMessage("Invalid phone number format.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Website)
            .MaximumLength(300).WithMessage("Website must not exceed 300 characters.")
            .When(x => x.Website != null);

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(100).WithMessage("Emergency contact must not exceed 100 characters.")
            .When(x => x.EmergencyContact != null);

        RuleFor(x => x.Coordinates!.Lat)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Coordinates != null);

        RuleFor(x => x.Coordinates!.Lng)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Coordinates != null);

        RuleFor(x => x.OperatingHours)
            .MaximumLength(500).WithMessage("Operating hours must not exceed 500 characters.")
            .When(x => x.OperatingHours != null);

        RuleFor(x => x.Departments)
            .Must(d => d!.Count <= 50).WithMessage("Departments list cannot exceed 50 entries.")
            .When(x => x.Departments != null);
    }
}

/// <summary>Validator for pharmacy create/update requests.</summary>
public class PharmacyUpsertDtoValidator : AbstractValidator<PharmacyUpsertDto>
{
    public PharmacyUpsertDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Pharmacy name is required.")
            .MaximumLength(200).WithMessage("Pharmacy name must not exceed 200 characters.");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required.")
            .MaximumLength(100).WithMessage("Country must not exceed 100 characters.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(500).WithMessage("Address must not exceed 500 characters.");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("City must not exceed 100 characters.")
            .When(x => x.City != null);

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[0-9\s\-()]{6,20}$").WithMessage("Invalid phone number format.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Website)
            .MaximumLength(300).WithMessage("Website must not exceed 300 characters.")
            .When(x => x.Website != null);

        RuleFor(x => x.EmergencyContact)
            .MaximumLength(100).WithMessage("Emergency contact must not exceed 100 characters.")
            .When(x => x.EmergencyContact != null);

        RuleFor(x => x.Coordinates!.Lat)
            .InclusiveBetween(-90, 90).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Coordinates != null);

        RuleFor(x => x.Coordinates!.Lng)
            .InclusiveBetween(-180, 180).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Coordinates != null);

        RuleFor(x => x.OperatingHours)
            .MaximumLength(500).WithMessage("Operating hours must not exceed 500 characters.")
            .When(x => x.OperatingHours != null);

        RuleFor(x => x.Services)
            .Must(s => s!.Count <= 30).WithMessage("Services list cannot exceed 30 entries.")
            .When(x => x.Services != null);
    }
}

/// <summary>Validator for the medication master-data write contract.</summary>
public class MedicationUpsertDtoValidator : AbstractValidator<MedicationUpsertDto>
{
    public MedicationUpsertDtoValidator()
    {
        RuleFor(x => x.BrandName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GenericName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Form).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Strength).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Manufacturer).MaximumLength(200).When(x => x.Manufacturer != null);
        RuleFor(x => x.AtcCode).MaximumLength(20).When(x => x.AtcCode != null);
        RuleFor(x => x.Barcode).MaximumLength(100).When(x => x.Barcode != null);
        RuleFor(x => x.ActiveIngredients).Must(values => values.Count <= 50)
            .WithMessage("No more than 50 active ingredients are allowed.");
        RuleForEach(x => x.ActiveIngredients).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GenericAlternatives).Must(values => values.Count <= 100)
            .WithMessage("No more than 100 generic alternatives are allowed.");
    }
}

/// <summary>Validator for assigning hospitals to a doctor.</summary>
public class AssignHospitalsRequestValidator : AbstractValidator<AssignHospitalsRequest>
{
    public AssignHospitalsRequestValidator()
    {
        RuleFor(x => x.HospitalIds)
            .NotNull().WithMessage("HospitalIds list is required (pass empty array to clear).")
            .Must(ids => ids.Count <= 20).WithMessage("Cannot assign more than 20 hospitals.");
    }
}
