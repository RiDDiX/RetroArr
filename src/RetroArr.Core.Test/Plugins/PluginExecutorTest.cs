using NUnit.Framework;
using RetroArr.Core.Plugins;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RetroArr.Core.Test.Plugins
{
    [TestFixture]
    public class PluginExecutorTest
    {
        private PluginExecutor _sut = null!;

        [SetUp]
        public void Setup()
        {
            _sut = new PluginExecutor();
        }

        [Test]
        public async Task ExecuteAsync_DisabledPlugin_ReturnsFailure()
        {
            var manifest = new PluginManifest
            {
                Name = "disabled-test",
                Enabled = false,
                Command = "echo"
            };
            var plugin = new LoadedPlugin(manifest, "/tmp", true, null);

            var result = await _sut.ExecuteAsync(plugin);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("disabled"));
        }

        [Test]
        public async Task ExecuteAsync_InvalidPlugin_ReturnsFailure()
        {
            var manifest = new PluginManifest { Name = "invalid-test" };
            var plugin = new LoadedPlugin(manifest, "/tmp", false, "manifest error");

            var result = await _sut.ExecuteAsync(plugin);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("invalid"));
        }

        [Test]
        public async Task ExecuteAsync_WorkingPlugin_ReturnsSuccess()
        {
            // Create a temporary plugin that echoes input
            var tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_exec_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var scriptPath = Path.Combine(tempDir, "test.sh");
                File.WriteAllText(scriptPath, "#!/bin/sh\necho '{\"status\":\"ok\"}'");
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                var manifest = new PluginManifest
                {
                    Name = "working-test",
                    Command = scriptPath,
                    Enabled = true,
                    TimeoutSeconds = 5
                };
                var plugin = new LoadedPlugin(manifest, tempDir, true, null);

                var result = await _sut.ExecuteAsync(plugin);

                Assert.That(result.Success, Is.True);
                Assert.That(result.Output, Does.Contain("ok"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task ExecuteAsync_TimingOutPlugin_ReturnsTimeout()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_timeout_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var scriptPath = Path.Combine(tempDir, "slow.sh");
                File.WriteAllText(scriptPath, "#!/bin/sh\nsleep 60");
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                var manifest = new PluginManifest
                {
                    Name = "timeout-test",
                    Command = scriptPath,
                    Enabled = true,
                    TimeoutSeconds = 1
                };
                var plugin = new LoadedPlugin(manifest, tempDir, true, null);

                var result = await _sut.ExecuteAsync(plugin);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("timed out"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task ExecuteAsync_CrashingPlugin_ReturnsError()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_crash_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var scriptPath = Path.Combine(tempDir, "crash.sh");
                File.WriteAllText(scriptPath, "#!/bin/sh\nexit 1");
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                var manifest = new PluginManifest
                {
                    Name = "crash-test",
                    Command = scriptPath,
                    Enabled = true,
                    TimeoutSeconds = 5
                };
                var plugin = new LoadedPlugin(manifest, tempDir, true, null);

                var result = await _sut.ExecuteAsync(plugin);

                Assert.That(result.Success, Is.False);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public async Task CircuitBreaker_OpensAfterThreeFailures()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "retroarr_test_cb_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var scriptPath = Path.Combine(tempDir, "fail.sh");
                File.WriteAllText(scriptPath, "#!/bin/sh\nexit 1");
                File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                var manifest = new PluginManifest
                {
                    Name = "cb-test",
                    Command = scriptPath,
                    Enabled = true,
                    TimeoutSeconds = 5
                };
                var plugin = new LoadedPlugin(manifest, tempDir, true, null);

                // Fail 3 times
                await _sut.ExecuteAsync(plugin);
                await _sut.ExecuteAsync(plugin);
                await _sut.ExecuteAsync(plugin);

                // 4th attempt should be blocked by circuit breaker
                var result = await _sut.ExecuteAsync(plugin);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("Circuit breaker"));
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void ResetCircuitBreaker_AfterFailures_AllowsRetry()
        {
            // Simulate failures via the status dictionary
            var status = _sut.GetCircuitBreakerStatus();
            status.TryAdd("reset-test", 5);

            var wasReset = _sut.ResetCircuitBreaker("reset-test");
            Assert.That(wasReset, Is.True);

            var afterReset = _sut.GetCircuitBreakerStatus();
            Assert.That(afterReset.ContainsKey("reset-test"), Is.False);
        }
    }
}
