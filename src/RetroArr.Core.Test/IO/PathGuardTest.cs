using NUnit.Framework;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.IO
{
    [TestFixture]
    public class PathGuardTest
    {
        [TestCase("/home")]
        [TestCase("/home/")]
        [TestCase("/etc")]
        [TestCase("/usr")]
        [TestCase("")]
        [TestCase(null)]
        public void IsCriticalPath_SystemFoldersAndRoots(string? path)
        {
            Assert.That(PathGuard.IsCriticalPath(path), Is.True);
        }

        // Fails today: TrimEnd turns "/" into "" and neither the root nor the list match it
        [Test]
        public void IsCriticalPath_FilesystemRoot() => Assert.That(PathGuard.IsCriticalPath("/"), Is.True);

        [TestCase("/srv/roms/snes")]
        [TestCase("/home/user/roms")]
        [TestCase("/mnt/games")]
        public void IsCriticalPath_GameFolders_AreNot(string path)
        {
            Assert.That(PathGuard.IsCriticalPath(path), Is.False);
        }

        [TestCase("C:\\")]
        [TestCase("C:\\Windows")]
        public void IsCriticalPath_WindowsFolders(string path)
        {
            if (!System.OperatingSystem.IsWindows()) Assert.Ignore("Windows paths only resolve on Windows");
            Assert.That(PathGuard.IsCriticalPath(path), Is.True);
        }
    }
}
