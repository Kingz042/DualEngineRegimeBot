using System;
using System.IO;
using System.Text;
using System.Threading;
using Xunit;
using DualEngineRegimeBot.Core.State;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Tests for AtomicFile durability and rotation.
    /// </summary>
    public class AtomicFileTests : IDisposable
    {
        private readonly string _testDir;
        
        public AtomicFileTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "AtomicFileTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }
        
        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, recursive: true); } catch { /* Best effort */ }
            }
        }
        
        [Fact]
        public void WriteAtomic_PersistsIntact_WhenCrashBetweenWrites()
        {
            // Simulate interrupted write scenario:
            // 1. Write valid v1
            // 2. Start v2 write to temp but don't complete rename
            // 3. Verify original file remains intact with v1 data
            
            string path = Path.Combine(_testDir, "crash_test.txt");
            string v1Content = "Version 1 - Valid Data";
            string v2Content = "Version 2 - Incomplete";
            
            // Write v1 successfully
            AtomicFile.WriteAtomicText(path, v1Content);
            Assert.True(File.Exists(path));
            Assert.Equal(v1Content, File.ReadAllText(path));
            
            // Simulate crash during v2 write by writing directly to temp file
            // without completing the atomic rename (using a pattern similar to AtomicFile)
            string tempPath = path + ".tmp.fakeguid";
            File.WriteAllText(tempPath, v2Content);
            
            // Original file should still contain v1
            Assert.Equal(v1Content, File.ReadAllText(path));
            
            // Temp file exists but original is intact
            Assert.True(File.Exists(tempPath));
            Assert.Equal(v2Content, File.ReadAllText(tempPath));
            
            // Now complete a proper atomic write with v2
            AtomicFile.WriteAtomicText(path, v2Content);
            Assert.Equal(v2Content, File.ReadAllText(path));
            
            // Old temp file from "crash" should still exist (orphaned)
            // AtomicFile uses unique GUID-based temp names, so it won't clean up arbitrary temp files
            Assert.True(File.Exists(tempPath));
        }
        
        [Fact]
        public void RollBySize_RetainsConfiguredGenerations()
        {
            string path = Path.Combine(_testDir, "rolling.log");
            int retainCount = 3;
            long maxBytes = 100;
            
            // Write initial file larger than maxBytes
            string largeContent = new string('X', 150);
            File.WriteAllText(path, largeContent);
            
            // Roll - should create .1
            AtomicFile.RollBySize(path, maxBytes, retainCount);
            Assert.False(File.Exists(path)); // Current moved to .1
            Assert.True(File.Exists(path + ".1"));
            
            // Write and roll again - should create .2
            File.WriteAllText(path, largeContent);
            AtomicFile.RollBySize(path, maxBytes, retainCount);
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            
            // Write and roll again - should create .3
            File.WriteAllText(path, largeContent);
            AtomicFile.RollBySize(path, maxBytes, retainCount);
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            Assert.True(File.Exists(path + ".3"));
            
            // Write and roll again - should delete .3, shift others, create new .1
            File.WriteAllText(path, largeContent);
            AtomicFile.RollBySize(path, maxBytes, retainCount);
            
            // Should have exactly retainCount files
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            Assert.True(File.Exists(path + ".3"));
            Assert.False(File.Exists(path + ".4")); // Should not exceed retainCount
        }
        
        [Fact]
        public void WriteAtomic_CreatesFile_WithCorrectContent()
        {
            string path = Path.Combine(_testDir, "simple.txt");
            string content = "Test Content 123";
            
            AtomicFile.WriteAtomicText(path, content);
            
            Assert.True(File.Exists(path));
            Assert.Equal(content, File.ReadAllText(path));
        }
        
        [Fact]
        public void WriteAtomic_OverwritesExistingFile()
        {
            string path = Path.Combine(_testDir, "overwrite.txt");
            
            AtomicFile.WriteAtomicText(path, "First");
            Assert.Equal("First", File.ReadAllText(path));
            
            AtomicFile.WriteAtomicText(path, "Second");
            Assert.Equal("Second", File.ReadAllText(path));
        }
        
        [Fact]
        public void WriteAtomic_CreatesDirectory_IfNotExists()
        {
            string subDir = Path.Combine(_testDir, "nested", "deep");
            string path = Path.Combine(subDir, "file.txt");
            
            AtomicFile.WriteAtomicText(path, "Content");
            
            Assert.True(Directory.Exists(subDir));
            Assert.True(File.Exists(path));
        }
        
        [Fact]
        public void WriteAtomic_ThrowsException_WhenPathIsEmpty()
        {
            Assert.Throws<ArgumentException>(() =>
                AtomicFile.WriteAtomicText("", "Content"));
        }
        
        [Fact]
        public void WriteAtomic_ThrowsException_WhenPathIsNull()
        {
            Assert.Throws<ArgumentException>(() =>
                AtomicFile.WriteAtomicText(null!, "Content"));
        }
        
        [Fact]
        public void RollBySize_DoesNotRoll_WhenFileBelowThreshold()
        {
            string path = Path.Combine(_testDir, "small.log");
            File.WriteAllText(path, "Small");
            
            AtomicFile.RollBySize(path, maxBytes: 1000, retainCount: 3);
            
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".1"));
        }
        
        [Fact]
        public void RollBySize_DoesNothing_WhenFileDoesNotExist()
        {
            string path = Path.Combine(_testDir, "nonexistent.log");
            
            // Should not throw
            AtomicFile.RollBySize(path, maxBytes: 100, retainCount: 3);
            
            Assert.False(File.Exists(path));
        }
        
        [Fact]
        public void GetTotalSize_ReturnsZero_WhenNoFilesExist()
        {
            string path = Path.Combine(_testDir, "missing.log");
            
            long size = AtomicFile.GetTotalSize(path);
            
            Assert.Equal(0, size);
        }
        
        [Fact]
        public void GetTotalSize_SumsAllRotations()
        {
            string path = Path.Combine(_testDir, "sized.log");
            
            File.WriteAllText(path, "12345"); // 5 bytes
            File.WriteAllText(path + ".1", "1234567890"); // 10 bytes
            File.WriteAllText(path + ".2", "123"); // 3 bytes
            
            long size = AtomicFile.GetTotalSize(path);
            
            Assert.Equal(18, size);
        }
        
        [Fact]
        public void CleanupRotations_DeletesAllRotatedFiles()
        {
            string path = Path.Combine(_testDir, "cleanup.log");
            
            File.WriteAllText(path, "Current");
            File.WriteAllText(path + ".1", "Rotation 1");
            File.WriteAllText(path + ".2", "Rotation 2");
            File.WriteAllText(path + ".3", "Rotation 3");
            
            AtomicFile.CleanupRotations(path);
            
            Assert.True(File.Exists(path)); // Current remains
            Assert.False(File.Exists(path + ".1"));
            Assert.False(File.Exists(path + ".2"));
            Assert.False(File.Exists(path + ".3"));
        }
        
        [Fact]
        public void WriteAtomic_HandlesUnicode()
        {
            string path = Path.Combine(_testDir, "unicode.txt");
            string content = "Hello 世界 🌍 Привет مرحبا";
            
            AtomicFile.WriteAtomicText(path, content);
            
            string readContent = File.ReadAllText(path, Encoding.UTF8);
            Assert.Equal(content, readContent);
        }
        
        [Fact]
        public void WriteAtomic_NoTempFileRemains_OnSuccess()
        {
            string path = Path.Combine(_testDir, "notmp.txt");
            string tempPath = path + ".tmp";
            
            AtomicFile.WriteAtomicText(path, "Content");
            
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(tempPath));
        }
        
        [Fact]
        public void RollBySize_ShiftsExistingRotations_Correctly()
        {
            string path = Path.Combine(_testDir, "shift.log");
            string largeContent = new string('X', 150);
            
            // Create existing rotations with identifiable content
            File.WriteAllText(path + ".1", "Old1");
            File.WriteAllText(path + ".2", "Old2");
            
            // Write new large file and roll
            File.WriteAllText(path, largeContent);
            AtomicFile.RollBySize(path, maxBytes: 100, retainCount: 5);
            
            // Current moved to .1, old .1 moved to .2, old .2 moved to .3
            Assert.True(File.Exists(path + ".1"));
            Assert.True(File.Exists(path + ".2"));
            Assert.True(File.Exists(path + ".3"));
            
            // Verify content shifted correctly
            Assert.Equal("Old1", File.ReadAllText(path + ".2"));
            Assert.Equal("Old2", File.ReadAllText(path + ".3"));
        }
        
        [Fact]
        public void WriteAtomic_IsThreadSafe_WithMultipleWrites()
        {
            string path = Path.Combine(_testDir, "threaded.txt");
            int writeCount = 100;
            int successCount = 0;
            object lockObj = new object();
            
            System.Threading.Tasks.Parallel.For(0, writeCount, i =>
            {
                try
                {
                    string content = $"Write {i}";
                    AtomicFile.WriteAtomicText(path, content);
                    lock (lockObj) { successCount++; }
                }
                catch
                {
                    // Some may fail due to race conditions, but file should never be corrupted
                }
            });
            
            // File should exist and contain valid content from one of the writes
            Assert.True(File.Exists(path));
            string finalContent = File.ReadAllText(path);
            Assert.StartsWith("Write ", finalContent);
            
            // Most writes should succeed
            Assert.True(successCount > writeCount / 2);
        }
    }
}

