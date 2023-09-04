namespace ThriveLauncher.Services;

using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

public class BitmapExtensions : Bitmap
{
    public BitmapExtensions(string uri) : base(AssetLoader.Open(new Uri(uri)))
    {
    }
}
