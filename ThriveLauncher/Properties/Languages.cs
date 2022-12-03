namespace ThriveLauncher.Properties;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
///   Helper for loading valid languages. Note adding new languages needs manual edits here
/// </summary>
public static class Languages
{
    private static readonly CultureInfo StartUpLanguage = CultureInfo.CurrentCulture;
    private static readonly CultureInfo DefaultLanguage = new("en-GB");

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

        // The following need to be sorted according to the native language names
        // TODO: create a tool (in Scripts) to output the correct order (a slight complication is that the scripts
        // doesn't import this project, so this needs to be moved to a common module or a new one created)

        yield return new CultureInfo("pl-PL");
        yield return new CultureInfo("pt-BR");
        yield return new CultureInfo("fi-FI");
        yield return new CultureInfo("tr-TR");
        yield return new CultureInfo("bg-BG");
        yield return new CultureInfo("uk-UA");
    }

    public static void SetLanguage(CultureInfo cultureInfo)
    {
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
}
