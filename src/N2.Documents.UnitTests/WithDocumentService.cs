using Moq;

using N2.Core;
using N2.Core.Dms;
using N2.Core.Identity;

using NUnit.Framework;

namespace N2.Documents.UnitTests;

#pragma warning disable CS8620 // For unit testing

public class WithDocumentService
{
    private Mock<IBinaryStorageService> _storageServiceMock = null!;
    private Mock<IDocumentRepository> _docRepositoryMock = null!;
    private Mock<ISettingsService> _settingsServiceMock = null!;
    private Mock<IUserContext> _userContextMock = null!;
    private Mock<ILogService> _logServiceMock = null!;
    private DocumentService _sut = null!;
    private DocumentServiceSettings _settings = null!;
    private readonly CancellationToken token = CancellationToken.None;

    private Guid _defaultUserId;

    [SetUp]
    public void SetUp()
    {
        _storageServiceMock = new Mock<IBinaryStorageService>(MockBehavior.Strict);
        _docRepositoryMock = new Mock<IDocumentRepository>(MockBehavior.Strict);
        _settingsServiceMock = new Mock<ISettingsService>(MockBehavior.Strict);
        _userContextMock = new Mock<IUserContext>(MockBehavior.Strict);
        _logServiceMock = new Mock<ILogService>(MockBehavior.Loose);

        _settings = new DocumentServiceSettings
        {
            ValidRoles = new[] { "ADMIN", "USER", "MANAGER" }
        };

        _settingsServiceMock
            .Setup(s => s.GetConfigSettings<DocumentServiceSettings>())
            .Returns(_settings);

        // Provide a default user context so any implicit access to PublicId / IsAdmin() is satisfied.
        _defaultUserId = Guid.NewGuid();
        _userContextMock.Setup(u => u.PublicId).Returns(_defaultUserId);
        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);

