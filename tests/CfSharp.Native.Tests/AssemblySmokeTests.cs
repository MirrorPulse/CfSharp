using System.Reflection;

namespace CfSharp.Native.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void NativeAssemblyCanBeLoaded()
    {
        Assembly assembly = Assembly.Load(new AssemblyName("CfSharp.Native"));

        Assert.Equal("CfSharp.Native", assembly.GetName().Name);
    }
}
