using System.Collections.Concurrent;

using Azure;
using Azure.Data.Tables;

using N2.Core.Dms;
using N2.Core.Identity;

namespace N2.Documents;

/// <summary>
/// Azure Table Storage implementation of <see cref="IDocumentRepository"/> for document metadata.
/// </summary>
/// <remarks>
/// This implementation eagerly loads all recent documents (Timestamp &gt;= configured lower bound) into an in-memory cache
/// on first access. It is therefore best suited for SMALL data sets (e.g. a few thousand documents) where
/// low-latency, repeated in-process querying outweighs memory usage.
/// For LARGE data sets this approach can:
///  - Increase memory pressure (entire entity set lives in one list)
///  - Add latency during first access (bulk load)
///  - Make filtered queries less efficient (LINQ over in-memory list instead of server side)
///  - Risk stale views if other writers modify the table (cache only invalidated by local writes)
///
/// TODO: Improve scalability:
///  - Replace full-cache strategy with segmented / demand-driven loading (e.g. per PublicId lookup first, then selective hydration)
///  - Add size / count threshold to disable caching automatically
///  - Introduce sliding expiration or background refresh with ETag comparison
///  - Provide server-side query pass-through for larger filtered queries
///  - Consider an index structure (e.g. ConcurrentDictionary&lt;Guid, Document&gt;) for faster existence checks / lookups
///  - Support asynchronous lazy initialization to avoid blocking callers on first access
/// </remarks>
public class AzureDocumentRepository : IDocumentRepository
{
    private const string DefaultTableName = "Documents";
    private const string DefaultPartitionKey = "DOCUMENT"; // Single partition simplifies lookups by PublicId
    private readonly TableClient tableClient;
    private readonly ConcurrentBag<Document> pendingUpserts = new();
    private readonly ConcurrentBag<Document> pendingDeletes = new();
    private volatile IReadOnlyList<Document>? cachedDocuments;
    private readonly object cacheLock = new();
    private readonly DateTime cacheSinceUtc; // Lower bound for Timestamp when priming cache

    /// <summary>
    /// Creates the repository.
    /// Optionally restricts the initial in-memory cache to entities with Timestamp >= <paramref name="cacheSinceUtc"/>.
    /// If not provided, defaults to (UtcNow - 10 days).
    /// </summary>
    public AzureDocumentRepository(string storageConnectionString, string? tableName = null, DateTime? cacheSinceUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageConnectionString);
        tableClient = new TableClient(storageConnectionString, tableName ?? DefaultTableName);
        tableClient.CreateIfNotExists();

