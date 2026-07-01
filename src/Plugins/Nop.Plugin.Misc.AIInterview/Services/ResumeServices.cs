using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Catalog;
using Nop.Core.Domain.Logging;
using Nop.Services.Media;
using NopLogger = Nop.Services.Logging.ILogger;
using UglyToad.PdfPig;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class ResumeFileService : IResumeFileService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx"
    };

    private const long MaxResumeBytes = 5 * 1024 * 1024;

    private readonly IDownloadService _downloadService;

    public ResumeFileService(IDownloadService downloadService)
    {
        _downloadService = downloadService;
    }

    public ResumeFileValidationResult ValidateResumeFile(IFormFile file)
    {
        if (file == null)
        {
            return new ResumeFileValidationResult
            {
                Success = false,
                ErrorCode = "missing_file",
                ErrorMessage = "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB."
            };
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (!SupportedExtensions.Contains(extension) || file.Length <= 0 || file.Length > MaxResumeBytes)
        {
            return new ResumeFileValidationResult
            {
                Success = false,
                ErrorCode = "invalid_file",
                ErrorMessage = "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB."
            };
        }

        return new ResumeFileValidationResult { Success = true };
    }

    public async Task<ResumeFileStoreResult> StoreResumeAsync(IFormFile file)
    {
        var validation = ValidateResumeFile(file);
        if (!validation.Success)
        {
            return new ResumeFileStoreResult
            {
                Success = false,
                ErrorCode = validation.ErrorCode,
                ErrorMessage = validation.ErrorMessage
            };
        }

        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadBinary = await _downloadService.GetDownloadBitsAsync(file),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? GetContentType(file.FileName) : file.ContentType,
            Filename = file.FileName,
            Extension = Path.GetExtension(file.FileName),
            IsNew = true
        };
        await _downloadService.InsertDownloadAsync(download);

        return new ResumeFileStoreResult
        {
            Success = true,
            DownloadId = download.Id
        };
    }

    private static string GetContentType(string fileName)
    {
        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
}

