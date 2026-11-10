using System.Runtime.CompilerServices;
using MxDevKit.Preview;

// dotnet run --project tools/BubblePreview [repository root]
// Writes docs/img/bubbles/*.png, docs/img/ring-*.png and the plugin icon.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : FindRoot());
var service = LogiRuntime.Init();
Console.WriteLine($"Logi Plugin Service: {service}");
Render(root);

static string FindRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (!File.Exists(Path.Combine(dir, "config.example.json")))
    {
        dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Run from the repository, or pass its path.");
    }

    return dir;
}

// Kept out of the top-level code so that no Logi type is loaded before LogiRuntime.Init.
[MethodImpl(MethodImplOptions.NoInlining)]
static void Render(string root)
{
    var images = Path.Combine(root, "docs", "img");
    var bubbles = Path.Combine(images, "bubbles");
    Directory.CreateDirectory(bubbles);

    foreach (var (name, bubble) in Samples.Gallery())
    {
        Previews.SaveBubble(bubble, 240, Path.Combine(bubbles, name + ".png"));
        Console.WriteLine($"  bubbles/{name}.png");
    }

    Previews.SaveRing(Samples.ProjectRing(), 4, Path.Combine(images, "ring-project.png"));
    Previews.SaveRing(Samples.OverviewRing(), 7, Path.Combine(images, "ring-overview.png"));
    Previews.SaveRing(Samples.FrenchRing(), 4, Path.Combine(images, "ring-french.png"));
    Previews.SavePair(Samples.ProjectRing(), 2, Samples.OverviewRing(), 7, Path.Combine(images, "hero.png"));
    Previews.SaveIcon(Path.Combine(root, "src", "MxDevKitPlugin", "package", "metadata", "Icon256x256.png"));
    Console.WriteLine("  ring-project.png, ring-overview.png, ring-french.png, hero.png, Icon256x256.png");
}
