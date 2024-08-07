namespace N2.Documents;

public class DocumentServiceSettings
{
    public string StorageConnectionString { get; set; } = string.Empty;
    public IEnumerable<string> ValidRoles { get; set; } = [];
}
