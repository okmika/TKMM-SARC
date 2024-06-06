using SarcLibrary;

namespace TKMM.SarcTool.Core;

internal class ArchiveHelper {

    private readonly ZsCompression compression;

    public ArchiveHelper(ZsCompression compression) {
        this.compression = compression;
    }

    public Span<byte> GetFileContents(string archivePath, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        Span<byte> sourceFileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var type = CompressionType.Common;

            // Change compression type
            if (isPackFile)
                type = CompressionType.Pack;
            else if (archivePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;

            var compressedContents = File.ReadAllBytes(archivePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents, type);
        } else {
            sourceFileContents = File.ReadAllBytes(archivePath).AsSpan();
        }

        return sourceFileContents;
    }

    public void WriteFileContents(string archivePath, Sarc sarc, bool isCompressed, bool isPackFile) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        using var memoryStream = new MemoryStream();
        sarc.Write(memoryStream);

        if (isCompressed) {
            var type = CompressionType.Common;

            // Change compression type
            if (isPackFile)
                type = CompressionType.Pack;
            else if (archivePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;

            File.WriteAllBytes(archivePath, compression.Compress(memoryStream.ToArray(), type).ToArray());
        } else {
            File.WriteAllBytes(archivePath, memoryStream.ToArray());
        }
    }

    public string GetRelativePath(string archivePath, string basePath) {
        var pathRelativeToBase = Path.GetRelativePath(basePath, archivePath);

        if (Path.DirectorySeparatorChar != '/')
            pathRelativeToBase = pathRelativeToBase.Replace(Path.DirectorySeparatorChar, '/');

        pathRelativeToBase = pathRelativeToBase.Replace($"romfs/", "")
                                               .Replace($"/romfs/", "");

        if (pathRelativeToBase.EndsWith(".zs"))
            pathRelativeToBase = pathRelativeToBase.Substring(0, pathRelativeToBase.Length - 3);

        return pathRelativeToBase;

    }

    public string GetAbsolutePath(string relativePath, string basePath) {
        if (Path.DirectorySeparatorChar != '/')
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

        if (!basePath.Contains("romfs"))
            return Path.Combine(basePath, "romfs", relativePath);
        else
            return Path.Combine(basePath, relativePath);
    }

    public Memory<byte> GetFlatFileContents(string filePath, bool isCompressed) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        Span<byte> sourceFileContents;
        if (isCompressed) {
            // Need to decompress the file first
            var type = CompressionType.Common;

            // Change compression type
            if (filePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;

            var compressedContents = File.ReadAllBytes(filePath).AsSpan();
            sourceFileContents = compression.Decompress(compressedContents, type);
        } else {
            sourceFileContents = File.ReadAllBytes(filePath).AsSpan();
        }

        return new Memory<byte>(sourceFileContents.ToArray());
    }

    public void WriteFlatFileContents(string filePath, ReadOnlyMemory<byte> contents, bool isCompressed) {
        if (compression == null)
            throw new Exception("Compression not loaded");

        if (isCompressed) {
            var type = CompressionType.Common;

            // Change compression type
            if (filePath.Contains("bcett", StringComparison.OrdinalIgnoreCase))
                type = CompressionType.Bcett;

            File.WriteAllBytes(filePath,
                               compression.Compress(contents.ToArray(), type).ToArray());
        } else {
            File.WriteAllBytes(filePath, contents.ToArray());
        }
    }
    
}