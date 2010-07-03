using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Risky")]
[assembly: AssemblyDescription("An experimental implementation of the game of Risk.")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

[assembly: AssemblyCopyright("Copyright © Adam Milazzo 2010")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
