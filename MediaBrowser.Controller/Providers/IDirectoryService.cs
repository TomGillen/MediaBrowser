using System.Collections.Generic;
using System.IO;
using CommonIO;
using MediaBrowser.Common.IO;

namespace MediaBrowser.Controller.Providers
{
    public interface IDirectoryService
    {
        IEnumerable<FileSystemMetadata> GetFileSystemEntries(string path);
        IEnumerable<FileSystemMetadata> GetFiles(string path);
        IEnumerable<FileSystemMetadata> GetDirectories(string path);
        IEnumerable<FileSystemMetadata> GetFiles(string path, bool clearCache);
        FileSystemMetadata GetFile(string path);
        Dictionary<string, FileSystemMetadata> GetFileSystemDictionary(string path);
    }
}