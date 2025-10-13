using Azure.Storage.Blobs;

namespace N2.Documents;

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
#pragma warning restore CA1308 // use uppercase