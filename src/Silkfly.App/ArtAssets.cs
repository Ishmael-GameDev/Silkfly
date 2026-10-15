using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Silkfly.App;

public static class ArtAssets
{
    public static Bitmap? Logo { get; } = Load("silkfly-1.png");

    public static Bitmap? InfoLandscape { get; } = Load("info-landscape.png");

    public static Bitmap? FliesScatter { get; } = Load("silkflies-scatter.png");

    public static bool HasLogo => Logo is not null;

    public static bool NoLogo => Logo is null;

    public static bool HasInfoArt => InfoLandscape is not null;

    public static bool NoInfoArt => InfoLandscape is null;

    private static Bitmap? Load(string name)
    {
        var uri = new Uri($"avares://Silkfly/Assets/Art/{name}");
        if (!AssetLoader.Exists(uri))
            return null;

        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }
}
