using System.Reflection;
using Avalonia.Headless;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The suite must build the Avalonia application once per assembly, not once per test.
/// </summary>
public class TestIsolationLevelTests
{
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_application_is_built_once_per_assembly_not_once_per_test()
    {
        var attribute = typeof(TestIsolationLevelTests).Assembly
            .GetCustomAttribute<AvaloniaTestIsolationAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(AvaloniaTestIsolationLevel.PerAssembly, attribute.IsolationLevel);
    }
}
