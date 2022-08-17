using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ThriveLauncher.Properties;

/// <summary>
///   Helper for loading valid languages. Note adding new languages needs manual edits here
/// </summary>
public static class Languages
{
    private static readonly CultureInfo StartUpLanguage = CultureInfo.CurrentCulture;
    private static readonly CultureInfo DefaultLanguage = new("en-GB");

    /// <summary>
    ///   Gets available languages in a dictionary
    /// </summary>
    /// <returns>The available languages with the native names being the keys</returns>
    public static Dictionary<string, CultureInfo> GetAvailableLanguages()
    {
        return GetLanguagesEnumerable().ToDictionary(i => i.NativeName, i => i);
    }

    /// <summary>
    ///   Detects the culture that should be shown as active (falls back to english
    /// </summary>
    /// <param name="availableCultures"></param>
    /// <returns></returns>
    public static CultureInfo GetCurrentlyUsedCulture(Dictionary<string, CultureInfo> availableCultures)
    {
        var current = CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture;
        foreach (var culture in availableCultures.Values)
        {
            if (culture.Equals(current))
                return culture;
        }

        return DefaultLanguage;
    }

    public static IEnumerable<CultureInfo> GetLanguagesEnumerable()
    {
        // Default language needs to be first
        yield return DefaultLanguage;

        // The following need to sorted according to the native language names

        yield return new CultureInfo("fi-FI");
    }

    public static void SetLanguage(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }

    public static void SetLanguage(string nativeName)
    {
        var language = GetLanguagesEnumerable().FirstOrDefault(l => l.NativeName == nativeName);

        if (language == null)
            throw new ArgumentException("Unknown specified language");

        SetLanguage(language);
    }
}
