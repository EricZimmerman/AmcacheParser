// --------------------------------------------------------------------------------------------------------------------
// <summary>
//   Defines the CustomNullTypeConverter type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace Amcache.Converters
{
    using CsvHelper;
    using CsvHelper.Configuration;
    using CsvHelper.TypeConversion;

    /// <inheritdoc />
    /// <summary>
    /// The custom null type converter.
    /// Use in case of null object should not be ignored.
    /// </summary>
    /// <typeparam name="T">
    /// Type of object to be converted
    /// </typeparam>
    public class CustomNullTypeConverter<T> : DefaultTypeConverter
    {
        /// <summary>Converts the object to a string. Converts null object to an empty string</summary>
        /// <param name="value">The object to convert to a string.</param>
        /// <param name="row">The <see cref="T:CsvHelper.IWriterRow" /> for the current record.</param>
        /// <param name="memberMapData">The <see cref="T:CsvHelper.Configuration.MemberMapData" /> for the member being written.</param>
        /// <returns>The string representation of the object.</returns>
        public override string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var converter = row.Configuration.TypeConverterCache.GetConverter<T>();
            return converter.ConvertToString(value, row, memberMapData);
        }
    }
}
