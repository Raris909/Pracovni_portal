using Microsoft.Extensions.Options;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.Profiles;

namespace Portal.Api.Profiles;

public sealed class CvValidationException : Exception
{
    public CvValidationException(string message) : base(message) { }
}

public sealed class CandidateCvService
{
    private static readonly Dictionary<string, (string MimeType, byte[] MagicBytes)> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", "%PDF-"u8.ToArray()),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [0x50, 0x4B, 0x03, 0x04])
    };

    private readonly ICandidateDocumentRepository _repository;
    private readonly IDocumentStorage _storage;
    private readonly DocumentStorageOptions _options;

    public CandidateCvService(
        ICandidateDocumentRepository repository,
        IDocumentStorage storage,
        IOptions<DocumentStorageOptions> options)
    {
        _repository = repository;
        _storage = storage;
        _options = options.Value;
    }

    public async Task<CandidateDocumentResponse> UploadCvAsync(
        Guid candidateId,
        string originalFilename,
        long length,
        Stream fileStream,
        bool setAsCurrent = true,
        CancellationToken cancellationToken = default)
    {
        var sanitizedFilename = Path.GetFileName(originalFilename?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedFilename))
        {
            throw new CvValidationException("Filename is required.");
        }

        if (length <= 0)
        {
            throw new CvValidationException("Uploaded file cannot be empty.");
        }

        if (length > _options.MaxFileSizeBytes)
        {
            throw new CvValidationException($"File size exceeds the maximum allowed limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(sanitizedFilename);
        if (!AllowedFormats.TryGetValue(extension, out var formatInfo))
        {
            throw new CvValidationException("Unsupported CV file format. Only .pdf and .docx files are allowed.");
        }

        // Buffer into memory or seekable stream to verify magic bytes before storing
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length != length || memoryStream.Length == 0)
        {
            throw new CvValidationException("Uploaded file stream length mismatch or empty.");
        }

        if (!HasValidMagicBytes(memoryStream, formatInfo.MagicBytes))
        {
            throw new CvValidationException($"File content does not match a valid {extension} file signature.");
        }

        memoryStream.Position = 0;

        var stored = await _storage.SaveAsync(candidateId, memoryStream, extension, cancellationToken);

        try
        {
            var record = await _repository.InsertDocumentAsync(
                candidateId: candidateId,
                kind: CandidateDocumentKinds.Cv,
                originalFilename: sanitizedFilename,
                storageKey: stored.StorageKey,
                mimeType: formatInfo.MimeType,
                sizeBytes: stored.SizeBytes,
                sha256: stored.Sha256,
                setAsCurrentCv: setAsCurrent,
                cancellationToken: cancellationToken);

            return MapToResponse(record);
        }
        catch
        {
            // Clean up orphan file if DB transaction fails
            await _storage.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }
    }

    public Task<IReadOnlyList<CandidateDocumentResponse>> GetDocumentsAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default) =>
        _repository.GetCandidateDocumentsAsync(candidateId, cancellationToken);

    public async Task<CandidateDocumentResponse?> GetCurrentCvMetadataAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetCurrentCvAsync(candidateId, cancellationToken);
        return record is null ? null : MapToResponse(record);
    }

    public async Task<(Stream Stream, CandidateDocumentRecord Metadata)?> DownloadCurrentCvAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetCurrentCvAsync(candidateId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(record.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return (stream, record);
    }

    public async Task<(Stream Stream, CandidateDocumentRecord Metadata)?> DownloadDocumentByIdAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetDocumentByIdAsync(candidateId, documentId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(record.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        return (stream, record);
    }

    public Task<bool> SetCurrentCvAsync(
        Guid candidateId,
        Guid documentId,
        CancellationToken cancellationToken = default) =>
        _repository.SetCurrentCvAsync(candidateId, documentId, cancellationToken);

    private static bool HasValidMagicBytes(MemoryStream stream, byte[] expectedSignature)
    {
        if (stream.Length < expectedSignature.Length)
        {
            return false;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[expectedSignature.Length];
        var read = stream.Read(header);
        return read == expectedSignature.Length && header.SequenceEqual(expectedSignature);
    }

    private static CandidateDocumentResponse MapToResponse(CandidateDocumentRecord r) =>
        new(
            r.Id,
            r.CandidateId,
            r.Kind,
            r.OriginalFilename,
            r.MimeType,
            r.SizeBytes,
            r.Sha256,
            r.IsCurrentCv,
            r.CreatedAt);
}