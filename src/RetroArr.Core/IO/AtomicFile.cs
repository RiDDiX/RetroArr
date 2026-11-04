using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Core.IO
{
    // Write beside the target, then move over it, so a kill mid-write leaves the old file
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            File.Move(tmp, path, true);
        }

        public static async Task WriteAllTextAsync(string path, string contents, CancellationToken ct = default)
        {
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, contents, ct);
            File.Move(tmp, path, true);
        }
    }
}
