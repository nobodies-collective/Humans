using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;
using Octokit;
using Humans.Application.Interfaces;
using Humans.Domain.Entities;
using Humans.Infrastructure.Configuration;
using Humans.Infrastructure.Data;

namespace Humans.Infrastructure.Services;

/// <summary>
/// Service for syncing legal documents from a GitHub repository.
/// Supports both folder-based multi-language discovery and legacy single-file paths.
/// </summary>
public class LegalDocumentSyncService : ILegalDocumentSyncService
{
    private readonly HumansDbContext _dbContext;
    private readonly GitHubClient _gitHubClient;
    private readonly GitHubSettings _settings;
    private readonly IClock _clock;
    private readonly ILogger<LegalDocumentSyncService> _logger;

    // Matches files like "name.md" (canonical es), "name-en.md", "name-de.md"
    private static readonly Regex LanguageFilePattern = new(
        @"^(?<name>[A-Za-z0-9_-]+?)(?:-(?<lang>[a-z]{2}))?\.md$",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public LegalDocumentSyncService(
        HumansDbContext dbContext,
        IOptions<GitHubSettings> settings,
        IClock clock,
        ILogger<LegalDocumentSyncService> logger)
    {
        _dbContext = dbContext;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;

        _gitHubClient = new GitHubClient(new ProductHeaderValue("NobodiesHumans"));

        if (!string.IsNullOrEmpty(_settings.AccessToken))
        {
            _gitHubClient.Credentials = new Credentials(_settings.AccessToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegalDocument>> SyncAllDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting sync of all legal documents from {Owner}/{Repo}",
            _settings.Owner, _settings.Repository);

        var updatedDocuments = new List<LegalDocument>();

        // Iterate active documents from database instead of config
        var activeDocuments = await _dbContext.LegalDocuments
            .Include(d => d.Versions)
            .Where(d => d.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var document in activeDocuments)
        {
            try
            {
                var updated = await SyncSingleDocumentAsync(document, cancellationToken);
                if (updated)
                {
                    updatedDocuments.Add(document);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing document {DocumentName} ({DocumentId})",
                    document.Name, document.Id);
            }
        }

        return updatedDocuments;
    }

    /// <inheritdoc />
    public async Task<bool> SyncDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.LegalDocuments
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found", documentId);
            return false;
        }

        return await SyncSingleDocumentAsync(document, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegalDocument>> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var documentsWithUpdates = new List<LegalDocument>();

        var documents = await _dbContext.LegalDocuments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            try
            {
                var checkPath = !string.IsNullOrEmpty(document.GitHubFolderPath)
                    ? await GetCanonicalFilePathAsync(document.GitHubFolderPath, cancellationToken)
                    : null;

                if (string.IsNullOrEmpty(checkPath))
                {
                    continue;
                }

                var latestSha = await GetLatestCommitShaAsync(checkPath, cancellationToken);
                if (latestSha != null && !string.Equals(latestSha, document.CurrentCommitSha, StringComparison.Ordinal))
                {
                    documentsWithUpdates.Add(document);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking for updates to {DocumentName}", document.Name);
            }
        }

        return documentsWithUpdates;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegalDocument>> GetActiveDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LegalDocuments
            .AsNoTracking()
            .Include(d => d.Versions)
            .Where(d => d.IsActive)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentVersion>> GetRequiredVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetCurrentInstant();

        // Get current versions of required documents
        var requiredDocuments = await _dbContext.LegalDocuments
            .AsNoTracking()
            .Include(d => d.Versions)
            .Where(d => d.IsActive && d.IsRequired)
            .ToListAsync(cancellationToken);

        return requiredDocuments
            .Select(d => d.Versions
                .Where(v => v.EffectiveFrom <= now)
                .MaxBy(v => v.EffectiveFrom))
            .Where(v => v != null)
            .Cast<DocumentVersion>()
            .ToList();
    }

    /// <inheritdoc />
    public async Task<DocumentVersion?> GetVersionByIdAsync(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentVersions
            .AsNoTracking()
            .Include(v => v.LegalDocument)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);
    }

    /// <summary>
    /// Syncs a single document from GitHub using folder-based discovery.
    /// </summary>
    private async Task<bool> SyncSingleDocumentAsync(
        LegalDocument document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(document.GitHubFolderPath))
        {
            _logger.LogWarning("Document {Name} ({Id}) has no GitHubFolderPath configured",
                document.Name, document.Id);
            return false;
        }

