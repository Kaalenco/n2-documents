using Moq;

using N2.Core;
using N2.Documents.Exceptions;

using NUnit.Framework;

namespace N2.Documents.UnitTests;

public class WithBlobStorageService
{
    private Mock<IBlobServiceClient> _blobServiceMock = null!;
    private Mock<IBlobContainerClient> _containerMock = null!;
    private Mock<ILogService> _logMock = null!;
    private BlobStorageService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _blobServiceMock = new Mock<IBlobServiceClient>(MockBehavior.Strict);
        _containerMock = new Mock<IBlobContainerClient>(MockBehavior.Strict);
        _logMock = new Mock<ILogService>(MockBehavior.Loose); // logging not asserted except in error case

        _sut = new BlobStorageService(_logMock.Object, _blobServiceMock.Object);
    }

    [Test]
    public async Task CreateDocumentAsync_Uploads_WhenContainerExists()
    {
        // Arrange
        var fileIdentifier = "FolderA/Sub/FILE.txt";
        var lowered = fileIdentifier.ToLowerInvariant().Replace('/', '\\');          // foldera\sub\file.txt
        var lastSep = lowered.LastIndexOf('\\');
        var expectedContainerPath = lowered[..lastSep];                              // foldera\sub
        var expectedFileName = lowered[(lastSep + 1)..];                             // file.txt
        var meta = new Dictionary<string, string> { { "k", "v" } };
        var expectedUri = new Uri("https://acc.blob.core.windows.net/foldera/sub/file.txt");
        var expectedHash = "ZmFrZU1ENC==";

        _blobServiceMock
            .Setup(s => s.CreateIfNotExistsAsync(expectedContainerPath))
            .ReturnsAsync("ignored");

        _blobServiceMock
            .Setup(s => s.GetBlobContainerClient(expectedContainerPath, expectedFileName))
            .Returns(_containerMock.Object);

        _containerMock.Setup(c => c.ContainerExists()).Returns(true);
        _containerMock
            .Setup(c => c.UploadBlobAsync(It.IsAny<Stream>(), meta))
            .ReturnsAsync((expectedUri, expectedHash));

        using var data = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var (identifier, md5) = await _sut.CreateDocumentAsync(data, fileIdentifier, meta);

        // Assert
        Assert.That(identifier, Is.EqualTo(expectedUri));
        Assert.That(md5, Is.EqualTo(expectedHash));

        _blobServiceMock.Verify(s => s.CreateIfNotExistsAsync(expectedContainerPath), Times.Once);
        _blobServiceMock.Verify(s => s.GetBlobContainerClient(expectedContainerPath, expectedFileName), Times.Once);
        _containerMock.Verify(c => c.ContainerExists(), Times.Once);
        _containerMock.Verify(c => c.UploadBlobAsync(It.IsAny<Stream>(), meta), Times.Once);
    }

    [Test]
    public void CreateDocumentAsync_Throws_WhenContainerMissing()
    {
        // Arrange
        var fileIdentifier = "a/b/c.txt";
        var lowered = fileIdentifier.ToLowerInvariant().Replace('/', '\\'); // a\b\c.txt
        var lastSep = lowered.LastIndexOf('\\');
        var expectedContainerPath = lowered[..lastSep];
        var expectedFileName = lowered[(lastSep + 1)..];

        _blobServiceMock
            .Setup(s => s.CreateIfNotExistsAsync(expectedContainerPath))
            .ReturnsAsync("ignored");
        _blobServiceMock
            .Setup(s => s.GetBlobContainerClient(expectedContainerPath, expectedFileName))
            .Returns(_containerMock.Object);

        _containerMock.Setup(c => c.ContainerExists()).Returns(false);
        _containerMock.SetupGet(c => c.Container).Returns(expectedContainerPath);
        _containerMock.SetupGet(c => c.FileName).Returns(expectedFileName);

        using var data = new MemoryStream(new byte[] { 9 });

        // Act & Assert
        var ex = Assert.ThrowsAsync<N2DocumentException>(async () =>
            await _sut.CreateDocumentAsync(data, fileIdentifier, new()));
        Assert.That(ex!.Message, Does.Contain("Expected existing container"));
    }

    [Test]
    public async Task CreateSavePathAsync_AppendsFourHexBuckets_AndCreatesContainer()
    {
        // Arrange
        var basePath = "Root/Files";
        var guid = new Guid("01020304-0000-0000-0000-000000000000"); // first 4 bytes = 04 03 02 01
        var bytes = guid.ToByteArray();
        var expectedSuffix = string.Join("\\", bytes.Take(4).Select(b => b.ToString("x2")));
        string? capturedCreatedPath = null;

        _blobServiceMock
            .Setup(s => s.CreateIfNotExistsAsync(It.IsAny<string>()))
            .Callback<string>(p => capturedCreatedPath = p)
            .ReturnsAsync("uri");

        // Act
        var created = await _sut.CreateSavePathAsync(basePath, guid);

        // Assert
        Assert.That(created, Is.EqualTo(capturedCreatedPath));
        Assert.That(created, Does.StartWith("root\\files\\"));
        Assert.That(created, Does.EndWith(expectedSuffix));
        Assert.That(created.Split('\\').Length, Is.EqualTo(6)); // 2 base + 4 hex
    }

    [Test]
    public async Task DeleteAsync_DelegatesToBinaryFileInfoDelete()
    {
        // Arrange
        var fileIdentifier = "x/y/z.bin";
        PrepareForFileOperations(fileIdentifier,
            containerExists: true,
            fileExists: true,
            deleteResult: true,
            out var binaryMock);

        // Act
        var result = await _sut.DeleteAsync(fileIdentifier);

        // Assert
        Assert.That(result, Is.True);
        binaryMock.Verify(b => b.Delete(), Times.Once);
    }

    [Test]
    public async Task BinaryFileInfoAsync_ReturnsUnderlyingBinaryInfo()
    {
        var fileIdentifier = "a/b/thing.dat";
        PrepareForFileOperations(fileIdentifier,
            containerExists: true,
            fileExists: true,
            deleteResult: true,
            out var binaryMock);

        var info = await _sut.BinaryFileInfoAsync(fileIdentifier);

        Assert.That(info, Is.SameAs(binaryMock.Object));
    }

    [Test]
    public async Task DocumentExistsAsync_ReturnsFalse_WhenContainerMissing()
    {
        var fileIdentifier = "c/d/e.txt";
        PrepareForFileOperations(fileIdentifier,
            containerExists: false,
            fileExists: false,
            deleteResult: false,
            out _);

        var exists = await _sut.DocumentExistsAsync(fileIdentifier);

        Assert.That(exists, Is.False);
    }

    [Test]
    public async Task DocumentExistsAsync_ReturnsTrue_WhenFileExists()
    {
        var fileIdentifier = "k/l/m.txt";
        PrepareForFileOperations(fileIdentifier,
            containerExists: true,
            fileExists: true,
            deleteResult: true,
            out _);

        var exists = await _sut.DocumentExistsAsync(fileIdentifier);

        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task HealthAsync_CreatesRootContainer_WhenMissing()
    {
        // Arrange
        var rootContainer = new Mock<IBlobContainerClient>();
        rootContainer.Setup(c => c.ContainerExists()).Returns(false);

        _blobServiceMock.Setup(s => s.AccountName).Returns("acct");
        _blobServiceMock.Setup(s => s.GetBlobContainerClient("$root", string.Empty)).Returns(rootContainer.Object);
        _blobServiceMock.Setup(s => s.CreateIfNotExistsAsync("$root")).ReturnsAsync("root-uri");

        // Act
        var health = await _sut.HealthAsync();

        // Assert
        Assert.That(health, Is.EqualTo("Healthy"));
        _blobServiceMock.Verify(s => s.CreateIfNotExistsAsync("$root"), Times.Once);
    }

    [Test]
    public async Task HealthAsync_ReturnsMessage_OnException()
    {
        // Arrange
        var exMessage = "boom";
        _blobServiceMock.Setup(s => s.AccountName).Returns("acct");
        _blobServiceMock.Setup(s => s.GetBlobContainerClient("$root", string.Empty))
            .Throws(new InvalidOperationException(exMessage));
        // Act
        var health = await _sut.HealthAsync();

        // Assert
        Assert.That(health, Is.EqualTo(exMessage));
    }

    private void PrepareForFileOperations(
        string fileIdentifier,
        bool containerExists,
        bool fileExists,
        bool deleteResult,
        out Mock<IBinaryFileInfo> binaryMock)
    {
        var lowered = fileIdentifier.ToLowerInvariant().Replace('/', '\\');
        var lastSep = lowered.LastIndexOf('\\');
        var containerPath = lowered[..lastSep];
        var fileName = lowered[(lastSep + 1)..];

        _blobServiceMock
            .Setup(s => s.CreateIfNotExistsAsync(containerPath))
            .ReturnsAsync("ignored");
        _blobServiceMock
            .Setup(s => s.GetBlobContainerClient(containerPath, fileName))
            .Returns(_containerMock.Object);

        _containerMock.Setup(c => c.ContainerExists()).Returns(containerExists);

        binaryMock = new Mock<IBinaryFileInfo>();
        binaryMock.SetupGet(b => b.Exists).Returns(fileExists);
        binaryMock.Setup(b => b.Delete()).Returns(deleteResult);

        _containerMock.Setup(c => c.BinaryFileInfo()).Returns(binaryMock.Object);
    }
}