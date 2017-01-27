namespace tomenglertde.ResXManager.Infrastructure
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Globalization;

    public static class ExtensionMethods
    {
        /// <summary>
        /// Converts the culture key name to the corresponding culture. The key name is the ieft language tag with an optional '.' prefix.
        /// </summary>
        /// <param name="cultureKeyName">Key name of the culture, optionally prefixed with a '.'.</param>
        /// <returns>
        /// The culture, or <c>null</c> if the key name is empty.
        /// </returns>
        /// <exception cref="System.InvalidOperationException">Error parsing language:  + cultureKeyName</exception>
        public static CultureInfo ToCulture(this string cultureKeyName)
        {
            try
            {
                cultureKeyName = cultureKeyName?.TrimStart('.');

                // MEP: Our incoming spreadsheets often contain a more friendly language name. If we
                // see any of those, we'll convert them to a Culture Name GetCultureInfo() can work
                // with. For a list of those, see:
                // https://www.microsoft.com/resources/msdn/goglobal/default.mspx

                switch (cultureKeyName)
                {
                    case "English":
                        cultureKeyName = String.Empty;
                        break;

                    case "German":
                        cultureKeyName = "de-DE";
                        break;

                    case "Spanish":
                        cultureKeyName = "es-ES";
                        break;

                    case "French":
                        cultureKeyName = "fr-FR";
                        break;

                    case "Italian":
                        cultureKeyName = "it-IT";
                        break;

                    case "Japanese":
                        cultureKeyName = "ja-JP";
                        break;

                    case "Korean":
                        cultureKeyName = "ko-KR";
                        break;

                    case "Dutch":
                        cultureKeyName = "nl-NL";
                        break;

                    case "Portuguese":
                        cultureKeyName = "pt-PT";
                        break;

                    case "Chinese (Simplified)":
                        cultureKeyName = "zh-Hans";
                        break;

                    case "Chinese (Traditional)":
                        cultureKeyName = "zh-Hant";
                        break;
                }

                return string.IsNullOrEmpty(cultureKeyName) ? null : CultureInfo.GetCultureInfo(cultureKeyName);
            }
            catch (Exception)
            {
            }

            throw new InvalidOperationException("Error parsing language: " + cultureKeyName);
        }

        /// <summary>
        /// Converts the culture key name to the corresponding culture. The key name is the ieft language tag with an optional '.' prefix.
        /// </summary>
        /// <param name="cultureKeyName">Key name of the culture, optionally prefixed with a '.'.</param>
        /// <returns>
        /// The cultureKey, or <c>null</c> if the culture is invalid.
        /// </returns>
        [ContractVerification(false)] // because of try/catch
        public static CultureKey ToCultureKey(this string cultureKeyName)
        {
            try
            {
                cultureKeyName = cultureKeyName?.TrimStart('.');

                return new CultureKey(string.IsNullOrEmpty(cultureKeyName) ? null : CultureInfo.GetCultureInfo(cultureKeyName));
            }
            catch (ArgumentException)
            {
            }

            return null;
        }
    }
}
