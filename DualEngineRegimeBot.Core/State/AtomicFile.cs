using System;
using System.IO;
using System.Linq;

namespace DualEngineRegimeBot.Core.State
{
    /// <summary>
    /// Provides atomic file write operations with rotation support.
    /// Ensures data integrity via write-flush-rename pattern.
    /// </summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Writes data atomically to a file using write-to-temp-then-rename pattern.
        /// Ensures data is flushed to disk before rename for crash safety.
        /// Uses WriteThrough and Flush(true) for hard durability guarantees.
        /// </summary>
        /// <param name="path">Target file path.</param>
        /// <param name="data">Data to write.</param>
        public static void WriteAtomic(string path, ReadOnlySpan<byte> data)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }
            
            // Use unique temp file name to support concurrent writes from multiple threads
            string tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
            
            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Write to temp file with WriteThrough for OS-level write-through cache
                // and explicit Flush(true) to guarantee disk persistence before rename
                using (var fileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough))
                {
                    // Write data directly to file stream
                    fileStream.Write(data);
                    
                    // Hard flush to disk before rename (critical for durability)
                    fileStream.Flush(flushToDisk: true);
                }
                
                // Atomic rename with File.Replace on Windows for robustness
                // If interrupted between write and rename, old file remains intact
                // Retry logic to handle concurrent access from multiple threads
                int maxRetries = 3;
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            // File.Replace with null backup means replace is atomic
                            // ignoreMetadataErrors: false ensures full durability
                            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: false);
                        }
                        else
                        {
                            // First write - simple move is atomic
                            File.Move(tempPath, path);
                        }
                        break; // Success, exit retry loop
                    }
                    catch (IOException) when (attempt < maxRetries - 1)
                    {
                        // Brief delay before retry to reduce contention
                        System.Threading.Thread.Sleep(10);
                    }
                }
            }
            catch
            {
                // Clean up temp file on failure - best effort
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* Ignore cleanup errors */ }
                }
                throw;
            }
        }
        
        /// <summary>
        /// Writes text atomically to a file.
        /// </summary>
        /// <param name="path">Target file path.</param>
        /// <param name="text">Text content to write.</param>
        public static void WriteAtomicText(string path, string text)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(text);
            WriteAtomic(path, data);
        }
        
        /// <summary>
        /// Rotates log files when size exceeds threshold.
        /// Renames current file to .1, shifts existing .1 to .2, etc.
        /// Deletes files beyond retainCount.
        /// </summary>
        /// <param name="path">File path to check and rotate.</param>
        /// <param name="maxBytes">Maximum file size before rotation.</param>
        /// <param name="retainCount">Number of rotated files to keep.</param>
        public static void RollBySize(string path, long maxBytes, int retainCount)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty", nameof(path));
            }
            
            if (maxBytes <= 0)
            {
                throw new ArgumentException("Max bytes must be positive", nameof(maxBytes));
            }
            
            if (retainCount < 1)
            {
                throw new ArgumentException("Retain count must be at least 1", nameof(retainCount));
            }
            
            // Check if file needs rotation
            if (!File.Exists(path))
            {
                return;
            }
            
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length <= maxBytes)
            {
                return;
            }
            
            // Delete oldest file if it would exceed retainCount
            string oldestPath = $"{path}.{retainCount}";
            if (File.Exists(oldestPath))
            {
                File.Delete(oldestPath);
            }
            
            // Shift existing rotated files
            for (int i = retainCount - 1; i >= 1; i--)
            {
                string fromPath = $"{path}.{i}";
                string toPath = $"{path}.{i + 1}";
                
                if (File.Exists(fromPath))
                {
                    if (File.Exists(toPath))
                    {
                        File.Delete(toPath);
                    }
                    File.Move(fromPath, toPath);
                }
            }
            
            // Rotate current file to .1
            string rotatedPath = $"{path}.1";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }
            File.Move(path, rotatedPath);
        }
        
        /// <summary>
        /// Gets the total size of a file including all its rotations.
        /// </summary>
        /// <param name="path">Base file path.</param>
        /// <param name="maxRotations">Maximum number of rotations to check.</param>
        /// <returns>Total size in bytes.</returns>
        public static long GetTotalSize(string path, int maxRotations = 10)
        {
            long total = 0;
            
            if (File.Exists(path))
            {
                total += new FileInfo(path).Length;
            }
            
            for (int i = 1; i <= maxRotations; i++)
            {
                string rotatedPath = $"{path}.{i}";
                if (File.Exists(rotatedPath))
                {
                    total += new FileInfo(rotatedPath).Length;
                }
                else
                {
                    break;
                }
            }
            
            return total;
        }
        
        /// <summary>
        /// Cleans up all rotated files for a given base path.
        /// </summary>
        /// <param name="path">Base file path.</param>
        /// <param name="maxRotations">Maximum number of rotations to check.</param>
        public static void CleanupRotations(string path, int maxRotations = 10)
        {
            for (int i = 1; i <= maxRotations; i++)
            {
                string rotatedPath = $"{path}.{i}";
                if (File.Exists(rotatedPath))
                {
                    File.Delete(rotatedPath);
                }
                else
                {
                    break;
                }
            }
        }
    }
}

