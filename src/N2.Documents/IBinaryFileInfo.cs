namespace N2.Documents;

/// <summary>
/// Abstraction for a binary file reader.
/// </summary>
public interface IBinaryFileInfo
{
    bool Exists { get; }
    string FileName { get; }
    string Extension { get; }

    Stream OpenRead();

    /// <summary>
    /// Upload the stream to the storage and return a hash of the content.
    /// </summary>
    /// <param name="data">Binary stream.</param>
    /// <returns>
    /// MD5 hash for the content so when retrieving the data,
    /// you can validate if the content was modifiued.
    /// </returns>
    Task<string> UploadAsync(Stream data);

    bool Delete();

    string ContentType();
}
