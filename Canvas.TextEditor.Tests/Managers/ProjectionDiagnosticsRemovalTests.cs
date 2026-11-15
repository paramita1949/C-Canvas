using System;
using System.Linq;
using System.Reflection;
using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers;

public class ProjectionDiagnosticsRemovalTests
{
    private static readonly Type Manager = typeof(ProjectionManager);
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void ProjectionTxtWriterTypeIsRemoved()
        => Assert.Null(Manager.Assembly.GetType("ImageColorChanger.Services.Diagnostics.ProjectionDiagnosticLog"));

    [Fact]
    public void NativeDiagnosticSnapshotCollectorTypeIsRemoved()
        => Assert.Null(Manager.Assembly.GetType("ImageColorChanger.Managers.ProjectionDiagnosticSnapshotCollector"));

    [Fact]
    public void DiagnosticStateAndPublicLogPathAreRemoved()
    {
        Assert.Null(Manager.GetProperty("ProjectionDiagnosticLogPath", Members));
        Assert.DoesNotContain(Manager.GetFields(Members), field => field.Name.Contains("projectionDiagnostic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticLifecycleTimerAndDisplayEventCallbacksAreRemoved()
        => Assert.DoesNotContain(Manager.GetMethods(Members), method => method.Name.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase));
}