        _sut = new DocumentService(
            _storageServiceMock.Object,
            _docRepositoryMock.Object,
            _settingsServiceMock.Object,
            _userContextMock.Object,
            _logServiceMock.Object);
    }

    #region DeleteDocumentAsync Tests

    [Test]
    public async Task DeleteDocumentAsync_ReturnsFalse_WhenDocumentNotFound()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _docRepositoryMock
            .Setup(r => r.FindDocumentAsync(documentId, false, token))
            .ReturnsAsync((Document?)null);

        // Act
        var (success, message) = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(message, Is.EqualTo("Document not found"));
        });
    }

    [Test]
    public async Task DeleteDocumentAsync_DeletesDocument_WhenPrivateAndOwnedByCurrentUser()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var document = new Document
        {
            PublicId = documentId,
            IsPrivate = true,
            CreatedBy = userId,
            IsEnabled = true,
            IsRemoved = false
        };

        _userContextMock.Setup(u => u.PublicId).Returns(userId);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(documentId, false, token)).ReturnsAsync(document);
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        // Act
        var (success, message) = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(message, Is.EqualTo("Document deleted"));
            Assert.That(document.IsEnabled, Is.False);
            Assert.That(document.IsRemoved, Is.True);
            Assert.That(document.Removed, Is.Not.Null);
        });
    }

    [Test]
    public async Task DeleteDocumentAsync_DeletesDocument_WhenUserIsAdmin()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            PublicId = documentId,
            IsPrivate = false,
            CreatedBy = Guid.NewGuid(),
            IsEnabled = true
        };

        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(documentId, false, token)).ReturnsAsync(document);
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        // Act
        var (success, message) = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(message, Is.EqualTo("Document deleted"));
            Assert.That(document.IsRemoved, Is.True);
        });
    }

    [Test]
    public async Task DeleteDocumentAsync_ReturnsNotFound_WhenPrivateDocumentNotOwnedByUser()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            PublicId = documentId,
            IsPrivate = true,
            CreatedBy = Guid.NewGuid()
        };

        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(documentId, false, token)).ReturnsAsync(document);

        // Act
        var (success, message) = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(message, Is.EqualTo("Document not found"));
        });
    }

    [Test]
    public async Task DeleteDocumentAsync_ReturnsNotAuthorized_WhenPublicDocumentAndNotAdmin()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new Document
        {
            PublicId = documentId,
            IsPrivate = false,
            CreatedBy = Guid.NewGuid()
        };

        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(documentId, false, token)).ReturnsAsync(document);

        // Act
        var (success, message) = await _sut.DeleteDocumentAsync(documentId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(message, Is.EqualTo("You are not authorized to delete documents"));
        });
    }

    #endregion DeleteDocumentAsync Tests

    #region GetDocumentInformationAsync Tests

    [Test]
    public async Task GetDocumentInformationAsync_ReturnsFalse_WhenDocumentNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, false, false, true, token))
            .ReturnsAsync((Document?)null);

        // Act
        var (success, _) = await _sut.GetDocumentInformationAsync(id);

        // Assert
        Assert.That(success, Is.False);
    }

    [Test]
    public async Task GetDocumentInformationAsync_ReturnsDocument_WhenUserIsAdmin()
    {
        // Arrange
        var id = Guid.NewGuid();
        var doc = new Document
        {
            PublicId = id,
            Location = "path/to/doc.pdf",
            Roles = new[] { "ADMIN", "USER" },
            Tags = new[] { "important", "draft" },
            OriginalName = "document.pdf",
            Extension = ".pdf",
            Created = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            Size = 1024,
            Remarks = "Test document",
            IsEnabled = true
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, token)).ReturnsAsync(doc);

        // Act
        var (success, info) = await _sut.GetDocumentInformationAsync(id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(info.PublicId, Is.EqualTo(id));
            Assert.That(info.DocumentIdentifier, Is.EqualTo("path/to/doc.pdf"));
            Assert.That(info.FileName, Is.EqualTo("document.pdf"));
            Assert.That(info.Extension, Is.EqualTo(".pdf"));
            Assert.That(info.Size, Is.EqualTo(1024));
            Assert.That(info.Remarks, Is.EqualTo("Test document"));
            Assert.That(info.Roles, Is.EquivalentTo(new[] { "ADMIN", "USER" }));
            Assert.That(info.Tags, Is.EquivalentTo(new[] { "important", "draft" }));
        });
    }

    [Test]
    public async Task GetDocumentInformationAsync_ReturnsDocument_WhenUserIsNotAdmin()
    {
        // Arrange
        var id = Guid.NewGuid();
        var doc = new Document
        {
            PublicId = id,
            Location = "path/to/doc.pdf",
            OriginalName = "document.pdf",
            Extension = ".pdf",
            Roles = Array.Empty<string>(),
            Tags = Array.Empty<string>(),
            Remarks = ""
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, false, false, true, token)).ReturnsAsync(doc);

        // Act
        var (success, info) = await _sut.GetDocumentInformationAsync(id);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(info.PublicId, Is.EqualTo(id));
        });
    }

    #endregion GetDocumentInformationAsync Tests

    #region SaveDocumentAsync Tests

    [Test]
    public void SaveDocumentAsync_ThrowsArgumentNullException_WhenFormDataIsNull()
    {
        using var stream = new MemoryStream();
        Assert.That(async () => await _sut.SaveDocumentAsync(stream, null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void SaveDocumentAsync_ThrowsArgumentNullException_WhenStreamIsNull()
    {
        var form = new DocumentInformation { ProcessName = "test" };
        Assert.That(async () => await _sut.SaveDocumentAsync(null!, form),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void SaveDocumentAsync_ThrowsArgumentException_WhenProcessNameIsEmpty()
    {
        using var stream = new MemoryStream();
        var form = new DocumentInformation { ProcessName = "" };
        Assert.That(async () => await _sut.SaveDocumentAsync(stream, form),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task SaveDocumentAsync_SavesDocument_WithValidData()
    {
        var userId = Guid.NewGuid();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var form = new DocumentInformation
        {
            FileName = "test.pdf",
            ProcessName = "TestProcess",
            Remarks = "Test remarks",
            Roles = new[] { "admin", "user" },
            Tags = new[] { "tag1", "tag2" },
            IsEnabled = true,
            DcmiType = AttachmentType.Text
        };

        _userContextMock.Setup(u => u.PublicId).Returns(userId);
        _storageServiceMock.Setup(s => s.CreateSavePathAsync("Data\\TestProcess", It.IsAny<Guid>()))
            .ReturnsAsync("Data\\TestProcess\\subfolder");
        _storageServiceMock.Setup(s => s.CreateDocumentAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((new Uri("https://test.blob/doc"), "hash123"));
        _docRepositoryMock.Setup(r => r.SaveDocument(It.IsAny<Document>()));
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        var (success, info) = await _sut.SaveDocumentAsync(stream, form);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(info.FileName, Is.EqualTo("test.pdf"));
            Assert.That(info.Extension, Is.EqualTo(".pdf"));
            Assert.That(info.Remarks, Is.EqualTo("Test remarks"));
            Assert.That(info.UploadedBy, Is.EqualTo(userId));
        });

        _docRepositoryMock.Verify(r => r.SaveDocument(It.Is<Document>(d =>
            d.OriginalName == "test.pdf" &&
            d.IsPrivate &&
            d.CreatedBy == userId &&
            d.ProcessName == "TestProcess" &&
            d.Roles.SequenceEqual(new[] { "ADMIN", "USER" }))), Times.Once);
    }

    [Test]
    public async Task SaveDocumentAsync_FiltersInvalidRoles()
    {
        var userId = Guid.NewGuid();
        using var stream = new MemoryStream([1, 2, 3]);
        var form = new DocumentInformation
        {
            FileName = "test.pdf",
            ProcessName = "TestProcess",
            Roles = new[] { "admin", "invalid_role", "user" },
            Tags = Array.Empty<string>()
        };

        _userContextMock.Setup(u => u.PublicId).Returns(userId);
        _storageServiceMock.Setup(s => s.CreateSavePathAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync("Data\\TestProcess\\subfolder");
        _storageServiceMock.Setup(s => s.CreateDocumentAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((new Uri("https://test.blob/doc"), "hash"));
        _docRepositoryMock.Setup(r => r.SaveDocument(It.IsAny<Document>()));
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        var (success, _) = await _sut.SaveDocumentAsync(stream, form);

        Assert.That(success, Is.True);
        _docRepositoryMock.Verify(r => r.SaveDocument(It.Is<Document>(d =>
            d.Roles.SequenceEqual(new[] { "ADMIN", "USER" }))), Times.Once);
    }

    #endregion SaveDocumentAsync Tests

    #region UpdateDocumentAsync Tests

    [Test]
    public async Task UpdateDocumentAsync_ReturnsFalse_WhenDocumentNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        var form = new DocumentInformation { Remarks = "Updated" };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, false, It.IsAny<Guid>(), token))
            .ReturnsAsync((Document?)null);

        // Act
        var (success, _) = await _sut.UpdateDocumentAsync(id, form);

        // Assert
        Assert.That(success, Is.False);
    }

    [Test]
    public async Task UpdateDocumentAsync_UpdatesDocument_WhenUserIsAdmin()
    {
        // Arrange
        var id = Guid.NewGuid();
        var doc = new Document
        {
            PublicId = id,
            Remarks = "Old remarks",
            IsEnabled = false,
            Roles = new[] { "USER" },
            Location = "path/doc.pdf",
            OriginalName = "doc.pdf",
            Extension = ".pdf"
        };
        var form = new DocumentInformation
        {
            Remarks = "New remarks",
            IsEnabled = true,
            Roles = new[] { "admin", "manager" }
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, token)).ReturnsAsync(doc);
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        // Act
        var (success, info) = await _sut.UpdateDocumentAsync(id, form);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(doc.Remarks, Is.EqualTo("New remarks"));
            Assert.That(doc.IsEnabled, Is.True);
            Assert.That(doc.Roles, Is.EquivalentTo(new[] { "ADMIN", "MANAGER" }));
            Assert.That(info.Remarks, Is.EqualTo("New remarks"));
        });
    }

    [Test]
    public async Task UpdateDocumentAsync_UpdatesDocument_WhenUserIsOwner()
    {
        // Arrange
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var doc = new Document
        {
            PublicId = id,
            CreatedBy = userId,
            Remarks = "Old",
            Roles = new[] { "USER" },
            Location = "path",
            OriginalName = "file.pdf",
            Extension = ".pdf"
        };
        var form = new DocumentInformation
        {
            Remarks = "Updated",
            Roles = new[] { "user" }
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _userContextMock.Setup(u => u.PublicId).Returns(userId);
        _docRepositoryMock.Setup(r => r.FindDocumentAsync(id, false, userId, token)).ReturnsAsync(doc);
        _docRepositoryMock.Setup(r => r.CompleteAsync(_userContextMock.Object, token)).ReturnsAsync(1);

        // Act
        var (success, info) = await _sut.UpdateDocumentAsync(id, form);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(doc.Remarks, Is.EqualTo("Updated"));
            Assert.That(info.Remarks, Is.EqualTo("Updated"));
        });
    }

    #endregion UpdateDocumentAsync Tests

    #region FindDocumentsAsync Tests

    [Test]
    public void FindDocumentsAsync_ThrowsArgumentNullException_WhenSearchIsNull()
    {
        // Act & Assert
        Assert.That(async () =>
            await _sut.FindDocumentsAsync(null!, new[] { "USER" }, "", false),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void FindDocumentsAsync_ThrowsArgumentNullException_WhenRolesIsNull()
    {
        // Act & Assert
        Assert.That(async () =>
            await _sut.FindDocumentsAsync("", null!, "", false),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public async Task FindDocumentsAsync_ReturnsAllDocuments_WhenUserIsAdmin()
    {
        // Arrange
        var d1 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc1.pdf",
            OriginalName = "doc1.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "Doc 1",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            Created = DateTime.UtcNow
        };
        var d2 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc2.pdf",
            OriginalName = "doc2.pdf",
            Extension = ".pdf",
            Roles = new[] { "ADMIN" },
            Tags = Array.Empty<string>(),
            Remarks = "Doc 2",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = true,
            Created = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _docRepositoryMock.Setup(r => r.DocumentQuery).Returns(new[] { d1, d2 }.AsQueryable());

        // Act
        var results = await _sut.FindDocumentsAsync("", new[] { "USER" }, "", false);

        // Assert
        Assert.That(results.Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task FindDocumentsAsync_FiltersDocumentsBySearch()
    {
        // Arrange
        var d1 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc1.pdf",
            OriginalName = "important.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "Important document",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            Created = DateTime.UtcNow
        };
        var d2 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc2.pdf",
            OriginalName = "regular.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "Regular document",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            Created = DateTime.UtcNow
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _docRepositoryMock.Setup(r => r.DocumentQuery).Returns(new[] { d1, d2 }.AsQueryable());

        // Act
        var results = await _sut.FindDocumentsAsync("important", new[] { "USER" }, "", false);

        // Assert
        var list = results.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].FileName, Is.EqualTo("important.pdf"));
        });
    }

    [Test]
    public async Task FindDocumentsAsync_ReturnsPrivateDocuments_OnlyForOwner()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "my-private.pdf",
            OriginalName = "my-private.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = true,
            CreatedBy = userId,
            Created = DateTime.UtcNow
        };
        var others = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "other-private.pdf",
            OriginalName = "other-private.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = true,
            CreatedBy = other,
            Created = DateTime.UtcNow
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(false);
        _userContextMock.Setup(u => u.PublicId).Returns(userId);
        _docRepositoryMock.Setup(r => r.DocumentQuery).Returns(new[] { mine, others }.AsQueryable());

        // Act
        var results = await _sut.FindDocumentsAsync("", new[] { "USER" }, "", false);

        // Assert
        var list = results.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].FileName, Is.EqualTo("my-private.pdf"));
        });
    }

    [Test]
    public async Task FindDocumentsAsync_FiltersDocumentsByProcessName()
    {
        // Arrange
        var d1 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc1.pdf",
            OriginalName = "doc1.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            ProcessName = "Process1",
            Created = DateTime.UtcNow
        };
        var d2 = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "doc2.pdf",
            OriginalName = "doc2.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            ProcessName = "Process2",
            Created = DateTime.UtcNow
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        _docRepositoryMock.Setup(r => r.DocumentQuery).Returns(new[] { d1, d2 }.AsQueryable());

        // Act
        var results = await _sut.FindDocumentsAsync("", new[] { "USER" }, "Process1", false);

        // Assert
        var list = results.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].FileName, Is.EqualTo("doc1.pdf"));
        });
    }

    [Test]
    public async Task FindDocumentsAsync_ExcludesInactiveDocuments_WhenShowInactiveIsFalse()
    {
        // Arrange
        var active = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "active.pdf",
            OriginalName = "active.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = true,
            IsPrivate = false,
            Created = DateTime.UtcNow
        };
        var inactive = new Document
        {
            PublicId = Guid.NewGuid(),
            Location = "inactive.pdf",
            OriginalName = "inactive.pdf",
            Extension = ".pdf",
            Roles = new[] { "USER" },
            Tags = Array.Empty<string>(),
            Remarks = "",
            IsRemoved = false,
            IsEnabled = false,
            IsPrivate = false,
            Created = DateTime.UtcNow
        };

        _userContextMock.Setup(u => u.IsAdmin()).Returns(true);
        _userContextMock.Setup(u => u.PublicId).Returns(Guid.NewGuid());
        var mockData = new[] { active, inactive };
        _docRepositoryMock.Setup(r => r.DocumentQuery).Returns(mockData.AsQueryable());

        // Act
        var results = await _sut.FindDocumentsAsync("", new[] { "USER" }, "", false);

        // Assert
        var list = results.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].FileName, Is.EqualTo("active.pdf"));
        });
    }

    #endregion FindDocumentsAsync Tests
}