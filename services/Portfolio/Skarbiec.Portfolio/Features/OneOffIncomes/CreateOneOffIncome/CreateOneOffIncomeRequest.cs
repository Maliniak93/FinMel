using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.CreateOneOffIncome;

public sealed record CreateOneOffIncomeRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal Amount { get; init; }

    public required Guid AccountId { get; init; }

    public required DateOnly Date { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        OneOffValidation.Validate(Date);
}
