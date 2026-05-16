using System;
using NUnit.Framework;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class ProxyConfiguratorTest
    {
        private readonly ProxyConfigurator _sut = ProxyConfigurator.Instance;

        [SetUp]
        public void Reset()
        {
            _sut.Update(null);
        }

        [Test]
        public void Disabled_GoesDirect()
        {
            _sut.Update(new ProxySettings { Enabled = false, Host = "127.0.0.1", Port = 8080 });

            var dest = new Uri("https://example.com");
            Assert.That(_sut.IsBypassed(dest), Is.True);
            Assert.That(_sut.GetProxy(dest), Is.EqualTo(dest));
        }

        [Test]
        public void HttpEnabled_ReturnsHttpProxyUri()
        {
            _sut.Update(new ProxySettings { Enabled = true, Type = "http", Host = "proxy.local", Port = 3128 });

            var proxy = _sut.GetProxy(new Uri("https://example.com"));
            Assert.That(proxy, Is.EqualTo(new Uri("http://proxy.local:3128")));
        }

        [Test]
        public void Socks5Enabled_ReturnsSocks5ProxyUri()
        {
            _sut.Update(new ProxySettings { Enabled = true, Type = "socks5", Host = "10.0.0.2", Port = 1080 });

            var proxy = _sut.GetProxy(new Uri("https://example.com"));
            Assert.That(proxy, Is.EqualTo(new Uri("socks5://10.0.0.2:1080")));
        }

        [Test]
        public void BypassLocal_LoopbackGoesDirect()
        {
            _sut.Update(new ProxySettings { Enabled = true, Host = "proxy.local", Port = 3128, BypassLocal = true });

            Assert.That(_sut.IsBypassed(new Uri("http://127.0.0.1:5002")), Is.True);
            Assert.That(_sut.IsBypassed(new Uri("https://example.com")), Is.False);
        }

        [Test]
        public void BypassList_MatchingHostGoesDirect()
        {
            var settings = new ProxySettings { Enabled = true, Host = "proxy.local", Port = 3128, BypassLocal = false };
            settings.BypassList.Add("example.com");
            _sut.Update(settings);

            Assert.That(_sut.IsBypassed(new Uri("https://api.example.com")), Is.True);
            Assert.That(_sut.IsBypassed(new Uri("https://other.org")), Is.False);
        }

        [Test]
        public void EnabledThenDisabled_LiveSwitchesBackToDirect()
        {
            _sut.Update(new ProxySettings { Enabled = true, Host = "proxy.local", Port = 3128 });
            Assert.That(_sut.GetProxy(new Uri("https://example.com")), Is.EqualTo(new Uri("http://proxy.local:3128")));

            _sut.Update(new ProxySettings { Enabled = false });
            var dest = new Uri("https://example.com");
            Assert.That(_sut.GetProxy(dest), Is.EqualTo(dest));
        }
    }
}
