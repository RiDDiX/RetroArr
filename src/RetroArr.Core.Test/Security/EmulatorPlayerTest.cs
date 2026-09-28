using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RetroArr.Api.V3.Emulator;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class EmulatorPlayerTest
    {
        private static EmulatorController Controller()
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("192.168.1.10", 2727);
            // The player doesn't touch the database or config
            return new EmulatorController(null!, null!, null!) { ControllerContext = new ControllerContext { HttpContext = context } };
        }

        private static async Task<(string Html, string Csp)> Page(string rom, string core, string title)
        {
            var controller = Controller();
            var result = await controller.GetEmulatorPlayer(rom, core, title);
            Assert.That(result, Is.InstanceOf<ContentResult>(), (result as ObjectResult)?.Value?.ToString());
            return (((ContentResult)result).Content!, controller.Response.Headers["Content-Security-Policy"].ToString());
        }

        private static string JsValue(string html, string name)
        {
            var m = Regex.Match(html, name + @" = (.*);");
            Assert.That(m.Success, Is.True, name);
            return JsonSerializer.Deserialize<string>(m.Groups[1].Value)!;
        }

        [TestCase(@"\';alert(document.domain)//")]
        [TestCase("</script><script>alert(1)</script>")]
        [TestCase("Zelda \"Link's\" <Awakening> & more \u2028 end")]
        public async Task Title_StaysAStringLiteral(string title)
        {
            var (html, _) = await Page("/api/v3/emulator/5/rom", "snes", title);

            Assert.That(JsValue(html, "EJS_gameName"), Is.EqualTo(title));
            Assert.That(html, Does.Not.Contain("<script>alert"));
            Assert.That(Regex.Matches(html, "</script>").Count, Is.EqualTo(2), "only our own script blocks close");
            Assert.That(html, Does.Contain("<title>" + System.Net.WebUtility.HtmlEncode(title) + "</title>"));
        }

        [Test]
        public async Task SignedRom_PassesThrough()
        {
            const string rom = "/api/v3/emulator/5/rom?exp=1790000000&sig=Ab-_9z";
            var (html, _) = await Page(rom, "segaMD", "Sonic");
            Assert.That(JsValue(html, "EJS_gameUrl"), Is.EqualTo(rom));
            Assert.That(JsValue(html, "EJS_core"), Is.EqualTo("segaMD"));
        }

        [Test]
        public async Task AbsoluteRomLink_IsCutToThePath()
        {
            var (html, _) = await Page("https://retroarr.example/api/v3/emulator/7/rom", "nes", "x");
            Assert.That(JsValue(html, "EJS_gameUrl"), Is.EqualTo("/api/v3/emulator/7/rom"));
        }

        [TestCase(@"/api/v3/emulator/5/rom\';alert(1)//", "nes")]
        [TestCase("/api/v3/emulator/5/rom?x='", "nes")]
        [TestCase("/api/v3/emulator/5/rom</script>", "nes")]
        [TestCase("/api/v3/emulator/5/rom\n", "nes")]
        [TestCase("https://evil.example/payload.js", "nes")]
        [TestCase("http://[broken", "nes")]
        [TestCase("javascript:alert(1)", "nes")]
        [TestCase("/api/v3/emulator/5/rom", "nes';alert(1)//")]
        [TestCase("/api/v3/emulator/5/rom", "</script><script>alert(1)</script>")]
        [TestCase("/api/v3/emulator/5/rom", "nes\\")]
        public async Task UnexpectedRomOrCore_IsRefused(string rom, string core)
        {
            var result = await Controller().GetEmulatorPlayer(rom, core, "Game");
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task Page_SendsAScriptPolicy_WithTheNonceOfItsOwnScripts()
        {
            var (html, csp) = await Page("/api/v3/emulator/5/rom", "nes", "x");
            var nonce = Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;

            Assert.That(nonce, Is.Not.Empty);
            Assert.That(csp, Does.Contain("script-src 'self'").And.Contain("object-src 'none'").And.Contain("base-uri 'none'"));
            Assert.That(csp, Does.Not.Contain("unsafe-inline"));
            // EmulatorJS evaluates strings; without it the core never starts (seen in a real browser)
            Assert.That(csp, Does.Contain("'unsafe-eval'").And.Contain("'wasm-unsafe-eval'").And.Contain("blob:"));
            Assert.That(Regex.Matches(html, "<script").Count, Is.EqualTo(Regex.Matches(html, $"<script nonce=\"{Regex.Escape(nonce)}\"").Count));
        }

        [Test]
        public async Task ThreadedCoreOverPlainHttp_ErrorPage_EncodesTheTitle()
        {
            var controller = Controller();
            var result = (ContentResult)await controller.GetEmulatorPlayer("/api/v3/emulator/5/rom", "psp", "<img src=x onerror=alert(1)>");
            Assert.That(result.Content, Does.Not.Contain("<img"));
            Assert.That(controller.Response.Headers["Content-Security-Policy"].ToString(), Does.Contain("default-src 'none'"));
        }
    }
}
