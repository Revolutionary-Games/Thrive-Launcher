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
    /// <summary>
    ///   The language that the launcher supports and is used by the current user (set to us by the operating system
    ///   on startup). This is a lazy variable to make sure the other fields are initialised before this is tried to
    ///   be computed.
    /// </summary>
    private static readonly Lazy<CultureInfo> StartUpLanguage = new(DetectCurrentLanguageAtStartup);

    private static readonly CultureInfo DefaultLanguage = new("en-GB");

    /// <summary>
    ///   The languages the launcher is translated into. These need to be sorted alphabetically.
    ///   Running the launcher with "--list-languages" will give the right sorted order.
    /// </summary>
    private static readonly string[] AdditionalAvailableLanguages =
    [
        "cs-CZ",
        "de-DE",
        "fr-FR",
        "ka-KA",
        "hr-HR",
        "it-IT",
        "lt-LT",
        "hu-HU",
        "nl-NL",
        "pl-PL",
        "pt-BR",
        "ro-RO",
        "gsw",
        "fi-FI",
        "sv-SE",
        "tr-TR",
        "bg-BG",
        "mk-MK",
        "ru-RU",
        "uk-UA",

        // Requires font support, See: https://github.com/Revolutionary-Games/Thrive-Launcher/issues/194
        // "zh-CN",
    ];

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
    ///   Detects the culture that should be shown as active (falls back to English)
    /// </summary>
    /// <param name="availableCultures">The culture list to get the closest match</param>
    /// <returns>The culture match (or default) that is found in the cultures</returns>
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

        // If no full match is possible, match the first 2 characters
        var firstCharacters = cultureToMatch.Name.Split("-")[0];

        foreach (var culture in availableCultures.Values)
        {
            // This is probably good enough matching here. This is not split to save a bit on computation here
            if (culture.Name.StartsWith(firstCharacters))
                return culture;
        }

        return DefaultLanguage;
    }

    public static IEnumerable<CultureInfo> GetLanguagesEnumerable()
    {
        // The default language needs to be first
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
        // Ensure startup language is detected, this is now needed as the value is lazy
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
    ///   Gets the startup locale (but only when it is supported)
    /// </summary>
    /// <returns>The startup locale</returns>
    public static CultureInfo GetStartupLanguage()
    {
        return StartUpLanguage.Value;
    }

    private static CultureInfo DetectCurrentLanguageAtStartup()
    {
        return GetCurrentlyUsedCulture(GetAvailableLanguages());
    }
}
