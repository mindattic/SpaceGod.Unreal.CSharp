using System;
using System.IO;
using System.Text;

namespace Unity.Shared.Core
{
    /// <summary>
    /// Writes a file so a crash or power cut mid-write leaves either the old file or the new one, never half
    /// of one: the bytes go to <c>&lt;path&gt;.tmp</c> first, which then replaces the file in one step.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>The suffix of the temporary file a write goes through.</summary>
        public const string TempSuffix = ".tmp";

        static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Writes <paramref name="text"/> as UTF-8 (no BOM) to <paramref name="path"/>, atomically.</summary>
        public static void WriteAllText(string path, string text) => WriteAllBytes(path, Utf8NoBom.GetBytes(text ?? ""));

        /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/>, atomically. The folder is made if needed.</summary>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temp = full + TempSuffix;
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (!File.Exists(full))
                {
                    File.Move(temp, full);
                    return;
                }
                try
                {
                    // One step: deleting the old file first would leave none at all if the move then failed.
                    File.Replace(temp, full, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temp, full, true);
                    File.Delete(temp);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
