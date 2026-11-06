using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Portal.Api.Profiles.Storage;

public sealed class LocalFileSystemDocumentStorage : IDocumentStorage
{
    private readonly string _rootPath;

    public LocalFileSystemDocumentStorage(IOptions<DocumentStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFileResult> SaveAsync(
        Guid candidateId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var safeExt = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant();
        if (!safeExt.StartsWith('.'))
        {
            safeExt = "." + safeExt;
        }

        var storageKey = $"candidates/{candidateId:N}/{Guid.NewGuid():N}{safeExt}";
        var fullPath = GetSafeFullPath(storageKey);

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        using var sha256 = SHA256.Create();
        await using var fileStream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await using var cryptoStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write);
        await content.CopyToAsync(cryptoStream, cancellationToken);
        await cryptoStream.FlushFinalBlockAsync(cancellationToken);

        var hashHex = Convert.ToHexStringLower(sha256.Hash!);
        var sizeBytes = fileStream.Length;

        return new StoredFileResult(storageKey, hashHex, sizeBytes);
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string GetSafeFullPath(string storageKey)
    {
        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_rootPath, relativePath));

        if (!combined.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage key path traversal attempt.");
        }

        return combined;
    }
}