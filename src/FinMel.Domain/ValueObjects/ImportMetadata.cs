using System;
using FinMel.Domain.Abstractions;

namespace FinMel.Domain.ValueObjects
{
    /// <summary>
    /// Represents metadata about an import operation (e.g., file name, import date, source, hash).
    /// Immutable value object.
    /// </summary>
    public sealed record ImportMetadata(
        string FileName,
        DateTime ImportedAt,
        string Source,
        string FileHash
    ) : ValueObject;
}
