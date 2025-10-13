using Azure.Storage.Blobs;

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
#pragma warning restore CA1308 // use uppercase