namespace ThriveLauncher.Services.Localization;

using System.ComponentModel;
using System.Diagnostics;
using Properties;

public class Localizer : INotifyPropertyChanged
{
    // These property names are some dark magic from:
    // https://www.sakya.it/wordpress/avalonia-ui-framework-localization/
    private const string IndexerName = "Item";
    private const string IndexerArrayName = "Item[]";

    public Localizer()
    {
        Languages.OnLanguageChanged += Invalidate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // We don't need to dispose or finalize as we are a singleton
    // ~Localizer()
    // {
    //     Languages.OnLanguageChanged -= Invalidate;
    // }

    public static Localizer Instance { get; } = new();

    public string this[string key]
    {
        get
        {
            var result = Resources.ResourceManager.GetString(key, Resources.Culture);

            if (result == null)
            {
                Trace.WriteLine($"Missing resource (translation) text with key: {key}");
                return "TEXT NOT FOUND";
            }

            // resx files can natively have new lines, so this isn't needed
            // return result.Replace("\\n", "\n");

            return result;
        }
    }

    public void Invalidate()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerArrayName));
    }
}
