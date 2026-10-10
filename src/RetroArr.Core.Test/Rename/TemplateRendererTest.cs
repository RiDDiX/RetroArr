using System.Collections.Generic;
using NUnit.Framework;
using RetroArr.Core.Rename;

namespace RetroArr.Core.Test.Rename
{
    [TestFixture]
    public class TemplateRendererTest
    {
        private const string Lacrimosa = "Ys VIII -Lacrimosa of DANA-";

        private static string Render(string template, string? title, string? region = null, string? version = null) =>
            new TemplateRenderer().RenderStem(template, new Dictionary<string, string?> { ["Title"] = title, ["Region"] = region, ["Version"] = version });

        // A dash of the title is no separator of the template
        [TestCase("{Title}", Lacrimosa, null, null, Lacrimosa)]
        [TestCase("{Title}", "-Zero-", null, null, "-Zero-")]
        [TestCase("{Title} - {Version}", Lacrimosa, null, null, Lacrimosa)]
        [TestCase("{Region} - {Title}", "-Zero-", "", null, "-Zero-")]
        [TestCase("{Title} - Update {Version}", "-Zero-", null, "", "-Zero- - Update")]
        [TestCase("{Title} - Update {Version}", "-Zero-", null, "1.02", "-Zero- - Update 1.02")]
        public void RenderStem_DashesOfTheTitle_Stay(string template, string? title, string? region, string? version, string expected)
        {
            Assert.That(Render(template, title, region, version), Is.EqualTo(expected));
        }

        // The separators next to an empty token go at either end and collapse to one in between
        [TestCase("{Title} - {Region} - {Version}", "", "1.02", "Chrono Trigger - 1.02")]
        [TestCase("{Title}-{Region}-{Version}", "", "1.02", "Chrono Trigger-1.02")]
        [TestCase("{Region} - {Title} - {Version}", null, null, "Chrono Trigger")]
        [TestCase("{Title} ({Region}) - {Version}", "", "1.02", "Chrono Trigger - 1.02")]
        [TestCase("- {Title} -", null, null, "Chrono Trigger")]
        public void RenderStem_SeparatorsNextToAnEmptyToken_Go(string template, string? region, string? version, string expected)
        {
            Assert.That(Render(template, "Chrono Trigger", region, version), Is.EqualTo(expected));
        }

        [TestCase("Diablo II: Resurrected", "Diablo II - Resurrected")]
        [TestCase("-?-", "untitled")]
        [TestCase("Game?-", "Game")]
        public void RenderStem_IllegalCharsAtTheEnds_LeaveNoDash(string title, string expected)
        {
            Assert.That(Render("{Title}", title), Is.EqualTo(expected));
        }
    }
}
