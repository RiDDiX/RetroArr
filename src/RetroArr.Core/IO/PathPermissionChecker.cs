using System;
using System.IO;
using System.Runtime.InteropServices;

namespace RetroArr.Core.IO
{
    public class PathPermissionResult
    {
        public string Key { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public bool Readable { get; set; }
        public bool Writable { get; set; }
        public string? Hint { get; set; }
    }

    public class PathPermissionsReport
    {
        public int? ProcessUid { get; set; }
        public int? ProcessGid { get; set; }
        public string? PuidEnv { get; set; }
        public string? PgidEnv { get; set; }
        public System.Collections.Generic.List<PathPermissionResult> Checks { get; set; } = new();
    }

    public static class PathPermissionChecker
    {
        [DllImport("libc", EntryPoint = "getuid")]
        private static extern uint Linux_getuid();

        [DllImport("libc", EntryPoint = "getgid")]
        private static extern uint Linux_getgid();

        public static int? CurrentUid()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;
            try { return (int)Linux_getuid(); } catch { return null; }
        }

        public static int? CurrentGid()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;
            try { return (int)Linux_getgid(); } catch { return null; }
        }

        public static PathPermissionResult Check(string key, string? path)
        {
            var result = new PathPermissionResult { Key = key, Path = path ?? string.Empty };
            if (string.IsNullOrWhiteSpace(path))
            {
                result.Hint = "Not configured.";
                return result;
            }

            try { result.Exists = Directory.Exists(path) || File.Exists(path); }
            catch { result.Exists = false; }

            if (!result.Exists)
            {
                result.Hint = "Folder does not exist. RetroArr will try to create it on first use.";
                return result;
            }

            try
            {
                Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext();
                result.Readable = true;
            }
            catch (UnauthorizedAccessException) { result.Readable = false; }
            catch (Exception) { result.Readable = false; }

            try
            {
                var probe = Path.Combine(path, $".retroarr-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                result.Writable = true;
            }
            catch (UnauthorizedAccessException) { result.Writable = false; }
            catch (Exception) { result.Writable = false; }

            if (!result.Readable || !result.Writable)
            {
                var puid = Environment.GetEnvironmentVariable("PUID");
                var pgid = Environment.GetEnvironmentVariable("PGID");
                var uid = CurrentUid()?.ToString() ?? puid ?? "<unknown>";
                var gid = CurrentGid()?.ToString() ?? pgid ?? "<unknown>";
                result.Hint = $"Process UID={uid}, GID={gid} cannot {(!result.Readable ? "read" : "write")} this path. On the host run: chown -R {uid}:{gid} \"{path}\" && chmod -R u+rwX \"{path}\"";
            }

            return result;
        }
    }
}