        DateTime since = cacheSinceUtc ?? DateTime.UtcNow.AddDays(-10);
        // Normalize to UTC
        this.cacheSinceUtc = since.Kind == DateTimeKind.Utc ? since : DateTime.SpecifyKind(since.ToUniversalTime(), DateTimeKind.Utc);
    }

    /// <summary>
    /// A queryable set for document metadata.
    /// Loads (and caches) documents whose Timestamp is newer than the configured threshold.
    /// Subsequent updates refresh the cache after CompleteAsync.
    /// NOTE: This is an in-memory LINQ over the cached collection, not a server-side query.
    /// </summary>
    public IQueryable<Document> DocumentQuery
    {
        get
        {
            EnsureCacheLoaded();
            return cachedDocuments!.AsQueryable();
        }
    }

    public void SaveDocument(Document file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.PublicId == Guid.Empty)
        {
            file.PublicId = Guid.NewGuid();
        }
        pendingUpserts.Add(file);
    }

    public async Task<int> CompleteAsync(IUserContext userMatrix, CancellationToken token)
    {
        int operations = 0;

        foreach (Document doc in pendingUpserts)
        {
            DocumentEntity entity = ToEntity(doc);
            // Timestamp is server-managed; assigning here is optional. Keep for local information.
            entity.Timestamp = DateTime.UtcNow;
            _ = tableClient.UpsertEntity(entity, TableUpdateMode.Replace, token);
            operations++;
        }

        foreach (Document doc in pendingDeletes)
        {
            try
            {
                await tableClient.DeleteEntityAsync(DefaultPartitionKey, doc.PublicId.ToString("N"), cancellationToken: token);
                operations++;
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                // Ignore not found
            }
        }

        while (pendingUpserts.TryTake(out _)) { }
        while (pendingDeletes.TryTake(out _)) { }

        if (operations > 0)
        {
            InvalidateCache();
        }

        return operations;
    }

    public bool DocumentExists(string fileIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileIdentifier);
        EnsureCacheLoaded();
        return cachedDocuments!.Any(d => d.Location == fileIdentifier && !d.IsRemoved);
    }

    public Task<Document> FindDocumentAsync(Guid publicId, CancellationToken token)
        => FindInternalAsync(publicId, isDeleted: null, isPublic: null, isEnabled: null, createdBy: null, token: token);

    public Task<Document> FindDocumentAsync(Guid publicId, bool isDeleted, CancellationToken token)
        => FindInternalAsync(publicId, isDeleted, isPublic: null, isEnabled: null, createdBy: null, token: token);

    public Task<Document> FindDocumentAsync(Guid publicId, bool isDeleted, bool isPublic, bool isEnabled, CancellationToken token)
        => FindInternalAsync(publicId, isDeleted, isPublic, isEnabled, createdBy: null, token: token);

    public Task<Document> FindDocumentAsync(Guid publicId, bool isDeleted, Guid createdBy, CancellationToken token)
        => FindInternalAsync(publicId, isDeleted, isPublic: null, isEnabled: null, createdBy: createdBy, token: token);

    private async Task<Document> FindInternalAsync(Guid publicId, bool? isDeleted, bool? isPublic, bool? isEnabled, Guid? createdBy, CancellationToken token)
    {
        EnsureCacheLoaded();

        Document? doc = cachedDocuments!
            .FirstOrDefault(d =>
                d.PublicId == publicId
                && (isDeleted is null || d.IsRemoved == isDeleted)
                && (isPublic is null || d.IsPrivate != isPublic)
                && (isEnabled is null || d.IsEnabled == isEnabled)
                && (createdBy is null || d.CreatedBy == createdBy));

        if (doc == null)
        {
            try
            {
                Response<DocumentEntity> entityResponse = await tableClient.GetEntityAsync<DocumentEntity>(
                    DefaultPartitionKey,
                    publicId.ToString("N"),
                    cancellationToken: token);

                Document candidate = FromEntity(entityResponse.Value);

                bool matches =
                    (isDeleted is null || candidate.IsRemoved == isDeleted) &&
                    (isPublic is null || candidate.IsPrivate != isPublic) &&
                    (isEnabled is null || candidate.IsEnabled == isEnabled) &&
                    (createdBy is null || candidate.CreatedBy == createdBy);

                if (matches)
                {
                    doc = candidate;
                    lock (cacheLock)
                    {
                        if (cachedDocuments != null && !cachedDocuments.Any(d => d.PublicId == publicId))
                        {
                            List<Document> list = cachedDocuments.ToList();
                            list.Add(candidate);
                            cachedDocuments = list;
                        }
                    }
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Not found
            }
        }

        return doc!;
    }

    /// <summary>
    /// Marks a document as removed (soft delete). Fetches from table if not in cache.
    /// </summary>
    public async Task RemoveDocument(Guid publicId, CancellationToken token)
    {
        EnsureCacheLoaded();
        Document? doc = cachedDocuments!.FirstOrDefault(d => d.PublicId == publicId);

        if (doc == null)
        {
            try
            {
                Response<DocumentEntity> entityResponse = await
                    tableClient.GetEntityAsync<DocumentEntity>(DefaultPartitionKey, publicId.ToString("N"), cancellationToken: token);

                Document candidate = FromEntity(entityResponse.Value);

                lock (cacheLock)
                {
                    if (cachedDocuments != null && !cachedDocuments.Any(d => d.PublicId == publicId))
                    {
                        List<Document> list = cachedDocuments.ToList();
                        list.Add(candidate);
                        cachedDocuments = list;
                    }
                }

                doc = candidate;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return;
            }
        }

        doc.IsEnabled = false;
        doc.IsRemoved = true;
        doc.Removed = DateTime.UtcNow;
        pendingUpserts.Add(doc);
    }

    private void EnsureCacheLoaded()
    {
        if (cachedDocuments != null)
        {
            return;
        }

        lock (cacheLock)
        {
            if (cachedDocuments != null)
            {
                return;
            }

            List<Document> items = [];

            // Build filter for Timestamp >= cacheSinceUtc (Azure Tables expect ISO8601 in 'O' format)
            string filter = $"Timestamp ge datetime'{cacheSinceUtc:O}'";

            Pageable<DocumentEntity> query = tableClient.Query<DocumentEntity>(filter: filter, maxPerPage: 1000);
            foreach (DocumentEntity entity in query)
            {
                items.Add(FromEntity(entity));
            }

            cachedDocuments = items;
        }
    }

    private void InvalidateCache()
    {
        lock (cacheLock)
        {
            cachedDocuments = null;
        }
    }

    #region Mapping

    private static DocumentEntity ToEntity(Document doc)
    {
        return new DocumentEntity
        {
            PartitionKey = DefaultPartitionKey,
            RowKey = doc.PublicId.ToString("N"),
            PublicId = doc.PublicId,
            Location = doc.Location,
            ExtensionGroup = doc.ExtensionGroup,
            Extension = doc.Extension,
            OriginalName = doc.OriginalName,
            Remarks = doc.Remarks,
            Roles = Join(doc.Roles),
            Tags = Join(doc.Tags),
            Size = doc.Size ?? -1,
            Created = doc.Created,
            CreatedBy = doc.CreatedBy,
            IsPrivate = doc.IsPrivate,
            IsEnabled = doc.IsEnabled,
            IsRemoved = doc.IsRemoved,
            Removed = doc.Removed,
            ProcessName = doc.ProcessName,
            DcmiType = doc.DcmiType.ToString()
        };
    }

    private static Document FromEntity(DocumentEntity e)
    {
        AttachmentType dcmiType = Enum.TryParse<AttachmentType>(e.DcmiType, out AttachmentType dt) ? dt : default;

        return new Document
        {
            PublicId = e.PublicId,
            Location = e.Location ?? string.Empty,
            ExtensionGroup = e.ExtensionGroup ?? string.Empty,
            Extension = e.Extension ?? string.Empty,
            OriginalName = e.OriginalName ?? string.Empty,
            Remarks = e.Remarks ?? string.Empty,
            Roles = Split(e.Roles),
            Tags = Split(e.Tags),
            Size = e.Size < 0 ? null : e.Size,
            Created = e.Created,
            CreatedBy = e.CreatedBy,
            IsPrivate = e.IsPrivate,
            IsEnabled = e.IsEnabled,
            IsRemoved = e.IsRemoved,
            Removed = e.Removed,
            ProcessName = e.ProcessName ?? string.Empty,
            DcmiType = dcmiType
        };
    }

    private static string Join(string[]? values) => (values == null || values.Length == 0) ? "" : string.Join(';', values);
    private static string[] Split(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public Task Cleanup(CancellationToken token) => throw new NotImplementedException();

    #endregion

    /// <summary>
    /// Internal table entity representation.
    /// </summary>
    private sealed class DocumentEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = DefaultPartitionKey;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Domain fields
        public Guid PublicId { get; set; }
        public string? Location { get; set; }
        public string? ExtensionGroup { get; set; }
        public string? Extension { get; set; }
        public string? OriginalName { get; set; }
        public string? Remarks { get; set; }
        public string? Roles { get; set; }
        public string? Tags { get; set; }
        public int Size { get; set; }
        public DateTime Created { get; set; }
        public Guid CreatedBy { get; set; }
        public bool IsPrivate { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsRemoved { get; set; }
        public DateTime? Removed { get; set; }
        public string? ProcessName { get; set; }
        public string? DcmiType { get; set; }
    }
}
