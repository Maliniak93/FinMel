using System;
using System.Text.RegularExpressions;

namespace FinMel.Domain.ValueObjects
{
    /// <summary>
    /// Represents a bank account number as a value object.
    /// Immutable and validated on creation.
    /// </summary>
    public sealed partial class AccountNumber : IEquatable<AccountNumber>
    {
        private static readonly Regex AccountNumberRegex = MyRegex();

        public string Value { get; }

        private AccountNumber(string value)
        {
            Value = value;
        }

        public static AccountNumber Create(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Account number cannot be empty.", nameof(value));

            var normalized = value.Replace(" ", "").Replace("-", "");

            if (!AccountNumberRegex.IsMatch(normalized))
                throw new ArgumentException("Account number must be 16-34 digits.", nameof(value));


            return new AccountNumber(normalized);
        }


        public override bool Equals(object? obj) => Equals(obj as AccountNumber);

        public bool Equals(AccountNumber? other) =>
            other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(AccountNumber left, AccountNumber right) =>
            Equals(left, right);

        public static bool operator !=(AccountNumber left, AccountNumber right) =>
            !Equals(left, right);

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
