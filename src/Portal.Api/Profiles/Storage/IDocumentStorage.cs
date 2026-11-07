namespace Portal.Api.Profiles.Storage;

public sealed record StoredFileResult(
    string StorageKey,
    string Sha256,
    long SizeBytes);

public interface IDocumentStorage
{
    Task<StoredFileResult> SaveAsync(
        Guid candidateId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}