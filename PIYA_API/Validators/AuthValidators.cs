using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for 2FA completion requests.</summary>
public class Complete2FARequestValidator : AbstractValidator<Complete2FARequest>
{
    public Complete2FARequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("2FA code is required.")
            .Matches(@"^\d{6}$").WithMessage("Code must be a 6-digit number.");

        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithMessage("ChallengeToken is required.");
    }
}

/// <summary>Validator for 2FA backup code completion.</summary>
public class Complete2FABackupRequestValidator : AbstractValidator<Complete2FABackupRequest>
{
    public Complete2FABackupRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.BackupCode)
            .NotEmpty().WithMessage("Backup code is required.");

        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithMessage("ChallengeToken is required.");
    }
}

/// <summary>Validator for token validation requests.</summary>
public class ValidateTokenRequestValidator : AbstractValidator<ValidateTokenRequest>
{
    public ValidateTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.");
    }
}
