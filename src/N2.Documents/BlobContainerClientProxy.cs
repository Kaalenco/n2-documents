using Azure;
using Azure.Storage.Blobs;

namespace N2.Documents;

/// <summary>
/// Wrapper for a <see cref="BlobContainerClient"/>.
/// </summary>
public class BlobContainerClientProxy : IBlobContainerClient
{
    private readonly BlobContainerClient client;

    public BlobContainerClientProxy(BlobContainerClient client, string fileName)
    {
        this.client = client;
        FileName = fileName;
    }

    /// <summary>
    /// File reference.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Name for the container.
    /// </summary>
    public string Container => client.Name;

    public bool ContainerExists()
    {
        try
        {
            return client.Exists();
        }
        catch (RequestFailedException e) when (e.ErrorCode == "InvalidUri")
        {
            return false;
        }
    }

    public bool BlobExists()
    {
        var doc = client.GetBlobClient(FileName);
        return doc.Exists();
    }

    public IBinaryFileInfo BinaryFileInfo()
    {
        var doc = client.GetBlobClient(FileName);
        return new BlobFileInfo(doc);
    }

    public async Task<(Uri path, string md5Hash)> UploadBlobAsync(Stream data, Dictionary<string, string>? metaData = null)
    {
        var blob = client.GetBlobClient(FileName);
        var contentInfo = await blob.UploadAsync(data);
        var md5Hash = Convert.ToBase64String(contentInfo.Value.ContentHash);
        if (metaData?.Count > 0)
        {
            _ = await blob.SetMetadataAsync(metaData);
        }
        return new(blob.Uri, md5Hash);
    }
}
