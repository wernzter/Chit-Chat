using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using ChitChat.Abstractions;

namespace ChitChat.App;

internal static class PlatformFactory
{
    private const string Dll = "ChitChat.Platform.Windows.dll";
    private static readonly Lazy<IPlatformServices?> Services = new(Load);

    internal static IPlatformServices? Current => Services.Value;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The platform DLL is published untrimmed beside the executable.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "The platform DLL is published untrimmed beside the executable.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "The selected platform entry point has a public constructor.")]
    private static IPlatformServices? Load()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var directory = AppContext.BaseDirectory;
        var file = Path.Combine(directory, Dll);
        if (!File.Exists(file)) return null;

        var context = AssemblyLoadContext.Default;
        context.Resolving += ResolveAudioDependency;
        try
        {
            var assembly = context.LoadFromAssemblyPath(file);
            var entryPoint = assembly.GetType("ChitChat.Platform.Windows.PlatformServices");
            return entryPoint is null ? null : Activator.CreateInstance(entryPoint) as IPlatformServices;
        }
        catch (Exception error) when (error is BadImageFormatException or FileLoadException)
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Runtime audio dependencies accompany the untrimmed platform DLL.")]
    private static Assembly? ResolveAudioDependency(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null || !name.Name.StartsWith("NAudio", StringComparison.Ordinal)) return null;
        if (Path.GetFileName(name.Name) != name.Name) return null;

        var sidecar = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        return File.Exists(sidecar) ? context.LoadFromAssemblyPath(sidecar) : null;
    }
}
