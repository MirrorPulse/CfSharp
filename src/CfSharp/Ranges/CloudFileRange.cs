namespace CfSharp;

/// <summary>Identifies a validated byte range in a cloud file.</summary>
/// <remarks>
/// The default value is invalid. Construct a finite range or use <see cref="ToEnd(long)"/>. The
/// value owns no resources and is safe for concurrent use.
/// </remarks>
public readonly record struct CloudFileRange
{
    private CloudFileRange(long offset, long length, bool extendsToEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (!extendsToEnd)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
            try
            {
                _ = checked(offset + length);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    length,
                    "The range end exceeds the largest supported file offset.");
            }
        }

        Offset = offset;
        Length = length;
        ExtendsToEnd = extendsToEnd;
    }

    /// <summary>Initializes a finite non-empty range.</summary>
    /// <param name="offset">Zero-based byte offset.</param>
    /// <param name="length">Positive number of bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative, zero, or overflows.</exception>
    public CloudFileRange(long offset, long length)
        : this(offset, length, extendsToEnd: false)
    {
    }

    /// <summary>Gets the zero-based first byte.</summary>
    public long Offset { get; }

    /// <summary>Gets the finite length, or zero when <see cref="ExtendsToEnd"/> is true.</summary>
    public long Length { get; }

    /// <summary>Gets whether the range continues through the logical end of the file.</summary>
    public bool ExtendsToEnd { get; }

    /// <summary>Gets a range covering the complete file.</summary>
    public static CloudFileRange WholeFile => ToEnd(0);

    /// <summary>Creates a range from an offset through the logical end of the file.</summary>
    public static CloudFileRange ToEnd(long offset) => new(offset, 0, extendsToEnd: true);

    internal void Validate(string parameterName)
    {
        if (!ExtendsToEnd && Length <= 0)
        {
            throw new ArgumentException("The default cloud file range is invalid.", parameterName);
        }
    }
}
