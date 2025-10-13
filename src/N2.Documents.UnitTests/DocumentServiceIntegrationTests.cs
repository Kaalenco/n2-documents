using System.IO.Abstractions;

using Azure.Storage.Blobs;

using Microsoft.Extensions.Configuration;

using Moq;

using N2.Core;
using N2.Core.Dms;
using N2.Core.Identity;

using NUnit.Framework;

namespace N2.Documents.UnitTests;

[Category("Integration")]
public class DocumentServiceIntegrationTests
{
    private string connectionString = string.Empty;
    private string tableName = string.Empty;
    private DocumentServiceSettings settings = null!;
    private AzureDocumentRepository repository = null!;
    private BlobStorageService storageService = null!;
    private DocumentService documentService = null!;

    private readonly Guid TestUserId = Guid.NewGuid();
    private readonly Mock<IUserContext> TestUserContext = new();

    private class TestSettingsService(DocumentServiceSettings settings) : ISettingsService
    {
        private readonly DocumentServiceSettings settings = settings;

        public IDirectoryInfo? DirectoryRoot { get; }
        public string SettingsFileName { get; set; } = "config.settings";

        public T GetConfigSettings<T>() where T : class, new()
        {
            if (typeof(T) == typeof(DocumentServiceSettings))
            {
                return (T)(object)settings;
            }
            return new T();
        }

        public TConfig GetConfigSettings<TConfig>(string sectionName) where TConfig : class, new() => throw new NotImplementedException();
        public string GetConnectionString(string name) => throw new NotImplementedException();
        public TValue GetSetting<TValue>(string name, TValue defaultValue) where TValue : struct => throw new NotImplementedException();
        public void Reload<T>() where T : class => throw new NotImplementedException();
    }

    private class NoopLogService : ILogService
    {
        public void LogDebug<T>(string message) { TestContext.Progress.WriteLine($"DEBUG {typeof(T).Name}: {message}"); }
        public void LogInformation<T>(string message) { TestContext.Progress.WriteLine($"INFO {typeof(T).Name}: {message}"); }
        public void LogError<T>(string message) { TestContext.Progress.WriteLine($"ERR {typeof(T).Name}: {message}"); }

        public void LogWarning<T>(string message) => throw new NotImplementedException();
        public void LogCritical<T>(string message) => throw new NotImplementedException();
        public void LogEvent<T>(string message, string category) => throw new NotImplementedException();
    }

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        TestUserContext.Setup(u => u.PublicId).Returns(TestUserId);
        TestUserContext.Setup(u => u.IsAdmin()).Returns(true);

