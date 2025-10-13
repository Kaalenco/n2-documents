using System.Diagnostics.CodeAnalysis;

using N2.Core;
using N2.Core.Dms;
using N2.Core.Identity;
using N2.Documents.Extensions;

namespace N2.Documents;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository docRepository;
    private readonly ILogService logService;
    private readonly DocumentServiceSettings settings;
    private readonly IBinaryStorageService storageService;
    private readonly IUserContext userContext;

    public DocumentService(
        [NotNull] IBinaryStorageService storageService,
        [NotNull] IDocumentRepository docRepository,
        [NotNull] ISettingsService settingsService,
        [NotNull] IUserContext userContext,
        [NotNull] ILogService logService)
    {
        this.storageService = storageService;
        this.docRepository = docRepository;
        this.userContext = userContext;
        this.logService = logService;
        this.settings = settingsService.GetConfigSettings<DocumentServiceSettings>();
        settings.ValidRoles = settings.ValidRoles.Select(r => r.Trim().ToUpperInvariant()).ToArray();
    }

    public async Task<(bool success, string message)> DeleteDocumentAsync(Guid documentIdentifier)
    {
        CancellationToken token = CancellationToken.None;
        Document document = await docRepository.FindDocumentAsync(documentIdentifier, false, token);
        if (document == null)
        {
            return (false, "Document not found");
        }

        if (document.IsPrivate && document.CreatedBy == userContext.PublicId)
        {
            // delete is allowed for a document that is private an created by the current user
        }
        else
        {
            bool userIsAdmin = userContext.IsAdmin();
            if (!userIsAdmin)
            {
                if (document.IsPrivate)
                {
                    return (false, "Document not found");
                }
                else
                {
                    return (false, "You are not authorized to delete documents");
                }
            }
        }
        document.IsEnabled = false;
        document.IsRemoved = true;
        document.Removed = DateTime.UtcNow;
        docRepository.SaveDocument(document);
        int updated = await docRepository.CompleteAsync(userContext, token);
        logService.LogInformation<DocumentService>($"Document {documentIdentifier} deleted");
        return (updated > 0, "Document deleted");
    }

    public async Task<IEnumerable<DocumentInformation>> FindDocumentsAsync(string search, IEnumerable<string> forRoles, string processName, bool showInactiveDocuments)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(forRoles);
        IQueryable<Document> docQuery = string.IsNullOrEmpty(search)
            ? docRepository.DocumentQuery.Where(d => !d.IsRemoved)
            : docRepository.DocumentQuery.Where(d => !d.IsRemoved && (d.Remarks.Contains(search) || d.OriginalName.Contains(search)));

        if (!string.IsNullOrEmpty(processName))
        {
            docQuery = docQuery.Where(d => d.ProcessName == processName);
        }

        if (!showInactiveDocuments)
        {
            docQuery = docQuery.Where(d => d.IsEnabled);
        }
        Guid userId = userContext.PublicId;
        bool userIsAdmin = userContext.IsAdmin();
        IOrderedQueryable<Document> documentQuery = docQuery.OrderByDescending(d => d.Created);
        Document[] documents;
        IAsyncEnumerable<Document>? docQueryAsync = documentQuery as IAsyncEnumerable<Document>;
        if (docQueryAsync != null)
        {
            List<Document> docList = new();
            IAsyncEnumerator<Document> enumerator = docQueryAsync.GetAsyncEnumerator();
            while (await enumerator.MoveNextAsync())
            {
                docList.Add(enumerator.Current);
            }
            documents = docList.ToArray();
        }
        else
        {
            documents = [.. documentQuery];
        }

        List<DocumentInformation> result = new();
        foreach (Document? document in documents)
        {
            string[] documentRoles = document.Roles ?? [];
            foreach (string role in forRoles)
            {
                if (userIsAdmin
                    || (!document.IsPrivate && documentRoles.Contains(role))
                    || (document.IsPrivate && document.CreatedBy == userId))
                {
                    DocumentInformation documentInfo = new()
                    {
                        PublicId = document.PublicId,
                        IsEnabled = document.IsEnabled,
                        DocumentIdentifier = document.Location,
                        Roles = document.Roles ?? [],
                        Tags = document.Tags ?? [],
                        FileName = document.OriginalName,
                        Extension = document.Extension,
                        Created = document.Created,
                        UploadedBy = document.CreatedBy,
                        Size = document.Size ?? -1,
                        Remarks = document.Remarks
                    };

                    result.Add(documentInfo);
                    break;
                }
            }
        }
        return result;
    }

    public async Task<(bool success, DocumentInformation document)> GetDocumentInformationAsync(Guid documentIdentifier)
    {
        CancellationToken token = CancellationToken.None;
        bool userIsAdmin = userContext.IsAdmin();
        Document? document;
        if (userIsAdmin)
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, token);
        }
        else
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, false, false, true, token);
        }

        if (document == null)
        {
            return (false, new DocumentInformation());
        }

        DocumentInformation result = new()
        {
            PublicId = document.PublicId,
            IsEnabled = document.IsEnabled,
            DocumentIdentifier = document.Location ?? "",
            Roles = document.Roles ?? [],
            Tags = document.Tags ?? [],
            FileName = document.OriginalName ?? "",
            Extension = document.Extension ?? "",
            Created = document.Created,
            UploadedBy = document.CreatedBy,
            Size = document.Size ?? -1,
            Remarks = document.Remarks ?? ""
        };
        return (true, result);
    }

    public async Task<(bool success, DocumentInformation document)> SaveDocumentAsync(
                    Stream streamedFileContent,
        DocumentInformation formData)
    {
        ArgumentNullException.ThrowIfNull(formData);
        ArgumentNullException.ThrowIfNull(streamedFileContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(formData.ProcessName);

        Guid fileGuid = Guid.NewGuid();
        CancellationToken token = CancellationToken.None;
        string targetPath = Path.Combine("Data", formData.ProcessName);
        string untrustedFileNameForStorage = formData.FileName;
        string fileExtension = untrustedFileNameForStorage.Substring(untrustedFileNameForStorage.LastIndexOf('.'));
        string contentType = UploadHelper.ExtensionType(fileExtension);
        string newFileName = string.Concat(fileGuid.ToString(), fileExtension);
        string savePath = await storageService.CreateSavePathAsync(targetPath, fileGuid);

        // Document identifier does not contain targetpath
        string documentStorage = Path.Combine(savePath, newFileName);
        string documentIdentifier = Path.Combine(savePath, newFileName)[(targetPath.Length + 1)..];

        logService.LogDebug<DocumentService>($"Saving file: {documentIdentifier}");

        Dictionary<string, string> metaData = new()
        {
            { "OriginalFileName", untrustedFileNameForStorage },
            { "UserId", userContext.PublicId.ToString() },
            { "DcmiType", formData.DcmiType.ToString() },
            { "ContentType", contentType }
        };
        await storageService.CreateDocumentAsync(streamedFileContent, documentStorage, metaData);

        Document file = new()
        {
            Location = documentIdentifier,
            ExtensionGroup = contentType,
            Extension = fileExtension,
            OriginalName = untrustedFileNameForStorage,
            Remarks = formData.Remarks,
            Roles = [.. GetValidRoles(formData)],
            Tags = [.. GetValidTags(formData)],
            Size = (int)streamedFileContent.Length,
            Created = DateTime.UtcNow,
            CreatedBy = userContext.PublicId,
            IsPrivate = true,
            IsEnabled = formData.IsEnabled,
            ProcessName = formData.ProcessName,
            DcmiType = formData.DcmiType,
        };

        docRepository.SaveDocument(file);
        int update = await docRepository.CompleteAsync(userContext, token);
        return new(update > 0, new DocumentInformation
        {
            DocumentIdentifier = file.Location,
            Roles = [.. file.Roles],
            FileName = file.OriginalName,
            Extension = file.Extension,
            Created = file.Created,
            IsEnabled = file.IsEnabled,
            UploadedBy = file.CreatedBy,
            Size = file.Size ?? -1,
            Remarks = file.Remarks
        });
    }

    public async Task<(bool success, DocumentInformation document)> UpdateDocumentAsync(
        Guid documentIdentifier,
        [NotNull] DocumentInformation formData)
    {
        CancellationToken token = CancellationToken.None;
        bool userIsAdmin = userContext.IsAdmin();
        Document? document;
        if (userIsAdmin)
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, token);
        }
        else
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, false, userContext.PublicId, token);
        }
        if (document == null)
        {
            return (false, new DocumentInformation());
        }
        document.Remarks = formData.Remarks;
        document.IsEnabled = formData.IsEnabled;
        document.Roles = [.. GetValidRoles(formData)];
        docRepository.SaveDocument(document);
        int updateCount = await docRepository.CompleteAsync(userContext, token);

        DocumentInformation result = new()
        {
            PublicId = document.PublicId,
            IsEnabled = document.IsEnabled,
            DocumentIdentifier = document.Location ?? "",
            Roles = document.Roles ?? [],
            FileName = document.OriginalName ?? "",
            Extension = document.Extension ?? "",
            Created = document.Created,
            UploadedBy = document.CreatedBy,
            Size = document.Size ?? -1,
            Remarks = document.Remarks ?? ""
        };
        return (updateCount > 0, result);
    }

    private static List<string> GetValidTags(DocumentInformation formData)
    {
        List<string> validTags = new();
        foreach (string tag in formData.Tags)
        {
            string tagNormalized = tag.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(tagNormalized))
            {
                validTags.Add(tagNormalized);
            }
        }

        return validTags;
    }

    private List<string> GetValidRoles(DocumentInformation formData)
    {
        List<string> validRoles = new();
        foreach (string role in formData.Roles)
        {
            string roleNormalized = role.Trim().ToUpperInvariant();
            if (settings.ValidRoles.Contains(roleNormalized))
            {
                validRoles.Add(roleNormalized);
            }
        }

        return validRoles;
    }
}