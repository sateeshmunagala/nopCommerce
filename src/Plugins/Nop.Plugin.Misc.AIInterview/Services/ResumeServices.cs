using System.Net;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Catalog;
using Nop.Core.Domain.Logging;
using Nop.Services.Media;
using NopLogger = Nop.Services.Logging.ILogger;

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
    private static readonly Regex InlineWhitespaceRegex = new(@"[^\S\r\n]+", RegexOptions.Compiled);
    private static readonly Regex LineBreakRegex = new(@"\r\n|\r|\n", RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLinesRegex = new(@"(?:\n\s*){3,}", RegexOptions.Compiled);
    private static readonly Regex SecretQueryParameterRegex = new(@"(?i)([?&](?:sig|se|sp|sv|sr|skoid|sktid|skt|ske|sks|skv|api[-_]?key|subscription[-_]?key|code|token|key)=)[^&\s]+", RegexOptions.Compiled);
    private const int MaxExtractedTextLength = 12000;

    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly IAzureDocumentIntelligenceResumeReader _azureDocumentIntelligenceResumeReader;
    private readonly NopLogger _nopLogger;

    public ResumeTextExtractionService(
        AIInterviewSettings aiInterviewSettings = null,
        IAzureDocumentIntelligenceResumeReader azureDocumentIntelligenceResumeReader = null,
        NopLogger nopLogger = null)
    {
        _aiInterviewSettings = aiInterviewSettings ?? new AIInterviewSettings();
        _azureDocumentIntelligenceResumeReader = azureDocumentIntelligenceResumeReader ?? new AzureDocumentIntelligenceResumeReader();
        _nopLogger = nopLogger;
    }

    public async Task<ResumeTextExtractionResult> ExtractTextAsync(Download download)
    {
        if (download == null)
        {
            return new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "missing_download",
                ErrorMessage = "Resume file could not be loaded."
            };
        }

        var extension = Path.GetExtension(download.Filename ?? download.Extension ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension))
            extension = download.Extension;

        if (download.DownloadBinary == null || download.DownloadBinary.Length == 0)
        {
            return new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "missing_binary",
                ErrorMessage = "Resume file is empty."
            };
        }

        var contentType = GetSupportedContentType(extension);
        if (contentType == null)
        {
            return new ResumeTextExtractionResult
            {
                Success = false,
                ErrorCode = "unsupported_extension",
                ErrorMessage = "Only PDF and DOCX resumes are supported."
            };
        }

        var modelId = NormalizeAzureDocumentIntelligenceModelId(_aiInterviewSettings.AzureDocumentIntelligenceModelId);
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_aiInterviewSettings.AzureDocumentIntelligenceEndpointUrl) ||
            string.IsNullOrWhiteSpace(_aiInterviewSettings.AzureDocumentIntelligenceApiKey))
        {
            var failure = BuildExtractionFailure(
                "AzureDocumentIntelligenceConfigurationException",
                "azure_document_intelligence_not_configured");
            await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
            return failure;
        }

        if (!Uri.TryCreate(_aiInterviewSettings.AzureDocumentIntelligenceEndpointUrl.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("https" or "http"))
        {
            var failure = BuildExtractionFailure(
                "AzureDocumentIntelligenceConfigurationException",
                "invalid_azure_document_intelligence_endpoint");
            await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
            return failure;
        }

        try
        {
            var timeoutSeconds = NormalizeAzureDocumentIntelligenceTimeoutSeconds(_aiInterviewSettings.AzureDocumentIntelligenceTimeoutSeconds);
            var extractedText = await _azureDocumentIntelligenceResumeReader.ReadTextAsync(
                download.DownloadBinary,
                extension,
                contentType,
                endpoint,
                _aiInterviewSettings.AzureDocumentIntelligenceApiKey.Trim(),
                modelId,
                timeoutSeconds);

            var normalized = NormalizeExtractedResumeText(extractedText);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                var failure = new ResumeTextExtractionResult
                {
                    Success = false,
                    ErrorCode = "empty_text",
                    ErrorMessage = "Resume text could not be extracted."
                };
                await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
                return failure;
            }

            return new ResumeTextExtractionResult
            {
                Success = true,
                Text = normalized.Length <= MaxExtractedTextLength ? normalized : normalized[..MaxExtractedTextLength]
            };
        }
        catch (RequestFailedException ex)
        {
            var failure = BuildExtractionFailure(ex.GetType().Name, BuildAzureRequestFailedDiagnosticMessage(ex));
            await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
            return failure;
        }
        catch (OperationCanceledException ex)
        {
            var failure = BuildExtractionFailure(ex.GetType().Name, "azure_document_intelligence_timeout");
            await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
            return failure;
        }
        catch (Exception ex)
        {
            var failure = BuildExtractionFailure(ex.GetType().Name, BuildExtractionDiagnosticMessage(ex.Message));
            await TryLogAzureResumeExtractionFailureAsync(download, extension, modelId, stopwatch.ElapsedMilliseconds, failure);
            return failure;
        }
    }

    private async Task TryLogAzureResumeExtractionFailureAsync(Download download, string extension, string modelId, long durationMs, ResumeTextExtractionResult failure)
    {
        if (_nopLogger == null || failure == null)
            return;

        try
        {
            var metadata = new List<string>
            {
                $"ErrorCode={TruncateDiagnostic(failure.ErrorCode)}",
                $"ResumeDownloadId={download?.Id ?? 0}",
                $"FileExtension={TruncateDiagnostic(extension)}",
                $"FileSizeBytes={download?.DownloadBinary?.LongLength ?? 0}",
                $"ModelId={TruncateDiagnostic(modelId)}",
                $"DurationMs={Math.Max(0, durationMs)}"
            };

            if (!string.IsNullOrWhiteSpace(failure.ExceptionType))
                metadata.Add($"ExceptionType={TruncateDiagnostic(failure.ExceptionType)}");
            if (!string.IsNullOrWhiteSpace(failure.DiagnosticMessage))
                metadata.Add($"Diagnostic={TruncateDiagnostic(SanitizeDiagnostic(failure.DiagnosticMessage))}");

            var level = string.Equals(failure.ErrorCode, "extraction_failed", StringComparison.OrdinalIgnoreCase)
                ? LogLevel.Error
                : LogLevel.Warning;

            await _nopLogger.InsertLogAsync(
                level,
                "AI Interview Azure resume extraction failed",
                string.Join("; ", metadata) + ".",
                null);
        }
        catch
        {
        }
    }

    private static ResumeTextExtractionResult BuildExtractionFailure(string exceptionType, string diagnosticMessage)
    {
        return new ResumeTextExtractionResult
        {
            Success = false,
            ErrorCode = "extraction_failed",
            ErrorMessage = "Resume text could not be extracted.",
            ExceptionType = exceptionType,
            DiagnosticMessage = diagnosticMessage
        };
    }

    private static string GetSupportedContentType(string extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => null
        };
    }

    private static string NormalizeAzureDocumentIntelligenceModelId(string modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? AIInterviewDefaults.DefaultAzureDocumentIntelligenceModelId
            : modelId.Trim();
    }

    private static int NormalizeAzureDocumentIntelligenceTimeoutSeconds(int timeoutSeconds)
    {
        return timeoutSeconds > 0
            ? timeoutSeconds
            : AIInterviewDefaults.DefaultAzureDocumentIntelligenceTimeoutSeconds;
    }

    private static string BuildAzureRequestFailedDiagnosticMessage(RequestFailedException exception)
    {
        var parts = new List<string>();
        if (exception.Status > 0)
            parts.Add($"azure_document_intelligence_status_{exception.Status}");
        if (!string.IsNullOrWhiteSpace(exception.ErrorCode))
            parts.Add(exception.ErrorCode);
        if (!string.IsNullOrWhiteSpace(exception.Message))
            parts.Add(exception.Message);

        return BuildExtractionDiagnosticMessage(string.Join(": ", parts));
    }

    private static string BuildExtractionDiagnosticMessage(string message)
    {
        var normalized = SanitizeDiagnostic(NormalizeDiagnosticWhitespace(message));
        return string.IsNullOrWhiteSpace(normalized)
            ? "azure_document_intelligence_read_failure"
            : TruncateDiagnostic(normalized);
    }

    private static string SanitizeDiagnostic(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : SecretQueryParameterRegex.Replace(value, "$1REDACTED");
    }

    private static string TruncateDiagnostic(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= 220 ? value : value[..220];
    }

    private static string NormalizeDiagnosticWhitespace(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : WhitespaceRegex.Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string NormalizeExtractedResumeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedLineBreaks = LineBreakRegex.Replace(WebUtility.HtmlDecode(text), "\n");
        var normalizedLines = normalizedLineBreaks
            .Split('\n')
            .Select(line => InlineWhitespaceRegex.Replace(line, " ").Trim());

        return ExcessBlankLinesRegex
            .Replace(string.Join("\n", normalizedLines), "\n\n")
            .Trim();
    }
}

