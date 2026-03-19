using System.Collections.Generic;
using System.Threading.Tasks;
using RetroArr.Core.Prowlarr; // For SearchResult

namespace RetroArr.Core.Indexers
{
    public interface IIndexerClient
    {
        Task<List<SearchResult>> SearchAsync(string query, int[]? categories = null);
        Task<bool> TestConnectionAsync();
    }
}
