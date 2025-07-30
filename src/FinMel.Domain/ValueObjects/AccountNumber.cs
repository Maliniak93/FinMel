using System.Text.RegularExpressions;

namespace FinMel.Domain.ValueObjects
{
    /// <summary>
    /// Represents a bank account number as a value object.
    /// Immutable and validated on creation.
    /// </summary>
    public sealed partial record AccountNumber(string Value)
    {
        private static readonly Regex AccountNumberRegex = MyRegex();

        public static AccountNumber Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Account number cannot be empty.", nameof(value));

            var normalized = value.Replace(" ", "").Replace("-", "");

            if (!AccountNumberRegex.IsMatch(normalized))
                throw new ArgumentException("Account number must be 16-34 digits.", nameof(value));

            return new AccountNumber(normalized);
        }

        public override string ToString() => Value;

        /// <summary>
        /// Compiles a regex to validate account numbers.
        /// The regex checks for 16 to 34 digits, allowing for spaces or dashes.
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex(@"^\d{16,34}$", RegexOptions.Compiled)]
        private static partial Regex MyRegex();
    }
}
