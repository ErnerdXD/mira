// Version/identity metadata for Miro.exe.
//
// Without this the compiled exe has empty Properties -> Details: no product
// name, no description, and Task Manager shows a bare filename. These
// attributes are what Windows reads for the file's version resource.

using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Miro")]
[assembly: AssemblyProduct("Miro")]
[assembly: AssemblyDescription("Miracast receiver for Windows - mirror your Android phone to your PC")]
[assembly: AssemblyCompany("ErnerdXD")]
[assembly: AssemblyCopyright("MIT Licensed")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

[assembly: ComVisible(false)]
