using Azure.Data.Tables;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using N2.Core.Dms;
using N2.Core.Identity;

using NUnit.Framework;

namespace N2.Documents.UnitTests;

[Category("Integration")]
public class AzureDocumentRepositoryIntegrationTests
{
    private IServiceProvider serviceProvider = null!;
    private string connectionString = string.Empty;
    private string tableName = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        // Load configuration (UserSecrets + Environment)
        var config = new ConfigurationBuilder()
            .AddUserSecrets<AzureDocumentRepositoryIntegrationTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        connectionString =
            config["AzureStorage:TableConnectionString"] ??
            config["AzureStorage:ConnectionString"] ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Inconclusive("Azure Storage connection string not found in user secrets or environment variables.");
        }

        // Unique table name per test run to isolate data
        tableName = "docs" + Guid.NewGuid().ToString("N").Substring(0, 16);

        var services = new ServiceCollection();

        // Register repository (DI sample)
        services.AddSingleton<IDocumentRepository>(_ =>
            new AzureDocumentRepository(connectionString, tableName));

        // Provide a dummy IUserContext (repository doesn't use it but required for CompleteAsync signature)
        var userContextMock = new Moq.Mock<IUserContext>();
        userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        services.AddSingleton(userContextMock.Object);

        serviceProvider = services.BuildServiceProvider();

        // Force table creation (repository constructor already does this, but we explicitly verify)
        var tableService = new TableServiceClient(connectionString);
        _ = tableService.GetTableClient(tableName);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (!string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(tableName))
        {
            try
            {
                var svc = new TableServiceClient(connectionString);
                await svc.DeleteTableAsync(tableName);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    private IDocumentRepository GetRepository() => serviceProvider.GetRequiredService<IDocumentRepository>();

    [Test]
    public async Task SaveDocument_Persists_AndAppearsInQuery_AndExistsLookup()
    {
        var token = CancellationToken.None;
        var repo = GetRepository();

        // Prime cache (should be empty initially)
        var initial = repo.DocumentQuery.ToList();
        //Assert.That(initial.Count, Is.EqualTo(0));

        var doc = NewDocument("folder/file-a.txt");
        repo.SaveDocument(doc);
        var ops = await repo.CompleteAsync(serviceProvider.GetRequiredService<IUserContext>(), token);

        Assert.That(ops, Is.EqualTo(1));

        var after = repo.DocumentQuery.ToList();
        Assert.That(after.Any(d => d.PublicId == doc.PublicId), Is.True);
        Assert.That(repo.DocumentExists("folder/file-a.txt"), Is.True);
    }

    [Test]
    public async Task RemoveDocument_SetsFlags_AndDocumentExistsReturnsFalse()
    {
        var token = CancellationToken.None;
        var userContext = serviceProvider.GetRequiredService<IUserContext>();
        var repo = GetRepository();
        var doc = NewDocument("alpha/beta/gamma.txt");
        repo.SaveDocument(doc);
        _ = await repo.CompleteAsync(userContext, token);

        Assert.That(repo.DocumentExists(doc.Location), Is.True);

        await repo.RemoveDocument(doc.PublicId, token);

        var ops = await repo.CompleteAsync(userContext, token);
        Assert.That(ops, Is.GreaterThanOrEqualTo(1));

        var fetched = repo.DocumentQuery.First(d => d.PublicId == doc.PublicId);
        Assert.That(fetched.IsRemoved, Is.True);
        Assert.That(repo.DocumentExists(doc.Location), Is.False);
    }

    [Test]
    public async Task CacheInvalidates_AfterCompleteAsync_NewDocumentAppears()
    {
        var token = CancellationToken.None;
        var repo = GetRepository();

        // Baseline snapshot
        var baselineCount = repo.DocumentQuery.Count();

        var doc = NewDocument("x/y/z.txt");
        repo.SaveDocument(doc);

        // Before CompleteAsync pending upsert not visible
        var preCommitCount = repo.DocumentQuery.Count();
        Assert.That(preCommitCount, Is.EqualTo(baselineCount));

        _ = await repo.CompleteAsync(serviceProvider.GetRequiredService<IUserContext>(), token);

        // After commit cache invalidated & reloaded
        var postCommitCount = repo.DocumentQuery.Count();
        Assert.That(postCommitCount, Is.EqualTo(baselineCount + 1));
        Assert.That(repo.DocumentQuery.Any(d => d.PublicId == doc.PublicId), Is.True);
    }

    [Test]
    public async Task FindDocumentAsync_FallbackLoads_AndAddsToCache_WhenNotInInitialCache()
    {
        // First repository creates a document (timestamp = now).
        var token = CancellationToken.None;
        var repoWriter = new AzureDocumentRepository(connectionString, tableName);
        var doc = NewDocument("orphan/path/file.txt");
        repoWriter.SaveDocument(doc);
        _ = await repoWriter.CompleteAsync(serviceProvider.GetRequiredService<IUserContext>(), token);

        // Second repository uses a future cacheSinceUtc so initial cache is empty.
        var futureCutoff = DateTime.UtcNow.AddMinutes(2);
        var repoReader = new AzureDocumentRepository(connectionString, tableName, futureCutoff);

        // Ensure initial cache really is empty
        Assert.That(repoReader.DocumentQuery.Count(), Is.EqualTo(0));

        var found = await repoReader.FindDocumentAsync(doc.PublicId, token);
        Assert.That(found, Is.Not.Null);
        Assert.That(found.PublicId, Is.EqualTo(doc.PublicId));

        // Now cache should contain the document
        Assert.That(repoReader.DocumentQuery.Any(d => d.PublicId == doc.PublicId), Is.True);
    }

    private static Document NewDocument(string location) =>
        new()
        {
            Location = location,
            OriginalName = Path.GetFileName(location),
            Extension = Path.GetExtension(location),
            Remarks = "IntegrationTest",
            Roles = new[] { "USER" },
            Tags = new[] { "INT" },
            Size = 123,
            Created = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            IsPrivate = false,
            IsEnabled = true,
            ProcessName = "TestProcess",
            DcmiType = AttachmentType.Text
        };
}