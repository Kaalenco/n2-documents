using Microsoft.EntityFrameworkCore;
using N2.Core;
using N2.Core.Identity;
using N2.Documents.Extensions;
using System.Diagnostics.CodeAnalysis;

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
        var document = await docRepository.FindDocumentAsync(documentIdentifier, false);
        if (document == null)
        {
            return (false, "Document not found");
        }

        if (document.IsPrivate && document.CreatedBy == userContext.UserId)
        {
            // delete is allowed for a document that is private an created by the current user
        }
        else
        {
            var userIsAdmin = userContext.IsAdmin();
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
        var updated = await docRepository.CompleteAsync(userContext);
        logService.LogInformation<DocumentService>($"Document {documentIdentifier} deleted");
        return (updated > 0, "Document deleted");
    }

    public async Task<IEnumerable<DocumentInformation>> FindDocumentsAsync(string search, IEnumerable<string> forRoles, string processName, bool showInactiveDocuments)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(forRoles);
        var docQuery = string.IsNullOrEmpty(search)
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
        var userId = userContext.UserId;
        var userIsAdmin = userContext.IsAdmin();
        var documents = await docQuery
            .OrderByDescending(d => d.Created)
            .ToArrayAsync();

        var result = new List<DocumentInformation>();
        foreach (var document in documents)
        {
            var documentRoles = (document.Roles ?? "").Split(';');
            foreach (var role in forRoles)
            {
                if (userIsAdmin
                    || (!document.IsPrivate && documentRoles.Contains(role))
                    || (document.IsPrivate && document.CreatedBy == userId))
                {
                    var documentInfo = new DocumentInformation
                    {
                        PublicId = document.PublicId,
                        IsEnabled = document.IsEnabled,
                        DocumentIdentifier = document.Location,
                        Roles = (document.Roles ?? "").Split(';'),
                        Tags = (document.Tags ?? "").Split(';'),
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
        var userIsAdmin = userContext.IsAdmin();
        Document? document;
        if (userIsAdmin)
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier);
        }
        else
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, false, false, true);
        }

        if (document == null)
        {
            return (false, new DocumentInformation());
        }

        var result = new DocumentInformation
        {
            PublicId = document.PublicId,
            IsEnabled = document.IsEnabled,
            DocumentIdentifier = document.Location ?? "",
            Roles = (document.Roles ?? "").Split(';'),
            Tags = (document.Tags ?? "").Split(';'),
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

        var fileGuid = Guid.NewGuid();
        var targetPath = Path.Combine("Data", formData.ProcessName);
        var untrustedFileNameForStorage = formData.FileName;
        var fileExtension = untrustedFileNameForStorage.Substring(untrustedFileNameForStorage.LastIndexOf('.'));
        var contentType = UploadHelper.ExtensionType(fileExtension);
        var newFileName = string.Concat(fileGuid.ToString(), fileExtension);
        var savePath = await storageService.CreateSavePathAsync(targetPath, fileGuid).ConfigureAwait(false);

        // Document identifier does not contain targetpath
        var documentStorage = Path.Combine(savePath, newFileName);
        var documentIdentifier = Path.Combine(savePath, newFileName)[(targetPath.Length + 1)..];

        logService.LogDebug<DocumentService>($"Saving file: {documentIdentifier}");

        var metaData = new Dictionary<string, string>
        {
            { "OriginalFileName", untrustedFileNameForStorage },
            { "UserId", userContext.UserId.ToString() },
            { "DcmiType", formData.DcmiType.ToString() },
            { "ContentType", contentType }
        };
        await storageService.CreateDocumentAsync(streamedFileContent, documentStorage, metaData);

        var file = new Document()
        {
            Location = documentIdentifier,
            ExtensionGroup = contentType,
            Extension = fileExtension,
            OriginalName = untrustedFileNameForStorage,
            Remarks = formData.Remarks,
            Roles = string.Join(';', GetValidRoles(formData)),
            Tags = string.Join(';', GetValidTags(formData)),
            Size = (int)streamedFileContent.Length,
            Created = DateTime.UtcNow,
            CreatedBy = userContext.UserId,
            IsPrivate = true,
            IsEnabled = formData.IsEnabled,
            ProcessName = formData.ProcessName,
            DcmiType = formData.DcmiType,
        };

        docRepository.SaveDocument(file);
        var update = await docRepository.CompleteAsync(userContext);
        return new(update > 0, new DocumentInformation
        {
            DocumentIdentifier = file.Location,
            Roles = file.Roles.Split(';'),
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
        var userIsAdmin = userContext.IsAdmin();
        Document? document;
        if (userIsAdmin)
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier);
        }
        else
        {
            document = await docRepository.FindDocumentAsync(documentIdentifier, false, userContext.UserId);
        }
        if (document == null)
        {
            return (false, new DocumentInformation());
        }
        document.Remarks = formData.Remarks;
        document.IsEnabled = formData.IsEnabled;
        document.Roles = string.Join(';', GetValidRoles(formData));
        var updateCount = await docRepository.CompleteAsync(userContext).ConfigureAwait(false);

        var result = new DocumentInformation
        {
            PublicId = document.PublicId,
            IsEnabled = document.IsEnabled,
            DocumentIdentifier = document.Location ?? "",
            Roles = (document.Roles ?? "").Split(';'),
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
        var validTags = new List<string>();
        foreach (var tag in formData.Tags)
        {
            var tagNormalized = tag.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(tagNormalized))
            {
                validTags.Add(tagNormalized);
            }
        }

        return validTags;
    }

    private List<string> GetValidRoles(DocumentInformation formData)
    {
        var validRoles = new List<string>();
        foreach (var role in formData.Roles)
        {
            var roleNormalized = role.Trim().ToUpperInvariant();
            if (settings.ValidRoles.Contains(roleNormalized))
            {
                validRoles.Add(roleNormalized);
            }
        }

        return validRoles;
    }
}
