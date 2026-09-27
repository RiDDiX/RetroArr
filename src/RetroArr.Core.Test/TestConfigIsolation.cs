using System;
using System.IO;
using NUnit.Framework;

// Outside any namespace so it wraps every fixture in the assembly.
[SetUpFixture]
public class TestConfigIsolation
{
    private string _root = null!;
    private string? _previous;
    private string? _previousHome;

    // PlatformService and ConfigurationService fall back to ApplicationData
    // (~/.config on Linux and macOS) when no config folder sits next to them,
    // and the default library/download folders live under the home folder.
    // Point both at a temp dir so tests never read or write a real install.
    [OneTimeSetUp]
    public void RedirectUserConfig()
    {
        _root = Path.Combine(Path.GetTempPath(), "retroarr_test_config_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _root);
        _previousHome = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", Path.Combine(_root, "home"));
    }

    [OneTimeTearDown]
    public void Restore()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previous);
        Environment.SetEnvironmentVariable("HOME", _previousHome);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
