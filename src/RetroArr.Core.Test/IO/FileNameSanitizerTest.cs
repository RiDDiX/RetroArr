using NUnit.Framework;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.IO
{
    [TestFixture]
    public class FileNameSanitizerTest
    {
        [Test]
        public void Sanitize_ReplacesIllegalColonWithDash_TheReportedBug()
        {
            // "Diablo II: Resurrected" must not keep the ':' (illegal on SMB/NTFS,
            // which mangles it to an 8.3 short name like "DTWOE3~0"). The colon
            // becomes a readable " - " separator.
            Assert.That(FileNameSanitizer.Sanitize("Diablo II: Resurrected"),
                Is.EqualTo("Diablo II - Resurrected"));
        }

        [TestCase("A:B", "A - B")]
        [TestCase("What? Now!", "What - Now!")]
        [TestCase("a<b>c:d\"e|f?g*h", "a - b - c - d - e - f - g - h")]
        [TestCase("name/with\\seps", "name - with - seps")]
        [TestCase("a<>b", "a - b")] // a run of illegals collapses to one dash
        public void Sanitize_ReplacesIllegalRunsWithDash(string input, string expected)
        {
            Assert.That(FileNameSanitizer.Sanitize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_KeepsLegitimateHyphen()
        {
            // A real hyphen must survive (only spaces *adjacent to an illegal char*
            // are consumed).
            Assert.That(FileNameSanitizer.Sanitize("Spider-Man: Miles Morales"),
                Is.EqualTo("Spider-Man - Miles Morales"));
        }

        [Test]
        public void Sanitize_TrimsTrailingDotsAndSpaces()
        {
            Assert.That(FileNameSanitizer.Sanitize("Trailing dots..."), Is.EqualTo("Trailing dots"));
            Assert.That(FileNameSanitizer.Sanitize("Trailing space   "), Is.EqualTo("Trailing space"));
        }

        [TestCase("CON")]
        [TestCase("nul")]
        [TestCase("COM1")]
        [TestCase("LPT9")]
        public void Sanitize_EscapesReservedDeviceNames(string reserved)
        {
            Assert.That(FileNameSanitizer.Sanitize(reserved), Is.EqualTo("_" + reserved));
        }

        [TestCase("", "unknown")]
        [TestCase("   ", "unknown")]
        [TestCase(":::", "unknown")]
        [TestCase(null, "unknown")]
        public void Sanitize_EmptyOrAllIllegal_FallsBack(string? input, string expected)
        {
            Assert.That(FileNameSanitizer.Sanitize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Sanitize_LeavesValidNameUntouched()
        {
            Assert.That(FileNameSanitizer.Sanitize("Gran Turismo 7 (2022) [PS5]"),
                Is.EqualTo("Gran Turismo 7 (2022) [PS5]"));
        }
    }
}
