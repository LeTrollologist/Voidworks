using System.Reflection;

namespace Voidworks.Engine;

/// <summary>
/// Build identity exposed to the window title, console banner, and release
/// diagnostics. The version comes from Directory.Build.props.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int metadata = informational.IndexOf('+');
            return metadata >= 0 ? informational[..metadata] : informational;
        }

        Version? version = typeof(BuildInfo).Assembly.GetName().Version;
        return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
