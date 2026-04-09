using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for creating a referral.</summary>
public class CreateReferralRequestValidator : AbstractValidator<CreateReferralRequest>
{
    public CreateReferralRequestValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.ReferredToSpecialty)
            .IsInEnum().WithMessage("Invalid specialty.");

        RuleFor(x => x.Urgency)
            .IsInEnum().WithMessage("Invalid urgency level.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Referral reason is required.")
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");

        RuleFor(x => x.ClinicalNotes)
            .MaximumLength(5000).WithMessage("Clinical notes must not exceed 5000 characters.")
            .When(x => x.ClinicalNotes != null);

        RuleFor(x => x.ExternalProviderName)
            .NotEmpty().WithMessage("External provider name is required for external referrals.")
            .When(x => x.IsExternal);

        RuleFor(x => x.ExternalProviderName)
            .MaximumLength(200).WithMessage("External provider name must not exceed 200 characters.")
            .When(x => x.ExternalProviderName != null);

        RuleFor(x => x.ExternalProviderContact)
            .MaximumLength(200).WithMessage("External provider contact must not exceed 200 characters.")
            .When(x => x.ExternalProviderContact != null);

        RuleFor(x => x.OrderedTests)
            .Must(t => t!.Count <= 20).WithMessage("Cannot order more than 20 tests at once.")
            .When(x => x.OrderedTests != null);
    }
}

/// <summary>Validator for forwarding a referral.</summary>
public class ForwardReferralRequestValidator : AbstractValidator<ForwardReferralRequest>
{
    public ForwardReferralRequestValidator()
    {
        RuleFor(x => x.Specialty)
            .IsInEnum().WithMessage("Invalid specialty.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Forward reason is required.")
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");
    }
}

/// <summary>Validator for assigning a doctor to a referral.</summary>
public class AssignDoctorRequestValidator : AbstractValidator<AssignDoctorRequest>
{
    public AssignDoctorRequestValidator()
    {
        RuleFor(x => x.DoctorId)
            .NotEmpty().WithMessage("DoctorId is required.");
    }
}

/// <summary>Validator for declining a referral.</summary>
public class DeclineReferralRequestValidator : AbstractValidator<DeclineReferralRequest>
{
    public DeclineReferralRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(2000).WithMessage("Decline reason must not exceed 2000 characters.")
            .When(x => x.Reason != null);
    }
}

/// <summary>Validator for completing a referral.</summary>
public class CompleteReferralRequestValidator : AbstractValidator<CompleteReferralRequest>
{
    public CompleteReferralRequestValidator()
    {
        RuleFor(x => x.ResultNotes)
            .NotEmpty().WithMessage("Result notes are required.")
            .MaximumLength(5000).WithMessage("Result notes must not exceed 5000 characters.");
    }
}
