using System;
using System.Threading.Tasks;

namespace RetroArr.Core.Cache
{
    public class NullCacheService : ICacheService
    {
        public bool IsEnabled => false;

        public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class => Task.CompletedTask;

        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : class
            => await factory();

        public Task RemoveAsync(string key) => Task.CompletedTask;

        public Task RemoveByPrefixAsync(string prefix) => Task.CompletedTask;

        public Task FlushAsync() => Task.CompletedTask;
    }
}
