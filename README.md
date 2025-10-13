# N2.Documents

A comprehensive document management library for .NET 9.0 applications using Azure Blob Storage and Azure Table Storage.

[![.NET Build and test](https://github.com/Kaalenco/n2-documents/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Kaalenco/n2-documents/actions/workflows/dotnet.yml)

## Overview

N2.Documents provides a production-ready document management solution with enterprise features including:

- **Azure-based Storage**: Documents stored in Azure Blob Storage with metadata in Azure Table Storage
- **Role-Based Access Control**: Fine-grained permission system with configurable roles
- **Private/Public Documents**: Support for both user-private and role-based public documents
- **Document Organization**: Tag-based categorization and process-based grouping
- **Search Capabilities**: Full-text search across document names and descriptions
- **Audit Trail**: Automatic tracking of creation, modification, and deletion

## Package Information

- **NuGet Package**: `N2.Documents`
- **Current Version**: 1.1.0
- **Target Framework**: .NET 9.0
- **License**: AFL-3.0

## Quick Start

```bash
dotnet add package N2.Documents
```

For detailed setup instructions and usage examples, see the [N2.Documents README](src/N2.Documents/readme.md).

## Project Structure

```
n2-documents/
├── src/
│   ├── N2.Documents/              # Main library
│   │   ├── DocumentService.cs     # Core service implementation
│   │   ├── AzureDocumentRepository.cs  # Metadata storage
│   │   ├── BlobStorageService.cs  # Binary storage
│   │   └── readme.md              # Detailed documentation
│   └── N2.Documents.UnitTests/    # Unit tests
├── docs/                          # Architecture decision records
└── README.md                      # This file
```

## Key Features

### Storage Architecture
- Documents stored hierarchically in Azure Blob Storage
- Metadata cached in-memory and persisted in Azure Table Storage
- Configurable cache lifetime for optimal performance

### Security
- Role-based authorization with configurable valid roles
- Private document support (creator-only access)
- Administrator override for all documents
- Audit logging for all operations

### API Design
- Async/await throughout for scalability
- Tuple return patterns for success/failure handling
- Stream-based upload for memory efficiency
- LINQ-queryable document collections

## Contributing

We welcome contributions! Please follow these guidelines:

### Reporting Issues

When submitting an issue, please include:

1. **Clear Description**: What is the problem or feature request?
2. **Steps to Reproduce**: For bugs, provide detailed steps
3. **Expected Behavior**: What should happen?
4. **Actual Behavior**: What actually happens?
5. **Environment**: .NET version, Azure SDK versions, OS
6. **Code Sample**: Minimal reproducible example if applicable

### Submitting Changes

1. **Fork the Repository**: Create your own fork of the project
2. **Create a Branch**: Use a descriptive branch name
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/issue-description
   ```
3. **Follow Code Standards**:
   - Enable nullable reference types
   - Treat warnings as errors (project enforces this)
   - Follow existing code style and conventions
   - Add XML documentation for public APIs
   - Include unit tests for new features

4. **Write Tests**:
   - Add unit tests in `N2.Documents.UnitTests`
   - Ensure all tests pass before submitting
   - Aim for meaningful test coverage

5. **Update Documentation**:
   - Update `src/N2.Documents/readme.md` for user-facing changes
   - Add architecture decision records in `docs/` for significant decisions
   - Include XML comments for new public APIs

6. **Commit Guidelines**:
   - Write clear, concise commit messages
   - Reference issue numbers where applicable
   - Use conventional commit format when possible:
     ```
     feat: add support for document versioning
     fix: resolve metadata caching issue
     docs: update configuration examples
     ```

7. **Submit Pull Request**:
   - Provide a clear description of the changes
   - Link to related issues
   - Ensure CI builds pass
   - Be responsive to review feedback

### Code Quality Standards

This project enforces strict code quality:
- All .NET analyzers enabled (`AnalysisMode: All`)
- Warnings treated as errors
- Code style enforcement during build
- Documentation file generation required

### Testing

Run all tests before submitting:

```bash
dotnet test
```

For integration tests with Azure Storage, configure:
```json
{
  "DocumentServiceSettings": {
    "StorageConnectionString": "UseDevelopmentStorage=true"
  }
}
```

## Architecture Decisions

Major architectural decisions are documented in the `docs/` directory using Architecture Decision Records (ADR). Current decisions include:

- Choice of Azure Table Storage for metadata
- In-memory caching strategy
- Role-based access control implementation

## License

This project is licensed under the Academic Free License v3.0 (AFL-3.0).

Copyright (c) 2025 Kaalenco - All rights reserved

## Support

For questions, issues, or feature requests, please use the GitHub issue tracker.
