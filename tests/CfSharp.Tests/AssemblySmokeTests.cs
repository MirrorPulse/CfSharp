using System.Reflection;

namespace CfSharp.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void HighLevelAssemblyCanBeLoaded()
    {
        Assembly assembly = Assembly.Load(new AssemblyName("CfSharp"));

        Assert.Equal("CfSharp", assembly.GetName().Name);
    }
}
