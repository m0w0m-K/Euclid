using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Euclid
{
    /// <summary>
    /// Localized UI text for Euclid.
    ///
    /// Locale files are embedded in the mod DLL from Localization/*.lang.  Each file is UTF-8
    /// tab-separated text: key<TAB>translation.  Missing keys and unknown game languages fall back
    /// to English, so adding a new string never makes the UI unusable.
    ///
    /// The selected locale follows ADOFAI's RDString.language directly.  We intentionally map by
    /// enum name instead of referencing every SystemLanguage member at compile time; this keeps the
    /// mod tolerant of small Unity enum differences between game versions.
    /// </summary>
    internal static class EuclidText
    {
        private const string DefaultLocale = "en";

        private static readonly string[] SupportedLocales =
        {
            "en", "ko", "zh-CN", "zh-TW", "ja", "fr", "de", "ru", "ro", "pl", "es", "pt-BR", "vi", "cs",
        };

        private static readonly Dictionary<string, Dictionary<string, string>> Cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        internal static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string locale = ResolveLocaleCode();
            string text;
            if (TryGetFromLocale(locale, key, out text))
            {
                return text;
            }

            if (!string.Equals(locale, DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
                TryGetFromLocale(DefaultLocale, key, out text))
            {
                return text;
            }

            return key;
        }

        internal static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>
        /// Returns every translation that can be represented by this Unity version's SystemLanguage
        /// enum.  This method is currently only a compatibility helper; normal UI should call Get().
        /// </summary>
        internal static Dictionary<SystemLanguage, string> All(string key)
        {
            var result = new Dictionary<SystemLanguage, string>();
            AddLanguage(result, "English", "en", key);
            AddLanguage(result, "Korean", "ko", key);
            AddLanguage(result, "ChineseSimplified", "zh-CN", key);
            AddLanguage(result, "ChineseTraditional", "zh-TW", key);
            AddLanguage(result, "Japanese", "ja", key);
            AddLanguage(result, "French", "fr", key);
            AddLanguage(result, "German", "de", key);
            AddLanguage(result, "Russian", "ru", key);
            AddLanguage(result, "Romanian", "ro", key);
            AddLanguage(result, "Polish", "pl", key);
            AddLanguage(result, "Spanish", "es", key);
            AddLanguage(result, "Portuguese", "pt-BR", key);
            AddLanguage(result, "Vietnamese", "vi", key);
            AddLanguage(result, "Czech", "cs", key);
            return result;
        }

        internal static string CurrentLocaleCode => ResolveLocaleCode();

        private static bool TryGetFromLocale(string locale, string key, out string text)
        {
            var table = GetLocaleTable(locale);
            return table.TryGetValue(key, out text);
        }

        private static Dictionary<string, string> GetLocaleTable(string locale)
        {
            Dictionary<string, string> table;
            if (Cache.TryGetValue(locale, out table))
            {
                return table;
            }

            table = LoadEmbeddedLocale(locale);
            Cache[locale] = table;
            return table;
        }

        private static Dictionary<string, string> LoadEmbeddedLocale(string locale)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            Assembly assembly = Assembly.GetExecutingAssembly();
            string suffix = ".Localization." + locale + ".lang";
            string resourceName = null;

            foreach (string candidate in assembly.GetManifestResourceNames())
            {
                if (candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = candidate;
                    break;
                }
            }

            if (resourceName == null)
            {
                EuclidMod.Logger?.Log("Localization resource not found: " + locale);
                return table;
            }

            try
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return table;
                    }

                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0 || line[0] == '#')
                            {
                                continue;
                            }

                            int separator = line.IndexOf('\t');
                            if (separator <= 0)
                            {
                                continue;
                            }

                            string key = line.Substring(0, separator).Trim();
                            string value = line.Substring(separator + 1).Replace("\\n", "\n");
                            if (key.Length > 0)
                            {
                                table[key] = value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EuclidMod.Logger?.Log("Failed to load localization '" + locale + "': " + ex.Message);
            }

            return table;
        }

        private static string ResolveLocaleCode()
        {
            string languageName;
            try
            {
                languageName = RDString.language.ToString();
            }
            catch
            {
                return DefaultLocale;
            }

            switch (languageName)
            {
                case "Korean":
                    return "ko";
                case "ChineseSimplified":
                case "Chinese":
                    return "zh-CN";
                case "ChineseTraditional":
                    return "zh-TW";
                case "Japanese":
                    return "ja";
                case "French":
                    return "fr";
                case "German":
                    return "de";
                case "Russian":
                    return "ru";
                case "Romanian":
                    return "ro";
                case "Polish":
                    return "pl";
                case "Spanish":
                    return "es";
                case "Portuguese":
                case "PortugueseBrazil":
                case "BrazilianPortuguese":
                    return "pt-BR";
                case "Vietnamese":
                    return "vi";
                case "Czech":
                    return "cs";
                case "English":
                default:
                    return DefaultLocale;
            }
        }

        private static void AddLanguage(
            Dictionary<SystemLanguage, string> result,
            string systemLanguageName,
            string locale,
            string key)
        {
            SystemLanguage language;
            if (!Enum.TryParse(systemLanguageName, out language))
            {
                return;
            }

            string text;
            if (!TryGetFromLocale(locale, key, out text) && !TryGetFromLocale(DefaultLocale, key, out text))
            {
                text = key;
            }

            result[language] = text;
        }
    }
}
