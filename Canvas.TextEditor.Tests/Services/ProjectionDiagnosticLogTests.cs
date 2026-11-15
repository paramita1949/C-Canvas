using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace Canvas.TextEditor.Tests.Services;

public class ProjectionDiagnosticLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-diag-tests-" + Guid.NewGuid().ToString("N"));

    private dynamic Create(params string[] directories)
    {
        var type = typeof(ProjectionDiagnosticLogTests).Assembly.GetType("ImageColorChanger.Services.Diagnostics.ProjectionDiagnosticLog")
            ?? AppDomain.CurrentDomain.GetAssemblies().SelectType();
        Assert.NotNull(type);
        return Activator.CreateInstance(type, new object[] { directories, "6.1.3.1-test" });
    }

    [Fact]
    public void ExceptionSummaryExcludesSensitiveMessageAndStack()
    {
        dynamic log = Create(_root);
        var type = ((object)log).GetType();
        var ex = new InvalidOperationException("SECRET_CONTENT C:\\Users\\ACCOUNT\\personal-song.txt", new Exception("INNER_SECRET"));
        string summary = (string)type.GetMethod("DescribeException").Invoke(null, new object[] { ex });
        Assert.Contains("InvalidOperationException", summary);
        Assert.Contains("HRESULT=0x", summary);
        Assert.DoesNotContain("SECRET", summary);
        Assert.DoesNotContain("ACCOUNT", summary);
        Assert.DoesNotContain("personal-song", summary);
    }

    [Fact]
    public void CreatesUtf8TxtAndFlushesEachStage()
    {
        dynamic log = Create(Path.Combine(_root, "中文目录"));
        string path = log.FilePath;
        Assert.EndsWith(".txt", path);
        Assert.True((bool)log.Append("before-open", "SelectedDevice=DISPLAY2\nActualProjectionDevice=DISPLAY1\n中文诊断"));
        string text = File.ReadAllText(path);
        Assert.Contains("6.1.3.1-test", text);
        Assert.Contains("before-open", text);
        Assert.Contains("ActualProjectionDevice=DISPLAY1", text);
        Assert.Contains("中文诊断", text);
        Assert.Contains("UTC=", text);
        Assert.Contains("SessionId=", text);
    }

    [Fact]
    public void RepeatedSessionsDoNotOverwrite()
    {
        dynamic first = Create(_root);
        dynamic second = Create(_root);
        Assert.NotEqual((string)first.FilePath, (string)second.FilePath);
        Assert.Equal(2, Directory.GetFiles(_root, "*.txt").Length);
    }

    [Fact]
    public void BlockedPreferredDirectoryFallsBackAndRecordsError()
    {
        Directory.CreateDirectory(_root);
        string blocked = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blocked, "leave unchanged");
        dynamic log = Create(blocked, Path.Combine(_root, "fallback"));
        Assert.StartsWith(Path.Combine(_root, "fallback"), (string)log.FilePath);
        Assert.Contains("PathFallback", File.ReadAllText((string)log.FilePath));
        Assert.Equal("leave unchanged", File.ReadAllText(blocked));
    }

    [Fact]
    public void AllDirectoriesUnavailableDoesNotThrow()
    {
        Directory.CreateDirectory(_root);
        string blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "fixture");
        dynamic log = Create(blocked);
        Assert.Null((string)log.FilePath);
        Assert.False((bool)log.Append("open-failed", "missing data"));
        Assert.NotEmpty((string)log.LastError);
    }

    [Theory]
    [InlineData("no-secondary-screen", "CachedScreenCount=1")]
    [InlineData("invalid-selection", "SelectedIndex=-1")]
    [InlineData("primary-screen-rejected", "IsPrimary=True")]
    [InlineData("display-settings-changed", "BeforePrimary=DISPLAY1\nAfterPrimary=DISPLAY2")]
    [InlineData("after-close", "ProjectionHandle=0")]
    public void PreservesLiteralNegativeAndRecoveryEvidence(string stage, string detail)
    {
        dynamic log = Create(_root);
        Assert.True((bool)log.Append(stage, detail));
        string text = File.ReadAllText((string)log.FilePath);
        Assert.Contains(stage, text);
        Assert.Contains(detail, text);
    }

    [Fact]
    public void LargePayloadIsBoundedAndMarkedWithoutThrowing()
    {
        dynamic log = Create(_root);
        log.Append("large", new string('中', 100000));
        string text = File.ReadAllText((string)log.FilePath);
        Assert.Contains("TRUNCATED", text);
        Assert.True(new FileInfo((string)log.FilePath).Length < 2 * 1024 * 1024);
    }

    [Fact]
    public void EmptyDataCanBeRecorded()
    {
        dynamic log = Create(_root);
        Assert.True((bool)log.Append("default", null));
        Assert.Contains("default", File.ReadAllText((string)log.FilePath));
    }

    [Fact]
    public void CapacityStopsFurtherWritesWithoutDeletingExistingEvidence()
    {
        dynamic log = Create(_root);
        for (int i = 0; i < 100; i++) log.Append("event", new string('中', 32000));
        long size = new FileInfo((string)log.FilePath).Length;
        Assert.True(size <= 2 * 1024 * 1024);
        Assert.False((bool)log.Append("overflow", new string('中', 32000)));
        Assert.Equal(size, new FileInfo((string)log.FilePath).Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

internal static class ProjectionDiagnosticTypeLookup
{
    public static Type SelectType(this Assembly[] assemblies)
    {
        foreach (var a in assemblies)
        {
            var type = a.GetType("ImageColorChanger.Services.Diagnostics.ProjectionDiagnosticLog");
            if (type != null) return type;
        }
        return null;
    }
}
