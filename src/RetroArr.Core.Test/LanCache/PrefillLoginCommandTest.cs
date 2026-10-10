using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.IO;
using RetroArr.Core.LanCache;

namespace RetroArr.Core.Test.LanCache
{
    // The login hint is copied into a shell as-is. Without -u it runs as root and leaves
    // app.log and the session file owned by root; without -w it runs in /app, where the
    // app user can't create app.log; without HOME a uid with no passwd entry caches in /.
    // Each way the tool crashes.
    [TestFixture]
    public class PrefillLoginCommandTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_prefill_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Test]
        public void LoginCommand_RunsAsTheAppUser()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Assert.Ignore("uid only exists on Linux/macOS");

            var status = new LanCachePrefillService(new ConfigurationService(_root)).GetAllStatus();
            var user = $"{PathPermissionChecker.CurrentUid()}:{PathPermissionChecker.CurrentGid()}";
            var home = Environment.GetEnvironmentVariable("HOME");

            Assert.That(status.Single(s => s.Id == "steam").LoginCommand,
                Is.EqualTo($"docker exec -it -u {user} -e HOME={home} -w /opt/steamprefill retroarr /opt/steamprefill/SteamPrefill select-apps"));
            Assert.That(status.Single(s => s.Id == "epic").LoginCommand,
                Is.EqualTo($"docker exec -it -u {user} -e HOME={home} -w /opt/epicprefill retroarr /opt/epicprefill/EpicPrefill select-apps"));
            Assert.That(status.Single(s => s.Id == "battlenet").LoginCommand, Is.Null);
        }
    }
}
