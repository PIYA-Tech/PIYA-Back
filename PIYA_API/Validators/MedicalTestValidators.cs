using FluentValidation;
using PIYA_API.Controllers;

namespace PIYA_API.Validators;

/// <summary>Validator for updating medical test status.</summary>
public class UpdateTestStatusRequestValidator : AbstractValidator<UpdateTestStatusRequest>
{
    public UpdateTestStatusRequestValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum().WithMessage("Invalid test status.");

        RuleFor(x => x.Findings)
            .MaximumLength(5000).WithMessage("Findings must not exceed 5000 characters.")
            .When(x => x.Findings != null);
    }
}

/// <summary>Validator for attaching a document to a test.</summary>
public class AttachDocumentRequestValidator : AbstractValidator<AttachDocumentRequest>
{
    public AttachDocumentRequestValidator()
    {
        RuleFor(x => x.DocumentId)
            .NotEmpty().WithMessage("DocumentId is required.");
    }
}

/// <summary>Validator for creating a standalone medical test.</summary>
public class CreateStandaloneTestRequestValidator : AbstractValidator<CreateStandaloneTestRequest>
{
    public CreateStandaloneTestRequestValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty().WithMessage("PatientId is required.");

        RuleFor(x => x.TestType)
            .IsInEnum().WithMessage("Invalid test type.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000).WithMessage("Notes must not exceed 2000 characters.")
            .When(x => x.Notes != null);
    }
}
