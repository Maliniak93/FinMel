using FluentValidation;

namespace Application.Core.Bank.Commands.UpdateTransactionCode;
public class UpdateTransactionCodeCommandValidator : AbstractValidator<UpdateTransactionCodeCommand>
{
    public UpdateTransactionCodeCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0).WithMessage("Id must be greater than 0.");

        RuleFor(x => x.Code).NotEmpty().MaximumLength(255).WithMessage("Code field cannot exceed 255 characters.");

        RuleFor(x => x.Description).MaximumLength(255).WithMessage("Description field cannot exceed 255 characters.");
    }
}