public interface IAzureDocumentIntelligenceResumeReader
{
    Task<string> ReadTextAsync(
        byte[] binary,
        string extension,
        string contentType,
        Uri endpoint,
        string apiKey,
        string modelId,
        int timeoutSeconds);
}

public class AzureDocumentIntelligenceResumeReader : IAzureDocumentIntelligenceResumeReader
{
    public async Task<string> ReadTextAsync(
        byte[] binary,
        string extension,
        string contentType,
        Uri endpoint,
        string apiKey,
        string modelId,
        int timeoutSeconds)
    {
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var client = new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(apiKey));
        var options = new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(binary));
        // Azure.AI.DocumentIntelligence 1.0.0 does not expose a content-type property on AnalyzeDocumentOptions.
        // Keep the parameter in this boundary so a future SDK can apply PDF/DOCX content types without changing callers.
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            options,
            timeoutSource.Token);

        return ExtractPlainText(operation.Value);
    }

    private static string ExtractPlainText(AnalyzeResult result)
    {
        if (!string.IsNullOrWhiteSpace(result?.Content))
            return result.Content;

        var pageLines = result?.Pages?
            .SelectMany(page => page.Lines ?? Enumerable.Empty<DocumentLine>())
            .Select(line => line.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content));

        return pageLines == null ? string.Empty : string.Join(Environment.NewLine, pageLines);
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
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly IProductService _productService;
    private readonly IAzureUsageService _azureUsageService;
    private readonly NopLogger _nopLogger;

    public ResumeProfileService(
        IDownloadService downloadService,
        IResumeTextExtractionService resumeTextExtractionService,
        IAIInterviewClient aiInterviewClient,
        IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        IProductService productService,
        IAzureUsageService azureUsageService,
        NopLogger nopLogger = null)
    {
        _downloadService = downloadService;
        _resumeTextExtractionService = resumeTextExtractionService;
        _aiInterviewClient = aiInterviewClient;
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _productService = productService;
        _azureUsageService = azureUsageService;
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

        await TryLogResumeProfileExtractionSuccessAsync(application, product, download, extraction.Text);

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

    public async Task<ResumeProfileGenerationResult> EnsureResumeProfileAsync(InterviewSession session, Product product = null, bool forceRegenerate = false)
    {
        if (session == null || session.ResumeDownloadId <= 0)
        {
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = "missing_resume",
                ErrorMessage = "Resume file is not available for profiling."
            };
        }

        var storedProfile = forceRegenerate ? null : ParseProfile(session.ResumeProfileJson);
        if (!forceRegenerate && storedProfile != null && storedProfile.Success)
        {
            return new ResumeProfileGenerationResult
            {
                Success = true,
                ProfileJson = session.ResumeProfileJson,
                Profile = storedProfile
            };
        }

        product ??= session.SourceProductId > 0
            ? await _productService.GetProductByIdAsync(session.SourceProductId)
            : session.ProductId > 0
                ? await _productService.GetProductByIdAsync(session.ProductId)
                : null;

        var download = await _downloadService.GetDownloadByIdAsync(session.ResumeDownloadId);
        var extraction = await _resumeTextExtractionService.ExtractTextAsync(download);
        if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
        {
            await PersistProfileFailureAsync(session, extraction.ErrorCode, extraction.ErrorMessage);
            await TryLogResumeProfileFailureAsync(session, product, download, extraction.ErrorCode, extraction.ErrorMessage, extraction.ExceptionType, extraction.DiagnosticMessage);
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = extraction.ErrorCode,
                ErrorMessage = extraction.ErrorMessage
            };
        }

        await TryLogResumeProfileExtractionSuccessAsync(session, product, download, extraction.Text);

        var response = await _aiInterviewClient.AnalyzeResumeAsync(new AIResumeProfileRequest
        {
            JobTitle = ResolveSessionTitle(session, product),
            JobContext = BuildJobContext(product),
            ResumeText = extraction.Text
        });
        if (_azureUsageService != null)
        {
            await _azureUsageService.RecordOpenAiUsageAsync(new AzureOpenAiUsageRecordRequest
            {
                InterviewSessionId = session.Id,
                UsageKind = AzureUsageMetricDefaults.UsageKindOpenAiResumeAnalysis,
                OperationName = "AnalyzeResume",
                UsageInfo = response?.UsageInfo,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = "resumeProfile",
                    scope = "interviewSession"
                }, SerializerOptions)
            });
        }

        if (response == null || !response.Success)
        {
            var errorMessage = response?.ErrorMessage ?? "Resume profiling is unavailable.";
            await PersistProfileFailureAsync(session, "profile_generation_failed", errorMessage);
            await TryLogResumeProfileFailureAsync(session, product, download, "profile_generation_failed", errorMessage);
            return new ResumeProfileGenerationResult
            {
                Success = false,
                ErrorCode = "profile_generation_failed",
                ErrorMessage = errorMessage
            };
        }

        var sanitized = SanitizeProfile(response);
        var profileJson = JsonSerializer.Serialize(sanitized, SerializerOptions);

        session.ResumeProfileJson = profileJson;
        session.ResumeProfileGeneratedOnUtc = DateTime.UtcNow;
        session.ResumeProfileError = null;
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

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

    private async Task PersistProfileFailureAsync(InterviewSession session, string errorCode, string errorMessage)
    {
        session.ResumeProfileJson = null;
        session.ResumeProfileGeneratedOnUtc = null;
        session.ResumeProfileError = Truncate($"{errorCode}: {errorMessage}", 1000);
        await _interviewSessionService.UpdateInterviewSessionAsync(session);
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

    private async Task TryLogResumeProfileExtractionSuccessAsync(JobApplication application, Product product, Download download, string extractedText)
    {
        if (_nopLogger == null)
            return;

        try
        {
            var metadata = new List<string>
            {
                "Stage=extraction-completed",
                "SuccessMessage=Resume extraction completed successfully.",
                $"ApplicationId={application?.Id ?? 0}",
                $"CustomerId={application?.CustomerId ?? 0}",
                $"ProductId={application?.ProductId ?? product?.Id ?? 0}",
                $"ResumeDownloadId={application?.ResumeDownloadId ?? download?.Id ?? 0}",
                $"FileExtension={Truncate(ResolveExtension(download), 20)}",
                $"ContentType={Truncate(download?.ContentType, 120)}",
                $"FileSizeBytes={download?.DownloadBinary?.LongLength ?? 0}",
                $"ExtractedTextLength={(extractedText ?? string.Empty).Length}"
            };

            await _nopLogger.InsertLogAsync(
                LogLevel.Information,
                "AI Interview resume extraction completed",
                string.Join("; ", metadata) + ".",
                null);
        }
        catch
        {
        }
    }

    private async Task TryLogResumeProfileExtractionSuccessAsync(InterviewSession session, Product product, Download download, string extractedText)
    {
        if (_nopLogger == null)
            return;

        try
        {
            var metadata = new List<string>
            {
                "Stage=extraction-completed",
                "SuccessMessage=Resume extraction completed successfully.",
                $"SessionId={session?.Id ?? 0}",
                $"CustomerId={session?.CustomerId ?? 0}",
                $"ProductId={session?.ProductId ?? product?.Id ?? 0}",
                $"SourceProductId={session?.SourceProductId ?? 0}",
                $"ResumeDownloadId={session?.ResumeDownloadId ?? download?.Id ?? 0}",
                $"FileExtension={Truncate(ResolveExtension(download), 20)}",
                $"ContentType={Truncate(download?.ContentType, 120)}",
                $"FileSizeBytes={download?.DownloadBinary?.LongLength ?? 0}",
                $"ExtractedTextLength={(extractedText ?? string.Empty).Length}"
            };

            await _nopLogger.InsertLogAsync(
                LogLevel.Information,
                "AI Interview practice resume extraction completed",
                string.Join("; ", metadata) + ".",
                null);
        }
        catch
        {
        }
    }

    private async Task TryLogResumeProfileFailureAsync(InterviewSession session, Product product, Download download, string errorCode, string errorMessage, string exceptionType = null, string diagnosticMessage = null)
    {
        if (_nopLogger == null)
            return;

        try
        {
            var metadata = new List<string>
            {
                $"ErrorCode={Truncate(errorCode, 80)}",
                $"SessionId={session?.Id ?? 0}",
                $"CustomerId={session?.CustomerId ?? 0}",
                $"ProductId={session?.ProductId ?? product?.Id ?? 0}",
                $"SourceProductId={session?.SourceProductId ?? 0}",
                $"ResumeDownloadId={session?.ResumeDownloadId ?? download?.Id ?? 0}",
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
                "AI Interview practice resume extraction failed",
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

    private static string ResolveSessionTitle(InterviewSession session, Product product)
    {
        if (!string.IsNullOrWhiteSpace(product?.Name))
            return product.Name;

        return string.Equals(session?.InterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase)
            ? "AI Practice Interview"
            : "Interview";
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
