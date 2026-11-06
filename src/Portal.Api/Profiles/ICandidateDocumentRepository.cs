using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

public sealed record CandidateDocumentRecord(
    Guid Id,
    Guid CandidateId,
    string Kind,
    string OriginalFilename,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    string Sha256,
    bool IsCurrentCv,
    DateTimeOffset CreatedAt);

public interface ICandidateDocumentRepository
{
    Task<CandidateDocumentRecord> InsertDocumentAsync(
        Guid candidateId,
        string kind,
        string originalFilename,
        string storageKey,
        string mimeType,
        long sizeBytes,
        string sha256,
        bool setAsCurrentCv,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CandidateDocumentResponse>> GetCandidateDocumentsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<CandidateDocumentRecord?> GetDocumentByIdAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<CandidateDocumentRecord?> GetCurrentCvAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<bool> SetCurrentCvAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}