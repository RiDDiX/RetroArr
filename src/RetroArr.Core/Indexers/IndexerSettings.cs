using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Indexers
{
    public class IndexerSettings
    {
        public int Id { get; set; }
        
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string ProwlarrUrl { get; set; } = string.Empty;
        
        public string ApiKey { get; set; } = string.Empty;
        public bool SyncEnabled { get; set; } = true;

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<int> EnabledIndexers { get; set; } = new();
    }
}
