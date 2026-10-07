using System.Reflection;

[assembly: AssemblyTitle("Hayase BindVPN")]
[assembly: AssemblyDescription("VPN network interface restrictions for Hayase on Windows")]
[assembly: AssemblyProduct("Hayase BindVPN")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Vramuser")]
[assembly: AssemblyVersion(HayaseBindVPN.ReleaseInfo.AssemblyVersion)]
[assembly: AssemblyFileVersion(HayaseBindVPN.ReleaseInfo.AssemblyVersion)]
[assembly: AssemblyInformationalVersion(HayaseBindVPN.ReleaseInfo.Version)]

namespace HayaseBindVPN
{
    internal static class ReleaseInfo
    {
        public const string Version = "1.0.0";
        public const string AssemblyVersion = "1.0.0.0";
    }
}
