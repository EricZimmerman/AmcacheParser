// --------------------------------------------------------------------------------------------------------------------
// <summary>
//   Defines the CustomNullTypeConverter type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Amcache.Converters;

using System;
using CsvHelper;

/// <summary>
/// The custom null type converter (CsvHelper 34: an <see cref="ICsvConverter{T}" />, for <c>[CsvConverter]</c>).
/// Writes a null value as an empty field, whatever <see cref="CsvWriterOptions.NullValue" /> is.
/// AmcacheParser no longer uses it; it is kept for the library's users.
/// </summary>
/// <typeparam name="T">
/// Type of object to be converted
/// </typeparam>
public class CustomNullTypeConverter<T> : ICsvConverter<T>
{
    /// <summary>Not supported: the converter is for writing only (as it was with CsvHelper 33)</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="index">The index of the field.</param>
    /// <returns>Nothing: it throws.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public T Read(CsvReader reader, int index) =>
        throw new NotSupportedException($"{nameof(CustomNullTypeConverter<T>)} only writes values");

    /// <summary>Writes the value, or an empty field for null</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public void Write(CsvWriter writer, T value)
    {
        if (value == null)
        {
            writer.WriteField(string.Empty);
            return;
        }

        writer.WriteField((object) value);
    }
}
