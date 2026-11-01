using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace RetroArr.Core.Download
{
    // RetroArr fetches the NZB itself and hands NZBGet and SABnzbd the file: both accept anything they are
    // given and only fail the job later in their history, so an indexer's error page or login form would
    // count as a grab
    internal static class NzbFile
    {
        public static async Task<(byte[] Content, string Filename)> FetchAsync(string nzbUrl)
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(nzbUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsByteArrayAsync();
            EnsureNzb(content);
            return (content, BuildFilename(nzbUrl, response.Content.Headers.ContentDisposition));
        }

        private static void EnsureNzb(byte[] content)
        {
            XElement? root = null;
            try
            {
                // Real NZBs carry a DOCTYPE; ignored, never fetched
                using var reader = XmlReader.Create(new MemoryStream(content), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                root = XDocument.Load(reader).Root;
            }
            catch (XmlException) { }

            if (root?.Name.LocalName == "error")
                throw new InvalidDataException($"indexer error {(string?)root.Attribute("code")}: {(string?)root.Attribute("description")}");
            if (root?.Name.LocalName != "nzb" || !root.Elements().Any(e => e.Name.LocalName == "file"))
                throw new InvalidDataException("the indexer sent no NZB");
        }

        // Both clients name the job after the file name and NZBGet runs its duplicate check on that name, so
        // every job needs its release name: the NZB response's file name (Prowlarr and most indexers send one),
        // else Prowlarr's file= parameter, else a *.nzb URL path segment, else something unique.
        internal static string BuildFilename(string nzbUrl, ContentDispositionHeaderValue? disposition)
        {
            var name = disposition?.FileNameStar ?? disposition?.FileName;
            if (string.IsNullOrWhiteSpace(name) && Uri.TryCreate(nzbUrl, UriKind.Absolute, out var uri))
            {
                name = HttpUtility.ParseQueryString(uri.Query)["file"];
                var segment = Uri.UnescapeDataString(uri.Segments[^1]);
                if (string.IsNullOrWhiteSpace(name) && segment.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                    name = segment;
            }

            name = Path.GetFileName((name ?? string.Empty).Trim().Trim('"').Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)) name = name[..^4].TrimEnd();
            // NZBGet stores the file and later a folder under this name, keep it well below the 255 byte limit
            while (Encoding.UTF8.GetByteCount(name) > 200) name = name[..^1];
            name = name.TrimEnd();
            if (name.Length == 0) name = "RetroArr_" + Guid.NewGuid().ToString("N");
            return name + ".nzb";
        }
    }
}