public class ResumeTextExtractionService : IResumeTextExtractionService
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private const int MaxExtractedTextLength = 12000;

    public Task<ResumeTextExtractionResult> ExtractTextAsync(Download download)
    {
        if (download == null)
        {
            return Task.FromResult(new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "missing_download",
                ErrorMessage = "Resume file could not be loaded."
            });
        }

        var extension = Path.GetExtension(download.Filename ?? download.Extension ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension))
            extension = download.Extension;

        if (download.DownloadBinary == null || download.DownloadBinary.Length == 0)
        {
            return Task.FromResult(new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "missing_binary",
                ErrorMessage = "Resume file is empty."
            });
        }

        try
        {
            var extractedText = extension?.Equals(".pdf", StringComparison.OrdinalIgnoreCase) == true
                ? ExtractPdfText(download.DownloadBinary)
                : extension?.Equals(".docx", StringComparison.OrdinalIgnoreCase) == true
                    ? ExtractDocxText(download.DownloadBinary)
                    : null;

            if (extractedText == null)
            {
                return Task.FromResult(new ResumeTextExtractionResult
                {
                    Success = false,
                    ErrorCode = "unsupported_extension",
                    ErrorMessage = "Only PDF and DOCX resumes are supported."
                });
            }

            var normalized = NormalizeWhitespace(extractedText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Task.FromResult(new ResumeTextExtractionResult
                {
                    Success = false,
                    ErrorCode = "empty_text",
                    ErrorMessage = "Resume text could not be extracted."
                });
            }

            return Task.FromResult(new ResumeTextExtractionResult
            {
                Success = true,
                Text = normalized.Length <= MaxExtractedTextLength ? normalized : normalized[..MaxExtractedTextLength]
            });
        }
        catch (OpenXmlPackageException ex)
        {
            return Task.FromResult(new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "extraction_failed",
                ErrorMessage = "Resume text could not be extracted.",
                ExceptionType = ex.GetType().Name,
                DiagnosticMessage = BuildExtractionDiagnosticMessage(extension, ex.Message, invalidPackage: true)
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "extraction_failed",
                ErrorMessage = "Resume text could not be extracted.",
                ExceptionType = ex.GetType().Name,
                DiagnosticMessage = BuildExtractionDiagnosticMessage(extension, ex.Message)
            });
        }
    }

    private static string ExtractPdfText(byte[] binary)
    {
        using var stream = new MemoryStream(binary, writable: false);
        using var document = PdfDocument.Open(stream);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static string ExtractDocxText(byte[] binary)
    {
        using var stream = new MemoryStream(binary, writable: false);
        using var document = WordprocessingDocument.Open(stream, false);
        var parts = new List<string>
        {
            ExtractOpenXmlText(document.MainDocumentPart?.Document),
            string.Join(" ", (document.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>()).Select(part => ExtractOpenXmlText(part.Header))),
            string.Join(" ", (document.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>()).Select(part => ExtractOpenXmlText(part.Footer))),
            ExtractOpenXmlText(document.MainDocumentPart?.FootnotesPart?.Footnotes),
            ExtractOpenXmlText(document.MainDocumentPart?.EndnotesPart?.Endnotes),
            ExtractOpenXmlText(document.MainDocumentPart?.WordprocessingCommentsPart?.Comments)
        };

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ExtractOpenXmlText(OpenXmlPartRootElement root)
    {
        return root == null
            ? string.Empty
            : string.Join(" ", root.Descendants<Text>().Select(text => text.Text));
    }

    private static string BuildExtractionDiagnosticMessage(string extension, string message, bool invalidPackage = false)
    {
        if (extension?.Equals(".pdf", StringComparison.OrdinalIgnoreCase) == true)
            return string.IsNullOrWhiteSpace(message) ? "pdf_read_failure" : TruncateDiagnostic(NormalizeWhitespace(message));

        var normalized = NormalizeWhitespace(message);
        if (string.IsNullOrWhiteSpace(normalized))
            return invalidPackage ? "invalid_openxml_package" : "docx_read_failure";

        var lowerMessage = normalized.ToLowerInvariant();
        if (lowerMessage.Contains("encrypted", StringComparison.Ordinal) || lowerMessage.Contains("password", StringComparison.Ordinal) || lowerMessage.Contains("protected", StringComparison.Ordinal))
            return "docx_protected_or_encrypted";
        if (invalidPackage || lowerMessage.Contains("package", StringComparison.Ordinal) || lowerMessage.Contains("zip", StringComparison.Ordinal))
            return "invalid_openxml_package";
        if (lowerMessage.Contains("strict", StringComparison.Ordinal))
            return "strict_or_malformed_docx";

        return TruncateDiagnostic(normalized);
    }

    private static string TruncateDiagnostic(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= 220 ? value : value[..220];
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : WhitespaceRegex.Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }
}

public class ResumeProfileService : IResumeProfileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IDownloadService _downloadService;
    private readonly IResumeTextExtractionService _resumeTextExtractionService;
    private readonly IAIInterviewClient _aiInterviewClient;
    private readonly IApplicationService _applicationService;
    private readonly IProductService _productService;
    private readonly NopLogger _nopLogger;

    public ResumeProfileService(
        IDownloadService downloadService,
        IResumeTextExtractionService resumeTextExtractionService,
        IAIInterviewClient aiInterviewClient,
        IApplicationService applicationService,
        IProductService productService,
        NopLogger nopLogger = null)
    {
        _downloadService = downloadService;
        _resumeTextExtractionService = resumeTextExtractionService;
        _aiInterviewClient = aiInterviewClient;
        _applicationService = applicationService;
        _productService = productService;
        _nopLogger = nopLogger;
    }

    public async Task<ResumeProfileGenerationResult> EnsureResumeProfileAsync(JobApplication application, Product product = null, bool forceRegenerate = false)
    {
        if (application == null || application.ResumeDownloadId <= 0)
        {
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = "missing_resume",
                ErrorMessage = "Resume file is not available for profiling."
            };
        }

        var storedProfile = forceRegenerate ? null : ParseProfile(application.ResumeProfileJson);
        if (!forceRegenerate && storedProfile != null && storedProfile.Success)
        {
            return new ResumeProfileGenerationResult
            {
                Success = true,
                ProfileJson = application.ResumeProfileJson,
                Profile = storedProfile
            };
        }

        product ??= application.ProductId > 0
            ? await _productService.GetProductByIdAsync(application.ProductId)
            : null;

        var download = await _downloadService.GetDownloadByIdAsync(application.ResumeDownloadId);
        var extraction = await _resumeTextExtractionService.ExtractTextAsync(download);
        if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
        {
            await PersistProfileFailureAsync(application, extraction.ErrorCode, extraction.ErrorMessage);
            await TryLogResumeProfileFailureAsync(application, product, download, extraction.ErrorCode, extraction.ErrorMessage, extraction.ExceptionType, extraction.DiagnosticMessage);
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = extraction.ErrorCode,
                ErrorMessage = extraction.ErrorMessage
            };
        }

        var response = await _aiInterviewClient.AnalyzeResumeAsync(new AIResumeProfileRequest
        {
            JobTitle = ResolveJobTitle(product, application),
            JobContext = BuildJobContext(product),
            ResumeText = extraction.Text
        });

        if (response == null || !response.Success)
        {
            var errorMessage = response?.ErrorMessage ?? "Resume profiling is unavailable.";
            await PersistProfileFailureAsync(application, "profile_generation_failed", errorMessage);
            await TryLogResumeProfileFailureAsync(application, product, download, "profile_generation_failed", errorMessage);
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = "profile_generation_failed",
                ErrorMessage = errorMessage
            };
        }

        var sanitized = SanitizeProfile(response);
        var profileJson = JsonSerializer.Serialize(sanitized, SerializerOptions);

        application.ResumeProfileJson = profileJson;
        application.ResumeProfileGeneratedOnUtc = DateTime.UtcNow;
        application.ResumeProfileError = null;
        await _applicationService.UpdateJobApplicationAsync(application);

        return new ResumeProfileGenerationResult
        {
            Success = true,
            ProfileJson = profileJson,
            Profile = sanitized
        };
    }

    public AIResumeProfileResponse ParseProfile(string resumeProfileJson)
    {
        if (string.IsNullOrWhiteSpace(resumeProfileJson))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<AIResumeProfileResponse>(resumeProfileJson, SerializerOptions);
            return parsed == null ? null : SanitizeProfile(parsed);
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistProfileFailureAsync(JobApplication application, string errorCode, string errorMessage)
    {
        application.ResumeProfileJson = null;
        application.ResumeProfileGeneratedOnUtc = null;
        application.ResumeProfileError = Truncate($"{errorCode}: {errorMessage}", 1000);
        await _applicationService.UpdateJobApplicationAsync(application);
    }

    private async Task TryLogResumeProfileFailureAsync(JobApplication application, Product product, Download download, string errorCode, string errorMessage, string exceptionType = null, string diagnosticMessage = null)
    {
        if (_nopLogger == null)
            return;

        try
        {
            var metadata = new List<string>
            {
                $"ErrorCode={Truncate(errorCode, 80)}",
                $"ApplicationId={application?.Id ?? 0}",
                $"CustomerId={application?.CustomerId ?? 0}",
                $"ProductId={application?.ProductId ?? product?.Id ?? 0}",
                $"ResumeDownloadId={application?.ResumeDownloadId ?? download?.Id ?? 0}",
                $"FileExtension={Truncate(ResolveExtension(download), 20)}",
                $"ContentType={Truncate(download?.ContentType, 120)}",
                $"FileSizeBytes={download?.DownloadBinary?.LongLength ?? 0}"
            };

            if (!string.IsNullOrWhiteSpace(exceptionType))
                metadata.Add($"ExceptionType={Truncate(exceptionType, 80)}");
            if (!string.IsNullOrWhiteSpace(diagnosticMessage))
                metadata.Add($"Diagnostic={Truncate(diagnosticMessage, 220)}");
            if (!string.IsNullOrWhiteSpace(errorMessage))
                metadata.Add($"Message={Truncate(errorMessage, 220)}");

            var level = string.Equals(errorCode, "extraction_failed", StringComparison.OrdinalIgnoreCase)
                ? LogLevel.Error
                : LogLevel.Warning;

            await _nopLogger.InsertLogAsync(
                level,
                "AI Interview resume extraction failed",
                string.Join("; ", metadata) + ".",
                null);
        }
        catch
        {
        }
    }

    private static string ResolveJobTitle(Product product, JobApplication application)
    {
        if (!string.IsNullOrWhiteSpace(product?.Name))
            return product.Name;

        if (!string.IsNullOrWhiteSpace(application?.JobTitle))
            return application.JobTitle;

        return "Interview";
    }

    private static string BuildJobContext(Product product)
    {
        if (product == null)
            return string.Empty;

        var parts = new[]
        {
            StripMarkup(product.Name),
            StripMarkup(product.ShortDescription),
            StripMarkup(product.FullDescription)
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        return Truncate(string.Join(Environment.NewLine, parts), 4000);
    }

    private static string StripMarkup(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = Regex.Replace(value, "<.*?>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private static AIResumeProfileResponse SanitizeProfile(AIResumeProfileResponse response)
    {
        response ??= new AIResumeProfileResponse();

        return new AIResumeProfileResponse
        {
            Success = response.Success,
            Skills = SanitizeList(response.Skills, 20, 80),
            PrimarySkills = SanitizeList(response.PrimarySkills, 10, 80),
            Tools = SanitizeList(response.Tools, 15, 80),
            Projects = (response.Projects ?? Array.Empty<AIResumeProjectProfile>())
                .Where(project => project != null)
                .Take(10)
                .Select(project => new AIResumeProjectProfile
                {
                    Name = Truncate(project.Name, 120),
                    Domain = Truncate(project.Domain, 120),
                    Technologies = SanitizeList(project.Technologies, 10, 80),
                    Responsibilities = SanitizeList(project.Responsibilities, 8, 160),
                    Impact = Truncate(project.Impact, 200)
                })
                .ToList(),
            ExperienceSummary = Truncate(response.ExperienceSummary, 280),
            SenioritySignals = SanitizeList(response.SenioritySignals, 8, 120),
            MissingOrUnclearAreas = SanitizeList(response.MissingOrUnclearAreas, 8, 160),
            ErrorMessage = Truncate(response.ErrorMessage, 200),
            RawJson = string.Empty
        };
    }

    private static IList<string> SanitizeList(IEnumerable<string> values, int maxItems, int maxLength)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => Truncate(value, maxLength))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string ResolveExtension(Download download)
    {
        var extension = Path.GetExtension(download?.Filename ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(extension))
            return extension;

        return string.IsNullOrWhiteSpace(download?.Extension) ? string.Empty : download.Extension;
    }
}
