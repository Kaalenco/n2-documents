# N2.Documents

Document management services using Azure Blob Storage and Azure Table Storage.

## Overview

N2.Documents provides a complete document management solution with:
- Document storage in Azure Blob Storage
- Document metadata stored in Azure Table Storage
- Role-based access control
- Support for private and public documents
- Document tagging and search capabilities

## Installation

Add the `N2.Documents` package to your project:

```bash
dotnet add package N2.Documents
```

## Configuration

### appsettings.json

Add the following configuration to your `appsettings.json`:

```json
{
  "DocumentServiceSettings": {
    "StorageConnectionString": "your-azure-storage-connection-string",
    "ValidRoles": ["ADMIN", "USER", "MANAGER"]
  }
}
```

### Dependency Injection Setup

Register the required services in your `Program.cs` or `Startup.cs`:

```csharp
using N2.Documents;
using N2.Core;

// Register core dependencies
builder.Services.AddSingleton<ILogService, YourLogService>();
builder.Services.AddScoped<IUserContext, YourUserContext>();
builder.Services.AddSingleton<ISettingsService, YourSettingsService>();

// Register document services
builder.Services.AddSingleton<IBlobServiceClient>(sp =>
{
    var settings = sp.GetRequiredService<ISettingsService>();
    var config = settings.GetConfigSettings<DocumentServiceSettings>();
    return new BlobServiceClient(config.StorageConnectionString);
});

builder.Services.AddSingleton<IBinaryStorageService, BlobStorageService>();

builder.Services.AddSingleton<IDocumentRepository>(sp =>
{
    var settings = sp.GetRequiredService<ISettingsService>();
    var config = settings.GetConfigSettings<DocumentServiceSettings>();
    return new AzureDocumentRepository(config.StorageConnectionString);
});

builder.Services.AddScoped<IDocumentService, DocumentService>();
```

## Usage Examples

### 1. Uploading a Document

```csharp
public class DocumentController : ControllerBase
{
    private readonly IDocumentService documentService;

    public DocumentController(IDocumentService documentService)
    {
        this.documentService = documentService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument(IFormFile file)
    {
        using var stream = file.OpenReadStream();

        var documentInfo = new DocumentInformation
        {
            FileName = file.FileName,
            ProcessName = "MyProcess",
            Remarks = "User uploaded document",
            IsEnabled = true,
            Roles = new[] { "USER", "ADMIN" },
            Tags = new[] { "invoice", "2024" },
            DcmiType = AttachmentType.Document
        };

        var (success, document) = await documentService.SaveDocumentAsync(stream, documentInfo);

        if (success)
        {
            return Ok(new { DocumentId = document.PublicId });
        }

        return BadRequest("Failed to save document");
    }
}
```

### 2. Searching for Documents

```csharp
public async Task<IEnumerable<DocumentInformation>> SearchDocuments(string searchTerm)
{
    var userRoles = new[] { "USER", "ADMIN" };
    var documents = await documentService.FindDocumentsAsync(
        search: searchTerm,
        forRoles: userRoles,
        processName: "MyProcess",
        showInactiveDocuments: false
    );

    return documents;
}
```

### 3. Retrieving Document Information

```csharp
public async Task<DocumentInformation> GetDocument(Guid documentId)
{
    var (success, document) = await documentService.GetDocumentInformationAsync(documentId);

    if (!success)
    {
        throw new DocumentNotFoundException($"Document {documentId} not found");
    }

    return document;
}
```

### 4. Updating Document Metadata

```csharp
public async Task<bool> UpdateDocument(Guid documentId, string newRemarks)
{
    var (success, existingDoc) = await documentService.GetDocumentInformationAsync(documentId);

    if (!success)
    {
        return false;
    }

    existingDoc.Remarks = newRemarks;
    existingDoc.Roles = new[] { "ADMIN", "MANAGER" };

    var (updateSuccess, updatedDoc) = await documentService.UpdateDocumentAsync(
        documentId,
        existingDoc
    );

    return updateSuccess;
}
```

### 5. Deleting a Document

```csharp
public async Task<bool> DeleteDocument(Guid documentId)
{
    var (success, message) = await documentService.DeleteDocumentAsync(documentId);

    if (success)
    {
        Console.WriteLine(message); // "Document deleted"
    }

    return success;
}
```

## Key Features

### Role-Based Access Control

Documents can be assigned to specific roles. Users can only access documents if:
- They have one of the assigned roles
- The document is marked as private and they are the creator
- They are an administrator

### Private vs Public Documents

- **Private documents**: Only visible to the creator and administrators
- **Public documents**: Visible to all users with the appropriate roles

### Document Metadata

Each document includes:
- `PublicId`: Unique identifier
- `FileName` and `Extension`: Original file information
- `Roles`: Array of roles that can access the document
- `Tags`: Array of tags for categorization
- `ProcessName`: Associated business process
- `Remarks`: Description or notes
- `Created` and `UploadedBy`: Audit information
- `IsEnabled`: Soft enable/disable flag
- `IsPrivate`: Privacy flag

## Best Practices

1. **Always check success flags**: All operations return a tuple with a success indicator
2. **Use process names**: Group documents by business process for easier management
3. **Leverage tags**: Use tags for flexible categorization and filtering
4. **Set appropriate roles**: Ensure documents are only accessible to authorized users
5. **Handle errors gracefully**: Check for null/empty results and handle accordingly

## Dependencies

Required interfaces that must be implemented in your application:
- `ILogService`: Logging abstraction
- `IUserContext`: Current user information (including `PublicId` and role checking)
- `ISettingsService`: Configuration management

## Storage Structure

Documents are stored hierarchically in Azure Blob Storage:
```
Data/{ProcessName}/{byte1}/{byte2}/{byte3}/{byte4}/{guid}.{extension}
```

Metadata is stored in Azure Table Storage for fast querying and filtering.
