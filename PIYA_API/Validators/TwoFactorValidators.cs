using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for enabling 2FA.</summary>
public class EnableTwoFactorRequestValidator : AbstractValidator<EnableTwoFactorRequest>
{
    public EnableTwoFactorRequestValidator()
    {
        RuleFor(x => x.Method)
            .IsInEnum().WithMessage("Invalid 2FA method.");
    }
}

/// <summary>Validator for verifying a 2FA code.</summary>
public class VerifyCodeRequestValidator : AbstractValidator<VerifyCodeRequest>
{
    public VerifyCodeRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.")
            .Matches(@"^\d{6}$").WithMessage("Code must be a 6-digit number.");
    }
}

/// <summary>Validator for verifying a backup code.</summary>
public class VerifyBackupCodeRequestValidator : AbstractValidator<VerifyBackupCodeRequest>
{
    public VerifyBackupCodeRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.BackupCode)
            .NotEmpty().WithMessage("Backup code is required.");
    }
}

/// <summary>Validator for sending a 2FA code.</summary>
public class SendCodeRequestValidator : AbstractValidator<SendCodeRequest>
{
    public SendCodeRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");
    }
}