        var config = new ConfigurationBuilder()
            .AddUserSecrets<DocumentServiceIntegrationTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        connectionString =
            config["AzureStorage:BlobConnectionString"] ??
            config["AzureStorage:TableConnectionString"] ??
            config["AzureStorage:ConnectionString"] ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Azure Storage connection string not found (UserSecrets or Environment).");
        }

        tableName = "docs" + Guid.NewGuid().ToString("N").Substring(0, 16);

        settings = new DocumentServiceSettings
        {
            StorageConnectionString = connectionString,
            ValidRoles = new[] { "ADMIN", "USER", "MANAGER" }
        };

        var userContext = TestUserContext.Object;
        var settingsService = new TestSettingsService(settings);
        var logService = new NoopLogService();
        var blobClientProxy = new BlobServiceClientProxy(settingsService);
        storageService = new BlobStorageService(logService, blobClientProxy);
        repository = new AzureDocumentRepository(connectionString, tableName);

        documentService = new DocumentService(storageService, repository, settingsService, userContext, logService);
    }

    [OneTimeTearDown]
    public async Task CleanupAsync()
    {
        if (!string.IsNullOrEmpty(connectionString))
        {
            // Remove table
            try
            {
                var tableSvc = new Azure.Data.Tables.TableServiceClient(connectionString);
                await tableSvc.DeleteTableAsync(tableName);
            }
            catch { }

            // Remove blob container(s) we created (mainly "data")
            try
            {
                //    var blobSvc = new BlobServiceClient(connectionString);
                //    await blobSvc.GetBlobContainerClient("data").DeleteIfExistsAsync();
            }
            catch { }
        }
    }

    private static DocumentInformation NewSaveForm(string fileName, string processName, string[]? roles = null, string[]? tags = null)
        => new()
        {
            FileName = fileName,
            ProcessName = processName,
            Roles = roles ?? new[] { "user" },
            Tags = tags ?? new[] { "tagA" },
            Remarks = "Initial",
            IsEnabled = true,
            DcmiType = AttachmentType.Text
        };

    [Test]
    public async Task SaveDocumentAsync_EndToEnd_PersistsBlobAndMetadata()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        var form = NewSaveForm("sample.txt", "TestProcess", roles: new[] { "admin", "user" });

        var (success, info) = await documentService.SaveDocumentAsync(stream, form);

        Assert.That(success, Is.True);
        Assert.That(info.FileName, Is.EqualTo("sample.txt"));
        Assert.That(info.Extension, Is.EqualTo(".txt"));
        Assert.That(info.Remarks, Is.EqualTo("Initial"));
        Assert.That(info.Roles, Is.EquivalentTo(["ADMIN", "USER"]));

        // Verify repository metadata present
        var saved = repository.DocumentQuery.FirstOrDefault(d => d.OriginalName == "sample.txt");
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.CreatedBy, Is.EqualTo(TestUserId));
        Assert.That(saved.IsPrivate, Is.True); // Service enforces private
        Assert.That(saved.Location, Is.EqualTo(info.DocumentIdentifier));

        // Verify blob exists
        var blobSvc = new BlobServiceClient(connectionString);
        var container = blobSvc.GetBlobContainerClient("data");
        var exists = container.GetBlobClient(BuildBlobName(saved.Location, saved.ProcessName)).Exists();
        Assert.That(exists.Value, Is.True);
    }

    [Test]
    public async Task GetDocumentInformationAsync_Returns_ForAdmin()
    {
        // Save
        using var stream = new MemoryStream(new byte[] { 5, 6, 7 });
        var form = NewSaveForm("getadmin.txt", "AdminProcess");
        await documentService.SaveDocumentAsync(stream, form);

        // Locate saved doc
        var doc = repository.DocumentQuery.First(d => d.OriginalName == "getadmin.txt");

        var (ok, info) = await documentService.GetDocumentInformationAsync(doc.PublicId);

        Assert.That(ok, Is.True);
        Assert.That(info.FileName, Is.EqualTo("getadmin.txt"));
        Assert.That(info.UploadedBy, Is.EqualTo(doc.CreatedBy));
        Assert.That(info.Roles, Is.EquivalentTo(doc.Roles));
    }

    [Test]
    public async Task UpdateDocumentAsync_UpdatesRemarksAndRoles_AsAdmin()
    {
        using var stream = new MemoryStream([9, 9, 9]);
        var form = NewSaveForm("update.txt", "UpdProcess", roles: new[] { "user" });
        await documentService.SaveDocumentAsync(stream, form);
        var doc = repository.DocumentQuery.First(d => d.OriginalName == "update.txt");

        var updateForm = new DocumentInformation
        {
            Remarks = "Changed",
            IsEnabled = doc.IsEnabled,
            Roles = new[] { "manager", "admin" }
        };

        var (ok, updatedInfo) = await documentService.UpdateDocumentAsync(doc.PublicId, updateForm);
        Assert.That(ok, Is.True);
        Assert.That(updatedInfo.Remarks, Is.EqualTo("Changed"));

        var refreshed = repository.DocumentQuery.First(d => d.PublicId == doc.PublicId);
        Assert.That(refreshed.Remarks, Is.EqualTo("Changed"));
        Assert.That(refreshed.Roles, Is.EquivalentTo(new[] { "MANAGER", "ADMIN" }));
    }

    [Test]
    public async Task DeleteDocumentAsync_SetsRemovedFlags_ForAdmin()
    {
        using var stream = new MemoryStream(new byte[] { 0xA });
        var form = NewSaveForm("delete.txt", "DelProcess");
        await documentService.SaveDocumentAsync(stream, form);
        var doc = repository.DocumentQuery.First(d => d.OriginalName == "delete.txt");

        var (ok, msg) = await documentService.DeleteDocumentAsync(doc.PublicId);

        Console.Write(msg);

        Assert.That(ok, Is.True);
        Assert.That(msg, Is.EqualTo("Document deleted"));

        var deleted = repository.DocumentQuery.First(d => d.PublicId == doc.PublicId);
        Assert.That(deleted.IsRemoved, Is.True);
        Assert.That(deleted.IsEnabled, Is.False);

        // Blob still exists (soft delete only)
        var blobSvc = new BlobServiceClient(connectionString);
        var container = blobSvc.GetBlobContainerClient("data");
        var exists = container.GetBlobClient(BuildBlobName(deleted.Location, deleted.ProcessName)).Exists();
        Assert.That(exists.Value, Is.True);
    }

    [Test]
    public async Task FindDocumentsAsync_FiltersPrivateOwnership_AndRoles_ForNonAdmin()
    {
        // Prepare: Private doc (created via service) and public doc (manual insert)
        using var stream = new MemoryStream(new byte[] { 0x1, 0x2 });
        var token = CancellationToken.None;
        var newUserId = Guid.NewGuid();
        var testUserContext = new Mock<IUserContext>();
        testUserContext.Setup(u => u.PublicId).Returns(newUserId);
        testUserContext.Setup(u => u.IsAdmin()).Returns(false);

        var privateForm = NewSaveForm("mine.txt", "ProcA", roles: new[] { "user" });
        await documentService.SaveDocumentAsync(stream, privateForm);
        var myDoc = repository.DocumentQuery.First(d => d.OriginalName == "mine.txt");

        // Create a public document directly (IsPrivate = false)
        var publicDoc = new Document
        {
            Location = "public/location.txt",
            OriginalName = "public.txt",
            Extension = ".txt",
            Remarks = "Public",
            Roles = new[] { "USER" },
            Tags = new[] { "PUB" },
            Size = 10,
            Created = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            IsPrivate = false,
            IsEnabled = true,
            ProcessName = "ProcB",
            DcmiType = AttachmentType.Text
        };
        repository.SaveDocument(publicDoc);
        await repository.CompleteAsync(testUserContext.Object, token);

        var results = (await documentService.FindDocumentsAsync("", new[] { "USER" }, "", false)).ToList();

        // Expect: my private doc (owner) + the public doc (role USER)
        Assert.That(results.Any(r => r.FileName == "mine.txt"), Is.True);
        Assert.That(results.Any(r => r.FileName == "public.txt"), Is.True);
    }

    private static string BuildBlobName(string documentLocation, string processName)
    {
        // documentLocation e.g. "testprocess\\04\\03\\02\\01\\<guid>.txt"
        // Blob path stored as "testprocess/04/03/02/01/<guid>.txt" inside container "data"
        return processName.ToLowerInvariant() + '/' + documentLocation.Replace('\\', '/');
    }
}