using System.Text;
using Microsoft.Extensions.Options;
using Portal.Api.Profiles;
using Portal.Api.Profiles.Storage;
using Portal.Contracts.Profiles;
using Xunit;

namespace Portal.Api.Tests;

public sealed class CandidateCvServiceTests : IDisposable
{
    private readonly string _tempStorageDir;
    private readonly LocalFileSystemDocumentStorage _storage;
    private readonly FakeCandidateDocumentRepository _repository;
    private readonly CandidateCvService _service;

    public CandidateCvServiceTests()
    {
        _tempStorageDir = Path.Combine(Path.GetTempPath(), "portal-cv-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DocumentStorageOptions
        {
            RootPath = _tempStorageDir,
            MaxFileSizeBytes = 1024 * 1024 // 1 MB for tests
        });

        _storage = new LocalFileSystemDocumentStorage(options);
        _repository = new FakeCandidateDocumentRepository();
        _service = new CandidateCvService(_repository, _storage, options);
    }

    [Fact]
    public async Task UploadCvAsync_ValidPdf_StoresFileComputesSha256AndSetsCurrentCv()
    {
        var candidateId = Guid.NewGuid();
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.7 fake pdf content for testing");
        using var stream = new MemoryStream(pdfBytes);

        var response = await _service.UploadCvAsync(
            candidateId,
            "My_Resume.pdf",
            pdfBytes.Length,
            stream,
            setAsCurrent: true);

        Assert.Equal(candidateId, response.CandidateId);
        Assert.Equal("cv", response.Kind);
        Assert.Equal("My_Resume.pdf", response.OriginalFilename);
        Assert.Equal("application/pdf", response.MimeType);
        Assert.Equal(pdfBytes.Length, response.SizeBytes);
        Assert.Equal(64, response.Sha256.Length);
        Assert.True(response.IsCurrentCv);

        var downloaded = await _service.DownloadCurrentCvAsync(candidateId);
        Assert.NotNull(downloaded);
        await using var downloadedStream = downloaded.Value.Stream;
        using var reader = new StreamReader(downloadedStream);
        var content = await reader.ReadToEndAsync();
        Assert.Equal("%PDF-1.7 fake pdf content for testing", content);
    }

    [Fact]
    public async Task UploadCvAsync_UnsupportedExtension_ThrowsCvValidationException()
    {
        var candidateId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("Executable content");
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<CvValidationException>(() =>
            _service.UploadCvAsync(candidateId, "malware.exe", bytes.Length, stream));
    }

    [Fact]
    public async Task UploadCvAsync_FakePdfHeader_ThrowsCvValidationException()
    {
        var candidateId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("This is plain text pretending to be a PDF");
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<CvValidationException>(() =>
            _service.UploadCvAsync(candidateId, "resume.pdf", bytes.Length, stream));
    }

    [Fact]
    public async Task UploadCvAsync_ExceedsMaxSize_ThrowsCvValidationException()
    {
        var candidateId = Guid.NewGuid();
        using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D]);

        await Assert.ThrowsAsync<CvValidationException>(() =>
            _service.UploadCvAsync(candidateId, "huge.pdf", 5 * 1024 * 1024, stream));
    }

    [Fact]
    public async Task SetCurrentCvAsync_SwitchesActiveCvBetweenUploadedVersions()
    {
        var candidateId = Guid.NewGuid();
        var pdfBytes1 = Encoding.UTF8.GetBytes("%PDF-1.7 version 1");
        var pdfBytes2 = Encoding.UTF8.GetBytes("%PDF-1.7 version 2");

        var doc1 = await _service.UploadCvAsync(candidateId, "v1.pdf", pdfBytes1.Length, new MemoryStream(pdfBytes1), setAsCurrent: true);
        var doc2 = await _service.UploadCvAsync(candidateId, "v2.pdf", pdfBytes2.Length, new MemoryStream(pdfBytes2), setAsCurrent: false);

        var currentBefore = await _service.GetCurrentCvMetadataAsync(candidateId);
        Assert.Equal(doc1.Id, currentBefore?.Id);

        var switched = await _service.SetCurrentCvAsync(candidateId, doc2.Id);
        Assert.True(switched);

        var currentAfter = await _service.GetCurrentCvMetadataAsync(candidateId);
        Assert.Equal(doc2.Id, currentAfter?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempStorageDir))
        {
            Directory.Delete(_tempStorageDir, recursive: true);
        }
    }

    private sealed class FakeCandidateDocumentRepository : ICandidateDocumentRepository
    {
        private readonly List<CandidateDocumentRecord> _records = [];
        private readonly Dictionary<Guid, Guid> _currentCvByCandidate = [];

        public Task<CandidateDocumentRecord> InsertDocumentAsync(
            Guid candidateId,
            string kind,
            string originalFilename,
            string storageKey,
            string mimeType,
            long sizeBytes,
            string sha256,
            bool setAsCurrentCv,
            CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid();
            if (setAsCurrentCv && kind == CandidateDocumentKinds.Cv)
            {
                _currentCvByCandidate[candidateId] = id;
            }

            var isCurrent = _currentCvByCandidate.TryGetValue(candidateId, out var currId) && currId == id;
            var record = new CandidateDocumentRecord(
                id, candidateId, kind, originalFilename, storageKey, mimeType, sizeBytes, sha256, isCurrent, DateTimeOffset.UtcNow);
            _records.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<CandidateDocumentResponse>> GetCandidateDocumentsAsync(
            Guid candidateId,
            CancellationToken cancellationToken = default)
        {
            _currentCvByCandidate.TryGetValue(candidateId, out var currentId);
            var list = _records
                .Where(r => r.CandidateId == candidateId)
                .Select(r => new CandidateDocumentResponse(
                    r.Id, r.CandidateId, r.Kind, r.OriginalFilename, r.MimeType, r.SizeBytes, r.Sha256, r.Id == currentId, r.CreatedAt))
                .ToList();
            return Task.FromResult<IReadOnlyList<CandidateDocumentResponse>>(list);
        }

        public Task<CandidateDocumentRecord?> GetDocumentByIdAsync(
            Guid candidateId,
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            _currentCvByCandidate.TryGetValue(candidateId, out var currentId);
            var record = _records.FirstOrDefault(r => r.CandidateId == candidateId && r.Id == documentId);
            return Task.FromResult(record is null ? null : record with { IsCurrentCv = record.Id == currentId });
        }

        public Task<CandidateDocumentRecord?> GetCurrentCvAsync(
            Guid candidateId,
            CancellationToken cancellationToken = default)
        {
            if (!_currentCvByCandidate.TryGetValue(candidateId, out var currentId))
            {
                return Task.FromResult<CandidateDocumentRecord?>(null);
            }

            return GetDocumentByIdAsync(candidateId, currentId, cancellationToken);
        }

        public Task<bool> SetCurrentCvAsync(
            Guid candidateId,
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            var exists = _records.Any(r => r.CandidateId == candidateId && r.Id == documentId && r.Kind == "cv");
            if (!exists)
            {
                return Task.FromResult(false);
            }

            _currentCvByCandidate[candidateId] = documentId;
            return Task.FromResult(true);
        }
    }
}