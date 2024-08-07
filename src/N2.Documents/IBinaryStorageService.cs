namespace N2.Documents;

/// <summary>
/// A generic service for storing binary data
/// </summary>
public interface IBinaryStorageService
{
    /// <summary>
    /// Open a file information object based on a file identifier.
    /// This can be a path or a guid or any other string that can be used as
    /// an identifier.
    /// </summary>
    /// <param name="fileIdentifier">Any identifying string.</param>
    /// <returns>File information.</returns>
    Task<IBinaryFileInfo> BinaryFileInfoAsync(string fileIdentifier);

    /// <summary>
    /// Open a stream for the binary data using the identifier.
    /// </summary>
    /// <param name="data">The data stream</param>
    /// <param name="fileIdentifier">Any identifying string</param>
    /// <param name="metadata">Tags for the file.</param>
    /// <returns>full resource identifier for the file.</returns>
    Task<(Uri identifier, string md5Hash)> CreateDocumentAsync(Stream data, string fileIdentifier, Dictionary<string, string> metadata);

    /// <summary>
    /// Remove a binary file using the file identifier..
    /// </summary>
    /// <param name="fileIdentifier">Any identifying string.</param>
    /// <returns>true if a file was found and could be removed.</returns>
    Task<bool> DeleteAsync(string fileIdentifier);

    /// <summary>
    /// Create a save path for a new document.
    /// </summary>
    /// <param name="basePath">A base identifier.</param>
    /// <param name="uuid">A unique identifier that will be used as part or the path.</param>
    /// <returns>A valid safe path or an empty string if a path could not be created.</returns>
    Task<string> CreateSavePathAsync(string basePath, Guid uuid);

    /// <summary>
    /// Check if the service is healhty.
    /// </summary>
    /// <returns>Healtrh information.</returns>
    Task<string> HealthAsync();

    /// <summary>
    /// Check if the document exists and return true if it does, or false if not.
    /// </summary>
    /// <param name="fileIdentifier"></param>
    /// <returns>True if the file exists.</returns>
    Task<bool> DocumentExistsAsync(string fileIdentifier);
}
