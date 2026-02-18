namespace TerraformDotnet.Hcl.Common;

/// <summary>
/// Represents a position in an HCL source document.
/// </summary>
/// <remarks>
/// Lines and columns are 1-based. Offset is 0-based byte offset from the start of the input.
/// </remarks>
public readonly struct Mark : IEquatable<Mark>
{
    /// <summary>
    /// Gets the zero-based byte offset from the beginning of the input.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets the one-based line number.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the one-based column number.
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mark"/> struct.
    /// </summary>
    /// <param name="offset">Zero-based byte offset.</param>
    /// <param name="line">One-based line number.</param>
    /// <param name="column">One-based column number.</param>
    public Mark(int offset, int line, int column)
    {
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <inheritdoc />
    public bool Equals(Mark other) =>
        Offset == other.Offset && Line == other.Line && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Mark other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Offset, Line, Column);

    /// <summary>
    /// Returns a human-readable string representation like <c>(Line: 1, Col: 5)</c>.
    /// </summary>
    public override string ToString() => $"(Line: {Line}, Col: {Column})";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Mark left, Mark right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Mark left, Mark right) => !left.Equals(right);
}
