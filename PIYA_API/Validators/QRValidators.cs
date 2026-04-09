using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for QR code scan requests.</summary>
public class ScanQRRequestValidator : AbstractValidator<ScanQRRequest>
{
    public ScanQRRequestValidator()
    {
        RuleFor(x => x.QrToken)
            .NotEmpty().WithMessage("QR token is required.");
    }
}

/// <summary>Validator for QR code revocation requests.</summary>
public class RevokeQRRequestValidator : AbstractValidator<RevokeQRRequest>
{
    public RevokeQRRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Revocation reason is required.")
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
    }
}

/// <summary>Validator for QR code validation requests.</summary>
public class ValidateQRRequestValidator : AbstractValidator<ValidateQRRequest>
{
    public ValidateQRRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.");
    }
}

/// <summary>Validator for QR-based prescription validation.</summary>
public class ValidateQrRequestValidator : AbstractValidator<ValidateQrRequest>
{
    public ValidateQrRequestValidator()
    {
        RuleFor(x => x.QrToken)
            .NotEmpty().WithMessage("QR token is required.");
    }
}

/// <summary>Validator for prescription fulfillment.</summary>
public class FulfillPrescriptionRequestValidator : AbstractValidator<FulfillPrescriptionRequest>
{
    public FulfillPrescriptionRequestValidator()
    {
        RuleFor(x => x.PharmacyId)
            .NotEmpty().WithMessage("PharmacyId is required.");
    }
}
