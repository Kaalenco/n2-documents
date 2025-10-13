using Azure.Storage.Blobs;

using N2.Core;

namespace N2.Documents;

/// <summary>
/// Wrapper for a <see cref="BlobServiceClient"/>
/// </summary>
#pragma warning disable CA1308 // use uppercase
public class BlobServiceClientProxy : IBlobServiceClient
{
    private readonly BlobServiceClient client;
    private readonly DocumentServiceSettings settings;
    public string AccountName => client.AccountName;

    public BlobServiceClientProxy(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        settings = settingsService.GetConfigSettings<DocumentServiceSettings>();
        client = new BlobServiceClient(settings.StorageConnectionString);
    }

    public IEnumerable<string> ValidRoles => settings.ValidRoles;

    public IBlobContainerClient GetBlobContainerClient(string blobContainerName, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(blobContainerName);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        string[] elements = blobContainerName.ToLowerInvariant().Split('\\', StringSplitOptions.RemoveEmptyEntries);

        string containerName;
        string blobFileName;
        if (elements.Length > 0)
        {
            containerName = elements[0];
            blobFileName = string.Concat(string.Join('/', elements.Skip(1)), '/', fileName);
        }
        else
        {
            containerName = "$root";
            blobFileName = fileName;
        }

        return new BlobContainerClientProxy(client.GetBlobContainerClient(containerName), blobFileName);
    }

    public async Task<string> CreateIfNotExistsAsync(string createdPath)
    {
        CancellationToken token = CancellationToken.None;
        ArgumentException.ThrowIfNullOrEmpty(createdPath);

        string[] elements = createdPath.ToLowerInvariant().Split('\\', StringSplitOptions.RemoveEmptyEntries);
        string containerName = elements.Length > 0 ? elements[0] : "$root";

        BlobContainerClient container = client.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: token);
        return container.Uri.ToString();
    }
}
#pragma warning restore CA1308 // use uppercase