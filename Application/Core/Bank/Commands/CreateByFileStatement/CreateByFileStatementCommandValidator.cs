using FluentValidation;

namespace Application.Core.Bank.Commands.CreateByFileStatement;
public class CreateByFileStatementCommandValidator : AbstractValidator<CreateByFileStatementCommand>
{
    public CreateByFileStatementCommandValidator()
    {
        RuleFor(x => x.File.Length).NotNull().LessThanOrEqualTo(200000)
                .WithMessage("File size is larger than allowed");

        RuleFor(x => x.File.ContentType).NotNull().Must(x => x.Equals("text/xml"))
                        .WithMessage("File type is not xml");
    }
}
