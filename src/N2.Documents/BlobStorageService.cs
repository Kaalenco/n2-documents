using Azure;
using N2.Core;
using N2.Documents.Exceptions;
using System.Globalization;

namespace N2.Documents;
#pragma warning disable CA1308 // use uppercase should not be used as blobstorage accepts only lowercase names

/// <summary>
/// Document storage implementation
/// </summary>
public class BlobStorageService : IBinaryStorageService
{
    private readonly IBlobServiceClient client;
    private readonly ILogService logger;
    private static readonly char[] separator = new[] { '\\', '/' };

    public BlobStorageService(
        ILogService logger,
        IBlobServiceClient client)
    {
        this.client = client;
        this.logger = logger;
    }

    public async Task<(Uri identifier, string md5Hash)> CreateDocumentAsync(Stream data, string fileIdentifier, Dictionary<string, string> metadata)
    {
        var containerClient = await FindContainerClientAsync(fileIdentifier).ConfigureAwait(false);

        if (!containerClient.ContainerExists())
        {
            throw new N2DocumentException($"Expected existing container: {containerClient.Container}");
        }

        var (identifier, md5Hash) = await containerClient.UploadBlobAsync(data, metadata).ConfigureAwait(false);

        return new(identifier, md5Hash);
    }

    private async Task<IBlobContainerClient> FindContainerClientAsync(string documentIdentifier)
    {
        documentIdentifier = documentIdentifier.ToLowerInvariant().Replace('/', '\\');
        var filestart = documentIdentifier.LastIndexOf('\\');
        var container = documentIdentifier[..filestart];
        var fileName = documentIdentifier[(filestart + 1)..];
        _ = await client.CreateIfNotExistsAsync(container).ConfigureAwait(false);
        return client.GetBlobContainerClient(container, fileName);
    }

    private static readonly CultureInfo culture = CultureInfo.InvariantCulture;

    public async Task<string> CreateSavePathAsync(string basePath, Guid uuid)
    {
        ArgumentException.ThrowIfNullOrEmpty(basePath);
        byte[] uChars = uuid.ToByteArray();
        var parts = new string[10];
        basePath.ToLowerInvariant().Split(separator, StringSplitOptions.RemoveEmptyEntries).CopyTo(parts, 0);
        var n = parts.TakeWhile(parts => parts != null).Count();
        parts[n++] = uChars[0].ToString("x2", culture);
        parts[n++] = uChars[1].ToString("x2", culture);
        parts[n++] = uChars[2].ToString("x2", culture);
        parts[n++] = uChars[3].ToString("x2", culture);

        var createdPath = string.Join('\\', parts, 0, n);
        await client.CreateIfNotExistsAsync(createdPath);

        return createdPath;
    }

    public async Task<bool> DeleteAsync(string fileIdentifier)
    {
        var docClient = await FindContainerClientAsync(fileIdentifier);
        return docClient.BinaryFileInfo().Delete();
    }

    public async Task<IBinaryFileInfo> BinaryFileInfoAsync(string fileIdentifier)
    {
        var docClient = await FindContainerClientAsync(fileIdentifier);
        return docClient.BinaryFileInfo();
    }

    private async Task CreateRootContainerAsync()
    {
        try
        {
            // Create the root container or handle the exception if it already exists
            var container = await client.CreateIfNotExistsAsync("$root");
            if (!string.IsNullOrEmpty(container))
            {
                logger.LogInformation<BlobStorageService>($"Created root container for {client.AccountName}: {container}");
            }
            else
            {
                logger.LogInformation<BlobStorageService>($"Could not create root container for {client.AccountName}.");
            }
        }
        catch (RequestFailedException e)
        {
            logger.LogInformation<BlobStorageService>($"Created root container HTTP error code {e.Status}: {e.ErrorCode}");
        }
    }

    public async Task<string> HealthAsync()
    {
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            if (client == null)
            {
                return "No blob client";
            }

            if (string.IsNullOrEmpty(client.AccountName))
            {
                return "Client has no account";
            }
            // try to connect
            var blobClient = client.GetBlobContainerClient("$root", string.Empty);
            var exists = blobClient.ContainerExists();
            if (!exists)
            {
                await CreateRootContainerAsync();
            }

            return "Healthy";
        }
        catch (Exception e)
        {
            logger.LogError<BlobStorageService>(e.Message);
            return e.Message;
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }

    public async Task<bool> DocumentExistsAsync(string fileIdentifier)
    {
        var docClient = await FindContainerClientAsync(fileIdentifier);
        if (!docClient.ContainerExists())
        {
            return false;
        }

        return docClient.BinaryFileInfo().Exists;
    }
}
