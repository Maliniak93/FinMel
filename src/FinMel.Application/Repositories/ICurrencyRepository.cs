using FinMel.Domain.Entities.Currency;

namespace FinMel.Application.Repositories;

public interface ICurrencyRepository
{
    /// <summary>
    /// Gets a currency by its 3-letter ISO code (e.g., "PLN", "EUR").
    /// </summary>
    /// <param name="code">The ISO currency code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The currency entity or null if not found.</returns>
    Task<Currency?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currencies.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all currencies.</returns>
    Task<IReadOnlyList<Currency>> GetAllAsync(CancellationToken cancellationToken = default);
}
