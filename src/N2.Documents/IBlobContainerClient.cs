using Azure.Storage.Blobs;
using N2.Core;

namespace N2.Documents;

/// <summary>
/// Interface to abstract a <see cref="BlobContainerClient"/>.
/// </summary>
public interface IBlobContainerClient
{
    bool ContainerExists();

    bool BlobExists();

    string Container { get; }
    string FileName { get; }

    /// <summary>
    /// Upload data and returns an uri path to the document.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="metaData"></param>
    /// <returns></returns>
    Task<(Uri path, string md5Hash)> UploadBlobAsync(Stream data, Dictionary<string, string>? metaData = null);

    IBinaryFileInfo BinaryFileInfo();
}

/// <summary>
/// Interface to abstract the <see cref="Azure.Storage.Blobs.BlobServiceClient"/>.
/// </summary>
public interface IBlobServiceClient
{
    /// <summary>
    /// The account name.
    /// </summary>
    string AccountName { get; }

    /// <summary>
    /// Create a BlobContainerClient using the container name.
    /// </summary>
    /// <param name="blobContainerName"></param>
    /// <param name="fileName"></param>
    /// <returns>a <see cref="BlobContainerClient"/></returns>
    IBlobContainerClient GetBlobContainerClient(string blobContainerName, string fileName);

    /// <summary>
    /// Check if a container exists and create the container if it does not exist.
    /// </summary>
    /// <param name="createdPath"></param>
    /// <returns></returns>
    Task<string> CreateIfNotExistsAsync(string createdPath);
}

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

        var elements = blobContainerName.ToLowerInvariant().Split('\\', StringSplitOptions.RemoveEmptyEntries);

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
        ArgumentException.ThrowIfNullOrEmpty(createdPath);

        var elements = createdPath.ToLowerInvariant().Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var containerName = elements.Length > 0 ? elements[0] : "$root";

        var container = client.GetBlobContainerClient(containerName);
        _ = await container.CreateIfNotExistsAsync().ConfigureAwait(false);
        return container.Uri.ToString();
    }
}
#pragma warning restore CA1308 // use uppercase