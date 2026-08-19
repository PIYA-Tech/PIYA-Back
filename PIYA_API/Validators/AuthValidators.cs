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
            .NotEmpty().WithMessage("ChallengeToken is required.")
            .MaximumLength(512).WithMessage("ChallengeToken is too long.");
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
            .NotEmpty().WithMessage("Backup code is required.")
            .Matches(@"^\d{8}$").WithMessage("Backup code must be an 8-digit number.");

        RuleFor(x => x.ChallengeToken)
            .NotEmpty().WithMessage("ChallengeToken is required.")
            .MaximumLength(512).WithMessage("ChallengeToken is too long.");
    }
}

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .MaximumLength(1024).WithMessage("Refresh token is too long.")
            .When(x => x.RefreshToken != null);
    }
}

public class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .MaximumLength(1024).WithMessage("Refresh token is too long.")
            .When(x => x.RefreshToken != null);

        RuleFor(x => x.AccessToken)
            .MaximumLength(4096).WithMessage("Access token is too long.")
            .When(x => x.AccessToken != null);
    }
}

/// <summary>Validator for token validation requests.</summary>
public class ValidateTokenRequestValidator : AbstractValidator<ValidateTokenRequest>
{
    public ValidateTokenRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required.")
            .MaximumLength(4096).WithMessage("Token is too long.");
    }
}
