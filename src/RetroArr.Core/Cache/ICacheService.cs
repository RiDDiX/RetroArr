using System;
using System.Threading.Tasks;

namespace RetroArr.Core.Cache
{
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : class;
        Task RemoveAsync(string key);
        Task RemoveByPrefixAsync(string prefix);
        Task FlushAsync();
        bool IsEnabled { get; }
    }
}
