using System.Reflection;

[assembly: AssemblyTitle(AppRelease.Name)]
[assembly: AssemblyProduct(AppRelease.Name)]
[assembly: AssemblyDescription("Controller battery overlays, notifications and desktop widget")]
[assembly: AssemblyVersion(AppRelease.FileVersion)]
[assembly: AssemblyFileVersion(AppRelease.FileVersion)]
[assembly: AssemblyInformationalVersion(AppRelease.Version)]

static class AppRelease
{
    internal const string Name="DS4 Battery Overlay";
    internal const string Version="1.0";
    internal const string DisplayVersion="Version 1";
    internal const string FileVersion="1.0.0.0";
}
