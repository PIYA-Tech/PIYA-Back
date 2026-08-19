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

        RuleFor(x => x.CurrentCode)
            .Matches(@"^\d{6}$").When(x =>
                !string.IsNullOrWhiteSpace(x.CurrentCode) && !x.CurrentCodeIsBackup)
            .WithMessage("Current 2FA code must be a 6-digit number.");

        RuleFor(x => x.CurrentCode)
            .Matches(@"^\d{8}$").When(x =>
                !string.IsNullOrWhiteSpace(x.CurrentCode) && x.CurrentCodeIsBackup)
            .WithMessage("Current backup code must be an 8-digit number.");
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
            .NotEmpty().WithMessage("Backup code is required.")
            .Matches(@"^\d{8}$").WithMessage("Backup code must be an 8-digit number.");

        RuleFor(x => x.ChallengeToken)
            .MaximumLength(512).WithMessage("Challenge token is too long.")
            .When(x => x.ChallengeToken != null);
    }
}

/// <summary>Validator for sending a 2FA code.</summary>
public class SendCodeRequestValidator : AbstractValidator<SendCodeRequest>
{
    public SendCodeRequestValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.ChallengeToken)
            .MaximumLength(512).WithMessage("Challenge token is too long.")
            .When(x => x.ChallengeToken != null);
    }
}

/// <summary>Validator for sensitive 2FA settings changes.</summary>
public class StepUpTwoFactorRequestValidator : AbstractValidator<StepUpTwoFactorRequest>
{
    public StepUpTwoFactorRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("A current 2FA or backup code is required.")
            .Matches(@"^\d{6}$").When(x => !x.IsBackupCode)
            .WithMessage("2FA code must be a 6-digit number.");

        RuleFor(x => x.Code)
            .Matches(@"^\d{8}$").When(x => x.IsBackupCode)
            .WithMessage("Backup code must be an 8-digit number.");
    }
}
