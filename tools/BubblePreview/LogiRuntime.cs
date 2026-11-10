namespace MxDevKit.Preview;

using System.Runtime.InteropServices;
using System.Runtime.Loader;

// BitmapBuilder lives in Logi Plugin Service: borrow its assemblies and Skia instead of shipping them.
internal static class LogiRuntime
{
    public static string Init()
    {
        var dir = Environment.GetEnvironmentVariable("LOGI_PLUGIN_SERVICE_DIR") ?? DefaultDir();
        if (!File.Exists(Path.Combine(dir, "PluginApi.dll")))
        {
            throw new DirectoryNotFoundException($"PluginApi.dll not found in {dir}. Install Logi Options+ or set LOGI_PLUGIN_SERVICE_DIR.");
        }

        var dirs = new[]
        {
            dir,
            Path.Combine(dir, ".xamarin", RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"),
        }.Where(Directory.Exists).ToArray();

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (var d in dirs)
            {
                var path = Path.Combine(d, name.Name + ".dll");
                if (File.Exists(path))
                {
                    return context.LoadFromAssemblyPath(path);
                }
            }

            return null;
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            foreach (var d in dirs)
            {
                foreach (var file in new[] { name, name + ".dylib", "lib" + name + ".dylib", name + ".dll" })
                {
                    var path = Path.Combine(d, file);
                    if (File.Exists(path))
                    {
                        return NativeLibrary.Load(path);
                    }
                }
            }

            return IntPtr.Zero;
        };

        return dir;
    }

    private static string DefaultDir() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Logi", "LogiPluginService")
        : "/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle";
}
