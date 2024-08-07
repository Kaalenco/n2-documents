using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.StaticFiles;
using System.Diagnostics.CodeAnalysis;

namespace N2.Documents;

/// <summary>
/// BlobClient proxy.
/// </summary>
public class BlobFileInfo : IBinaryFileInfo
{
    private readonly BlobClient blobClient;

    public bool Exists => blobClient.Exists();
    public string FileName { get; }
    public string Extension { get; }
    public string LastException { get; private set; }

    public BlobFileInfo([NotNull] BlobClient blobClient)
    {
        this.blobClient = blobClient;
        Extension = string.Empty;
        LastException = string.Empty;
        FileName = blobClient.Name;
        if (string.IsNullOrEmpty(FileName))
        {
            return;
        }
        var n = FileName.LastIndexOf('.');
        if (n >= 0)
        {
            Extension = FileName[(n + 1)..];
        }
    }

    public Stream OpenRead()
    {
        LastException = string.Empty;
        try
        {
            return blobClient.OpenRead();
        }
        catch (Exception e)
        {
            LastException = $"500: {e.Message}";
            throw;
        }
    }

    public bool Delete()
    {
        LastException = string.Empty;
        try
        {
            blobClient.Delete();
            return true;
        }
        catch (RequestFailedException e)
        {
            LastException = $"{e.Status}: {e.Message}";
            return false;
        }
    }

    public string ContentType()
    {
        new FileExtensionContentTypeProvider()
            .TryGetContentType(FileName, out var contentType);
        return contentType ?? "application/octet-stream";
    }

    public async Task<string> UploadAsync(Stream data)
    {
        var result = await blobClient.UploadAsync(data);
        return Convert.ToBase64String(result.Value.ContentHash);
    }
}
