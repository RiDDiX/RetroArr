using System.IO;
using NUnit.Framework;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class DatabaseSettingsRoundtripTest
    {
        private string _contentRoot = null!;
        private string _configDir = null!;

        [SetUp]
        public void SetUp()
        {
            _contentRoot = Path.Combine(Path.GetTempPath(), "retroarr_dbcfg_" + Path.GetRandomFileName());
            // ConfigurationService prefers <contentRoot>/config when it exists,
            // otherwise it silently falls back to ~/.config/RetroArr/config -
            // which would contaminate the developer's live install. Pre-creating
            // the dir pins the service to our temp tree.
            _configDir = Path.Combine(_contentRoot, "config");
            Directory.CreateDirectory(_configDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_contentRoot))
                Directory.Delete(_contentRoot, recursive: true);
        }

        [Test]
        public void SaveAndLoad_PasswordSurvivesRoundTrip()
        {
            var protector = new SecretProtector(Path.Combine(_contentRoot, "keys"));
            var svc = new ConfigurationService(_contentRoot, protector);

            svc.SaveDatabaseSettings(new DatabaseSettings
            {
                Type = DatabaseType.PostgreSQL,
                Host = "pg.example.org",
                Port = 5432,
                Database = "retroarr",
                Username = "ra_user",
                Password = "s3cret!"
            });

            var loaded = svc.LoadDatabaseSettings();

            Assert.That(loaded.Password, Is.EqualTo("s3cret!"));
            Assert.That(loaded.Username, Is.EqualTo("ra_user"));
            Assert.That(loaded.Type, Is.EqualTo(DatabaseType.PostgreSQL));
        }

        [Test]
        public void SavedFile_DoesNotContainPlainPassword()
        {
            var protector = new SecretProtector(Path.Combine(_contentRoot, "keys"));
            var svc = new ConfigurationService(_contentRoot, protector);

            const string plainPassword = "P@ssw0rd-on-disk";
            svc.SaveDatabaseSettings(new DatabaseSettings
            {
                Type = DatabaseType.PostgreSQL,
                Host = "h",
                Database = "d",
                Username = "u",
                Password = plainPassword
            });

            var diskJson = File.ReadAllText(Path.Combine(_configDir, "database.json"));

            // The whole point of encrypting at rest: the on-disk file must not
            // contain the cleartext password. The DataProtection wrapper leaves
            // an "__enc__:" marker we can also assert on.
            Assert.That(diskJson, Does.Not.Contain(plainPassword));
            Assert.That(diskJson, Does.Contain("__enc__:"));
        }

        [Test]
        public void Load_WithoutProtector_TreatsExistingPlainPasswordAsIs()
        {
            // Backwards compat: if a user had pre-encrypt database.json with a
            // plain password, loading without a protector must not blow up - it
            // returns the plain string so the migration path keeps working.
            var path = Path.Combine(_configDir, "database.json");
            File.WriteAllText(path,
                "{\"Type\":1,\"Host\":\"h\",\"Database\":\"d\",\"Username\":\"u\",\"Password\":\"plain\"}");

            var svc = new ConfigurationService(_contentRoot);
            var loaded = svc.LoadDatabaseSettings();

            Assert.That(loaded.Password, Is.EqualTo("plain"));
        }
    }
}
