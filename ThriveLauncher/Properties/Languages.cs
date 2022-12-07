namespace ThriveLauncher.Properties;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

/// <summary>
///   Helper for loading valid languages. Note adding new languages needs manual edits here
/// </summary>
public static class Languages
{
    private static readonly CultureInfo StartUpLanguage = CultureInfo.CurrentCulture;
    private static readonly CultureInfo DefaultLanguage = new("en-GB");

    /// <summary>
    ///   The languages the launcher is translated into. These need to be sorted alphabetically.
    ///   Running the launcher with "--list-languages" will give the right sorted order.
    /// </summary>
    private static readonly string[] AdditionalAvailableLanguages =
    {
        "de-DE",
        "fr-FR",
        "pl-PL",
        "pt-BR",
        "ro-RO",
        "fi-FI",
        "sv-SV",
        "tr-TR",
        "bg-BG",
        "ru-RU",
        "uk-UA",
    };

    public delegate void OnCurrentLanguageChanged();

    /// <summary>
    ///   Triggered when the program language is changed through the proper method
    ///   (<see cref="SetLanguage(System.Globalization.CultureInfo)"/>)
    /// </summary>
    public static event OnCurrentLanguageChanged? OnLanguageChanged;

    /// <summary>
    ///   Gets available languages in a dictionary
    /// </summary>
    /// <returns>The available languages with the native names being the keys</returns>
    public static Dictionary<string, CultureInfo> GetAvailableLanguages()
    {
        return GetLanguagesEnumerable().ToDictionary(i => i.NativeName, i => i);
    }

    /// <summary>
    ///   Detects the culture that should be shown as active (falls back to english)
    /// </summary>
    /// <param name="availableCultures"></param>
    /// <returns></returns>
    public static CultureInfo GetCurrentlyUsedCulture(Dictionary<string, CultureInfo> availableCultures)
    {
        return GetMatchingCultureOrDefault(CultureInfo.DefaultThreadCurrentUICulture ?? CultureInfo.CurrentUICulture,
            availableCultures);
    }

    public static CultureInfo GetMatchingCultureOrDefault(CultureInfo cultureToMatch,
        Dictionary<string, CultureInfo> availableCultures)
    {
        foreach (var culture in availableCultures.Values)
        {
            if (culture.Equals(cultureToMatch))
                return culture;
        }

        return DefaultLanguage;
    }

    public static IEnumerable<CultureInfo> GetLanguagesEnumerable()
    {
        // Default language needs to be first
        yield return DefaultLanguage;

        if (AdditionalAvailableLanguages.Contains("en-BG"))
            throw new InvalidOperationException("Default locale should not be in the additional locales list");

        foreach (var language in AdditionalAvailableLanguages)
        {
            CultureInfo cultureInfo;

            try
            {
                cultureInfo = new CultureInfo(language);
            }
            catch (Exception e)
            {
                Trace.TraceError($"Language {language} is not available on this platform: {e}");
                continue;
            }

            yield return cultureInfo;
        }
    }

    public static void SetLanguage(CultureInfo cultureInfo)
    {
        // Ensure startup language is detected, probably unneeded but we don't need any bugs related to this
        GetStartupLanguage();

        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

        Resources.Culture = cultureInfo;

        OnLanguageChanged?.Invoke();
    }

    public static void SetLanguage(string nativeName)
    {
        var language = GetLanguagesEnumerable().FirstOrDefault(l => l.NativeName == nativeName);

        if (language == null)
            throw new ArgumentException("Unknown specified language");

        SetLanguage(language);
    }

    public static CultureInfo GetDefaultLanguage()
    {
        return DefaultLanguage;
    }

    /// <summary>
    ///   Gets the startup locale
    /// </summary>
    /// <returns>The startup locale</returns>
    public static CultureInfo GetStartupLanguage()
    {
        return StartUpLanguage;
    }
}