        return await SyncFolderBasedDocumentAsync(document, cancellationToken);
    }

    /// <summary>
    /// Discovers language files in a folder and syncs content into the Content dictionary.
    /// Convention: {name}.md → Spanish ("es", canonical), {name}-en.md → English, etc.
    /// </summary>
    private async Task<bool> SyncFolderBasedDocumentAsync(
        LegalDocument document,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Syncing document {Name} from folder {Folder}",
            document.Name, document.GitHubFolderPath);

        // Discover language files in the folder
        var languageFiles = await DiscoverLanguageFilesAsync(document.GitHubFolderPath!, cancellationToken);

        if (languageFiles.Count == 0)
        {
            _logger.LogWarning("No markdown files found in folder {Folder} for document {Name}",
                document.GitHubFolderPath, document.Name);
            return false;
        }

        // The canonical file (no language suffix) is Spanish
        var canonicalEntry = languageFiles.FirstOrDefault(kv =>
            string.Equals(kv.Key, "es", StringComparison.Ordinal));

        if (canonicalEntry.Value == null)
        {
            _logger.LogWarning("No canonical (Spanish) file found in folder {Folder}", document.GitHubFolderPath);
            return false;
        }

        // Fetch canonical file to get commit SHA
        string canonicalContent;
        string commitSha;
        try
        {
            var result = await GetFileContentAsync(canonicalEntry.Value, cancellationToken);
            canonicalContent = result.Content;
            commitSha = result.Sha;
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Canonical file not found at {Path}", canonicalEntry.Value);
            return false;
        }

        var now = _clock.GetCurrentInstant();

        // Check if content has changed
        if (string.Equals(document.CurrentCommitSha, commitSha, StringComparison.Ordinal))
        {
            _logger.LogDebug("Document {Name} is up to date (SHA: {Sha})", document.Name, commitSha);
            document.LastSyncedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        // Fetch all language files
        var content = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (lang, path) in languageFiles)
        {
            try
            {
                var file = await GetFileContentAsync(path, cancellationToken);
                content[lang] = file.Content;
            }
            catch (NotFoundException)
            {
                _logger.LogDebug("Language file not found at {Path}", path);
            }
        }

        // Create new version
        var isNew = document.Versions.Count == 0;
        var versionNumber = $"v{document.Versions.Count + 1}.0";
        var newVersion = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            LegalDocumentId = document.Id,
            VersionNumber = versionNumber,
            CommitSha = commitSha,
            Content = content,
            EffectiveFrom = now,
            RequiresReConsent = !isNew,
            CreatedAt = now,
            ChangesSummary = isNew ? "Initial version" : "Updated from GitHub"
        };

        document.Versions.Add(newVersion);
        document.CurrentCommitSha = commitSha;
        document.LastSyncedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Synced document {Name} version {Version} (SHA: {Sha}, languages: {Languages})",
            document.Name, versionNumber, commitSha, string.Join(", ", content.Keys));

        return true;
    }

    /// <summary>
    /// Lists files in a GitHub folder and maps them to language codes using naming convention.
    /// </summary>
    private async Task<Dictionary<string, string>> DiscoverLanguageFilesAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        var languageFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var contents = await _gitHubClient.Repository.Content.GetAllContentsByRef(
                _settings.Owner,
                _settings.Repository,
                folderPath.TrimEnd('/'),
                _settings.Branch);

            string? canonicalBaseName = null;

            foreach (var item in contents)
            {
                if (item.Type != ContentType.File)
                {
                    continue;
                }

                var match = LanguageFilePattern.Match(item.Name);
                if (!match.Success)
                {
                    continue;
                }

                var baseName = match.Groups["name"].Value;
                var langGroup = match.Groups["lang"];
                var lang = langGroup.Success ? langGroup.Value.ToLowerInvariant() : "es"; // no suffix = Spanish (canonical)

                // Track the first canonical base name found
                canonicalBaseName ??= baseName;

                // Only include files matching the same base name
                if (string.Equals(baseName, canonicalBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    languageFiles[lang] = item.Path;
                }
            }
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Folder not found in GitHub: {FolderPath}", folderPath);
        }

        return languageFiles;
    }

    /// <summary>
    /// Gets the canonical file path for a folder (first .md file without language suffix).
    /// </summary>
    private async Task<string?> GetCanonicalFilePathAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        var files = await DiscoverLanguageFilesAsync(folderPath, cancellationToken);
        return files.GetValueOrDefault("es");
    }

    private async Task<(string Content, string Sha)> GetFileContentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var contents = await _gitHubClient.Repository.Content.GetAllContentsByRef(
            _settings.Owner,
            _settings.Repository,
            path,
            _settings.Branch);

        var file = contents.FirstOrDefault()
            ?? throw new NotFoundException("File not found", System.Net.HttpStatusCode.NotFound);

        // Content is base64 encoded for files
        var content = file.Content;
        if (string.Equals(file.Encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Convert.FromBase64String(file.Content);
            content = System.Text.Encoding.UTF8.GetString(bytes);
        }

        return (content, file.Sha);
    }

    private async Task<string?> GetLatestCommitShaAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var commits = await _gitHubClient.Repository.Commit.GetAll(
                _settings.Owner,
                _settings.Repository,
                new CommitRequest { Path = path, Sha = _settings.Branch },
                new ApiOptions { PageCount = 1, PageSize = 1 });

            return commits.FirstOrDefault()?.Sha;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting latest commit SHA for {Path}", path);
            return null;
        }
    }
}
