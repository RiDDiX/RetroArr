using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class CacheControllerTest
    {
        private const string Placeholder = "••••••••";
        private const string Stored = "redis:6379,password=hunter2,ssl=true";
        private string _root = null!;
        private ConfigurationService _config = null!;

        private sealed class FakeCache : ICacheService
        {
            public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
            public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class => Task.CompletedTask;
            public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : class => factory();
            public Task RemoveAsync(string key) => Task.CompletedTask;
            public Task RemoveByPrefixAsync(string prefix) => Task.CompletedTask;
            public Task FlushAsync() => Task.CompletedTask;
            public bool IsEnabled => false;
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_cache_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _config = new ConfigurationService(_root);
            _config.SaveCacheSettings(new CacheSettings { Enabled = true, ConnectionString = Stored });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private CacheController Controller() => new(_config, new FakeCache());

        [Test]
        public void Get_MasksThePassword()
        {
            var response = (CacheSettingsResponse)((OkObjectResult)Controller().GetSettings().Result!).Value!;
            Assert.That(response.ConnectionString, Is.EqualTo("redis:6379,password=" + Placeholder + ",ssl=true"));
            Assert.That(response.ConnectionString, Does.Not.Contain("hunter2"));
        }

        [Test]
        public void Put_PlaceholderWithSameAddress_KeepsTheStoredString()
        {
            var result = Controller().SaveSettings(new CacheSettingsRequest { Enabled = true, ConnectionString = "redis:6379,password=" + Placeholder + ",ssl=true" });
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(_config.LoadCacheSettings().ConnectionString, Is.EqualTo(Stored));
        }

        [Test]
        public void Put_PlaceholderWithChangedHost_IsRefused()
        {
            var result = Controller().SaveSettings(new CacheSettingsRequest { Enabled = true, ConnectionString = "evil.example:6379,password=" + Placeholder + ",ssl=true" });
            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(_config.LoadCacheSettings().ConnectionString, Is.EqualTo(Stored));
        }
    }
}
