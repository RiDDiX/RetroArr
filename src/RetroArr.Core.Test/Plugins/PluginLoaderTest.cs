using NUnit.Framework;
using RetroArr.Core.Plugins;
using System.Collections.Generic;

namespace RetroArr.Core.Test.Plugins
{
    [TestFixture]
    public class PluginLoaderTest
    {
        [Test]
        public void ValidateManifest_ValidManifest_ReturnsNoErrors()
        {
            var manifest = new PluginManifest
            {
                Name = "test-plugin",
                ApiVersion = "1",
                Type = "script",
                Command = "python3",
                Args = "main.py",
                Permissions = new List<string> { "filesystem:read" },
                TimeoutSeconds = 10
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidateManifest_MissingName_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "",
                ApiVersion = "1",
                Type = "script",
                Command = "python3"
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Count.GreaterThan(0));
            Assert.That(errors[0], Does.Contain("name"));
        }

        [Test]
        public void ValidateManifest_UnsupportedApiVersion_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "test",
                ApiVersion = "99",
                Type = "script",
                Command = "python3"
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Some.Contains("apiVersion"));
        }

        [Test]
        public void ValidateManifest_UnknownType_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "test",
                ApiVersion = "1",
                Type = "unknown_type",
                Command = "python3"
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Some.Contains("type"));
        }

        [Test]
        public void ValidateManifest_MissingCommand_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "test",
                ApiVersion = "1",
                Type = "script",
                Command = ""
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Some.Contains("command"));
        }

        [Test]
        public void ValidateManifest_InvalidPermission_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "test",
                ApiVersion = "1",
                Type = "script",
                Command = "python3",
                Permissions = new List<string> { "root:all" }
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Some.Contains("permission"));
        }

        [Test]
        public void ValidateManifest_TimeoutOutOfRange_ReturnsError()
        {
            var manifest = new PluginManifest
            {
                Name = "test",
                ApiVersion = "1",
                Type = "script",
                Command = "python3",
                TimeoutSeconds = 999
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Has.Some.Contains("timeoutSeconds"));
        }

        [Test]
        public void ValidateManifest_MetadataType_IsValid()
        {
            var manifest = new PluginManifest
            {
                Name = "metadata-test",
                ApiVersion = "1",
                Type = "metadata",
                Command = "python3",
                Args = "main.py"
            };

            var errors = PluginLoader.ValidateManifest(manifest, "/tmp/test");
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void DiscoverAndLoad_NonexistentDirectory_CreatesItAndReturnsEmpty()
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retroarr_test_plugins_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var loader = new PluginLoader(tempDir);
                loader.DiscoverAndLoad();

                Assert.That(loader.Plugins, Is.Empty);
                Assert.That(System.IO.Directory.Exists(tempDir), Is.True);
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                    System.IO.Directory.Delete(tempDir, true);
            }
        }
    }
}
