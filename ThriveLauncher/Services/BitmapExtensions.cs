namespace ThriveLauncher.Services;

using System;
using Avalonia.Media.Imaging;
using Avalonia.Shared.PlatformSupport;

public class BitmapExtensions : Bitmap
{
    public BitmapExtensions(string uri) : base(new AssetLoader().Open(new Uri(uri)))
    {
    }
}
