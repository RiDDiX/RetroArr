using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class ImportStatusServiceTest
    {
        [Test]
        public void MarkImporting_SecondCallForTheSameKey_ReturnsFalseUntilFinished()
        {
            var status = new ImportStatusService();

            Assert.That(status.MarkImporting(1, "abc"), Is.True);
            Assert.That(status.MarkImporting(1, "abc"), Is.False);
            Assert.That(status.MarkImporting(2, "abc"), Is.True);

            status.MarkFinished(1, "abc");
            Assert.That(status.MarkImporting(1, "abc"), Is.True);
        }
    }
}
