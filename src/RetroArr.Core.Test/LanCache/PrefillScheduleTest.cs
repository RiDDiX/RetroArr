using System;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.LanCache;

namespace RetroArr.Core.Test.LanCache
{
    [TestFixture]
    public class PrefillScheduleTest
    {
        private static DateTime Local(int y, int mo, int d, int h, int mi) =>
            new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Local);

        [Test]
        public void ComputeNextRun_LaterToday_WhenStartIsAfterNow()
        {
            var s = new PrefillSchedule { Enabled = true, StartTime = "04:00" };
            var next = PrefillSchedulerService.ComputeNextRun(s, Local(2026, 6, 10, 2, 0));
            Assert.That(next, Is.EqualTo(Local(2026, 6, 10, 4, 0)));
        }

        [Test]
        public void ComputeNextRun_Tomorrow_WhenStartAlreadyPassed()
        {
            var s = new PrefillSchedule { Enabled = true, StartTime = "04:00" };
            var next = PrefillSchedulerService.ComputeNextRun(s, Local(2026, 6, 10, 5, 0));
            Assert.That(next, Is.EqualTo(Local(2026, 6, 11, 4, 0)));
        }

        [Test]
        public void ComputeNextRun_Null_WhenDisabled()
        {
            var s = new PrefillSchedule { Enabled = false, StartTime = "04:00" };
            Assert.That(PrefillSchedulerService.ComputeNextRun(s, Local(2026, 6, 10, 2, 0)), Is.Null);
        }

        [Test]
        public void ComputeNextRun_RespectsSpecificWeekday()
        {
            // 2026-06-10 is a Wednesday (DayOfWeek 3). Only Monday (1) is allowed.
            var s = new PrefillSchedule { Enabled = true, StartTime = "04:00" };
            s.Days.Add(1);
            var next = PrefillSchedulerService.ComputeNextRun(s, Local(2026, 6, 10, 2, 0));
            Assert.That(next, Is.EqualTo(Local(2026, 6, 15, 4, 0))); // next Monday
        }

        [Test]
        public void Time_LegacyField_FillsStartTime()
        {
            var s = new PrefillSchedule();
            s.Time = "07:30";
            Assert.That(s.StartTime, Is.EqualTo("07:30"));
        }

        [Test]
        public void ComputeNextRun_Null_OnBadTime()
        {
            var s = new PrefillSchedule { Enabled = true, StartTime = "not-a-time" };
            Assert.That(PrefillSchedulerService.ComputeNextRun(s, Local(2026, 6, 10, 2, 0)), Is.Null);
        }
    }
}
