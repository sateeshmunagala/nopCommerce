using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.Extensions.Logging;
using NopLogLevel = Nop.Core.Domain.Logging.LogLevel;

namespace Nop.Plugin.Misc.AIInterview.Services;

public partial class InterviewAiClient
{
    private static readonly JsonSerializerOptions ResumePlanSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string[] KnownSkillKeywords =
    {
        "c#", ".net", "asp.net", "java", "spring", "python", "javascript", "typescript", "react", "angular",
        "node", "sql", "azure", "aws", "docker", "kubernetes", "microservices", "rest", "api", "selenium",
        "testing", "automation", "git", "linux", "html", "css"
    };

    private static readonly HashSet<string> AllowedPlanCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "skill",
        "project_scenario",
        "job_fit",
        "behavioral"
    };

    public async Task<AIResumeProfileResponse> AnalyzeResumeAsync(AIResumeProfileRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockResumeProfile(request);

        var result = await CallAzureContentAsync(
            "resume-profile",
            "Return JSON only. Extract only facts supported by the resume text. Do not invent companies, projects, dates, skills, tools, metrics, or responsibilities. If project names are unclear, use a short descriptive label based on the resume text. If no projects are present, return an empty projects array. Keep each string concise.",
            BuildResumeProfilePrompt(request),
            1400);

        if (!result.Success)
            return new AIResumeProfileResponse { Success = false, ErrorMessage = result.ErrorMessage };

        var parsed = ParseResumeProfileResponse(result.Content);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 4000) };

        var contractReason = $"Mode=resume-profile; Reason=invalid JSON or failed contract parsing; Sample={TruncateSafe(result.Content, 800)}.";
        _logger?.LogWarning("Azure OpenAI resume profile call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview resume profile contract failure", contractReason);
        return new AIResumeProfileResponse { Success = false, ErrorMessage = "Resume profiling is unavailable." };
    }

    public async Task<AIInterviewQuestionPlanResponse> GenerateQuestionPlanAsync(AIInterviewQuestionPlanRequest request)
    {
        if (_mockSettings?.UseMockResponses != false)
            return BuildMockQuestionPlan(request);

        var result = await CallAzureContentAsync(
            "question-plan",
            "Return JSON only. Return exactly the requested number of questions. Ask one clear question per item. Do not include answers. Do not ask duplicate questions. Do not invent resume facts. Use resumeEvidence only for facts present in the resume profile. Project-scenario questions must be tied to a real project or responsibility from the resume profile when available. Skill questions must prioritize resume profile primary skills and job-required skills. Keep questions concise enough to read aloud in an interview runtime.",
            BuildQuestionPlanPrompt(request),
            2200);

        if (!result.Success)
        {
            return new AIInterviewQuestionPlanResponse
            {
                Success = false,
                ErrorMessage = result.ErrorMessage
            };
        }

        var parsed = ParseQuestionPlanResponse(result.Content, request.QuestionCount);
        if (parsed != null)
            return parsed with { RawJson = TruncateSafe(result.Content, 4000) };

        var contractReason = $"Mode=question-plan; Reason=invalid JSON or failed contract parsing; Sample={TruncateSafe(result.Content, 800)}.";
        _logger?.LogWarning("Azure OpenAI question plan call failed contract validation.");
        await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview question plan contract failure", contractReason);
        return new AIInterviewQuestionPlanResponse
        {
            Success = false,
            ErrorMessage = "Question plan generation is unavailable."
        };
    }

    private async Task<(bool Success, string Content, string ErrorMessage)> CallAzureContentAsync(string mode, string systemPrompt, string prompt, int maxTokens)
    {
        var endpointConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiEndpointUrl);
        var apiKeyConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiApiKey);
        var deploymentConfigured = !string.IsNullOrWhiteSpace(_settings?.AzureOpenAiDeploymentOrModel);
        if (!endpointConfigured || !apiKeyConfigured || !deploymentConfigured)
        {
            var detail = BuildConfigurationIncompleteLog(mode, endpointConfigured, apiKeyConfigured, deploymentConfigured);
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI unavailable", detail);
            return (false, string.Empty, $"AI service unavailable. {detail}");
        }

        try
        {
            var endpoint = _settings.AzureOpenAiEndpointUrl.TrimEnd('/');
            if (!endpoint.Contains("/openai/deployments/", StringComparison.OrdinalIgnoreCase))
                endpoint = $"{endpoint}/openai/deployments/{_settings.AzureOpenAiDeploymentOrModel.Trim()}/chat/completions?api-version=2024-06-01";
            else if (!endpoint.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
                endpoint += endpoint.Contains('?') ? "&api-version=2024-06-01" : "?api-version=2024-06-01";

            var payload = new
            {
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = prompt }
                },
                temperature = 0.2,
                max_tokens = maxTokens
            };

            using var httpClient = CreateHttpClient();
            httpClient.DefaultRequestHeaders.Add("api-key", _settings.AzureOpenAiApiKey.Trim());
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(endpoint, body);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var detail = BuildAzureHttpFailureLog(mode, endpoint, (int)response.StatusCode, response.ReasonPhrase, json);
                await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI HTTP failure", detail);
                return (false, string.Empty, $"AI service unavailable. {detail}");
            }

            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return (false, string.Empty, "AI service unavailable. Empty response choices.");

            if (!choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var contentProperty))
                return (false, string.Empty, "AI service unavailable. Missing response content.");

            var content = contentProperty.GetString();
            return string.IsNullOrWhiteSpace(content)
                ? (false, string.Empty, "AI service unavailable. Empty response content.")
                : (true, content, string.Empty);
        }
        catch (Exception ex)
        {
            var detail = $"Mode={mode}; Reason={ex.GetType().Name}; Message={TruncateSafe(ex.Message, 220)}.";
            await LogAiClientIssueAsync(NopLogLevel.Warning, "AI Interview Azure OpenAI exception", detail);
            return (false, string.Empty, $"AI service unavailable. {detail}");
        }
    }

    private static string BuildResumeProfilePrompt(AIResumeProfileRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Resume analysis mode: resume-profile");
        builder.AppendLine($"Job title: {request.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request.JobContext, 2000)}");
        builder.AppendLine("Resume text:");
        builder.AppendLine(TruncateSafe(request.ResumeText, 12000));
        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "skills": ["string"],
  "primarySkills": ["string"],
  "tools": ["string"],
  "projects": [
    {
      "name": "string",
      "domain": "string",
      "technologies": ["string"],
      "responsibilities": ["string"],
      "impact": "string"
    }
  ],
  "experienceSummary": "string",
  "senioritySignals": ["string"],
  "missingOrUnclearAreas": ["string"]
}
""");
        return builder.ToString();
    }

    private static string BuildQuestionPlanPrompt(AIInterviewQuestionPlanRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Interview mode: question-plan");
        builder.AppendLine($"Job title: {request.JobTitle}");
        builder.AppendLine($"Job context: {TruncateSafe(request.JobContext, 2500)}");
        builder.AppendLine($"Difficulty: {request.Difficulty}");
        builder.AppendLine($"Question count: {request.QuestionCount}");
        builder.AppendLine($"Global prompt: {request.Prompt}");
        builder.AppendLine("Resume profile JSON:");
        builder.AppendLine(TruncateSafe(request.ResumeProfileJson, 4000));
        builder.AppendLine("Allowed categories: skill, project_scenario, job_fit, behavioral");
        builder.AppendLine("Response contract:");
        builder.Append("""
{
  "questions": [
    {
      "sequenceNumber": 1,
      "category": "skill",
      "question": "string",
      "resumeEvidence": "string",
      "expectedSignals": ["string"],
      "rubric": {
        "technical": "string",
        "communication": "string",
        "professionalism": "string",
        "positiveAttitude": "string"
      }
    }
  ]
}
""");
        return builder.ToString();
    }

    private static AIResumeProfileResponse ParseResumeProfileResponse(string content)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;

            return new AIResumeProfileResponse
            {
                Success = true,
                Skills = ParseStringArray(root, "skills", 20, 80),
                PrimarySkills = ParseStringArray(root, "primarySkills", 10, 80),
                Tools = ParseStringArray(root, "tools", 15, 80),
                Projects = root.TryGetProperty("projects", out var projectsElement) && projectsElement.ValueKind == JsonValueKind.Array
                    ? projectsElement.EnumerateArray().Select(ParseProject).Where(project => project != null).ToList()
                    : new List<AIResumeProjectProfile>(),
                ExperienceSummary = TruncateSafe(TryGetString(root, "experienceSummary"), 280),
                SenioritySignals = ParseStringArray(root, "senioritySignals", 8, 120),
                MissingOrUnclearAreas = ParseStringArray(root, "missingOrUnclearAreas", 8, 160),
                RawJson = TruncateSafe(content, 4000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIResumeProjectProfile ParseProject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return new AIResumeProjectProfile
        {
            Name = TruncateSafe(TryGetString(element, "name"), 120),
            Domain = TruncateSafe(TryGetString(element, "domain"), 120),
            Technologies = ParseStringArray(element, "technologies", 10, 80),
            Responsibilities = ParseStringArray(element, "responsibilities", 8, 160),
            Impact = TruncateSafe(TryGetString(element, "impact"), 200)
        };
    }

    private static AIInterviewQuestionPlanResponse ParseQuestionPlanResponse(string content, int expectedCount)
    {
        try
        {
            var normalized = ExtractJsonObjectPayload(content);
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            if (!root.TryGetProperty("questions", out var questionsElement) || questionsElement.ValueKind != JsonValueKind.Array)
                return null;

            var questions = questionsElement.EnumerateArray()
                .Select((question, index) => ParseQuestionPlanItem(question, index + 1))
                .Where(question => question != null)
                .ToList();

            if (questions.Count != expectedCount)
                return new AIInterviewQuestionPlanResponse
                {
                    Success = false,
                    ErrorMessage = "Question plan did not return the configured number of questions.",
                    RawJson = TruncateSafe(content, 4000)
                };

            return new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = questions,
                RawJson = TruncateSafe(content, 4000)
            };
        }
        catch
        {
            return null;
        }
    }

    private static AIInterviewQuestionPlanItem ParseQuestionPlanItem(JsonElement element, int fallbackSequenceNumber)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var category = NormalizePlanCategory(TryGetString(element, "category"));
        return new AIInterviewQuestionPlanItem
        {
            SequenceNumber = element.TryGetProperty("sequenceNumber", out var sequenceElement) && sequenceElement.TryGetInt32(out var parsedSequence)
                ? parsedSequence
                : fallbackSequenceNumber,
            Category = category,
            Question = TruncateSafe(TryGetString(element, "question"), 240),
            ResumeEvidence = TruncateSafe(TryGetString(element, "resumeEvidence"), 200),
            ExpectedSignals = ParseStringArray(element, "expectedSignals", 6, 120),
            Rubric = ParseQuestionRubric(element)
        };
    }

    private static AIInterviewQuestionRubric ParseQuestionRubric(JsonElement element)
    {
        if (!element.TryGetProperty("rubric", out var rubricElement) || rubricElement.ValueKind != JsonValueKind.Object)
            return new AIInterviewQuestionRubric();

        return new AIInterviewQuestionRubric
        {
            Technical = TruncateSafe(TryGetString(rubricElement, "technical"), 160),
            Communication = TruncateSafe(TryGetString(rubricElement, "communication"), 160),
            Professionalism = TruncateSafe(TryGetString(rubricElement, "professionalism"), 160),
            PositiveAttitude = TruncateSafe(TryGetString(rubricElement, "positiveAttitude"), 160)
        };
    }

    private static IList<string> ParseStringArray(JsonElement element, string propertyName, int maxItems, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => TruncateSafe(item.GetString(), maxLength))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .ToList();
    }

    private static string TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : string.Empty;
    }

    private static string NormalizePlanCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "job_fit";

        var normalized = category.Trim().ToLowerInvariant();
        return AllowedPlanCategories.Contains(normalized) ? normalized : "job_fit";
    }

    private static AIResumeProfileResponse ParseResumeProfileForMock(string resumeProfileJson)
    {
        if (string.IsNullOrWhiteSpace(resumeProfileJson))
            return new AIResumeProfileResponse();

        try
        {
            return JsonSerializer.Deserialize<AIResumeProfileResponse>(resumeProfileJson, ResumePlanSerializerOptions) ?? new AIResumeProfileResponse();
        }
        catch
        {
            return new AIResumeProfileResponse();
        }
    }

    private AIResumeProfileResponse BuildMockResumeProfile(AIResumeProfileRequest request)
    {
        var resumeText = request?.ResumeText ?? string.Empty;
        var skills = ExtractSkills(resumeText);
        var projects = ExtractProjects(resumeText, skills);

        return new AIResumeProfileResponse
        {
            Success = true,
            Skills = skills,
            PrimarySkills = skills.Take(4).ToList(),
            Tools = skills.Where(skill => skill.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("aws", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("docker", StringComparison.OrdinalIgnoreCase) ||
                                          skill.Contains("kubernetes", StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList(),
            Projects = projects,
            ExperienceSummary = string.IsNullOrWhiteSpace(request?.JobTitle)
                ? "Resume-backed candidate profile."
                : $"Resume-backed profile for {request.JobTitle}.",
            SenioritySignals = new List<string> { "Hands-on delivery", "Project execution" },
            MissingOrUnclearAreas = new List<string>()
        };
    }

    private AIInterviewQuestionPlanResponse BuildMockQuestionPlan(AIInterviewQuestionPlanRequest request)
    {
        var profile = ParseResumeProfileForMock(request.ResumeProfileJson);
        var questionCount = Math.Clamp(request.QuestionCount <= 0 ? 3 : request.QuestionCount, 1, 10);
        var categories = BuildQuestionCategories(questionCount, profile.Projects.Any());
        var primarySkills = profile.PrimarySkills.Any() ? profile.PrimarySkills : profile.Skills.Any() ? profile.Skills : new List<string> { request.JobTitle, "problem solving" };
        var questions = new List<AIInterviewQuestionPlanItem>();

        for (var index = 0; index < questionCount; index++)
        {
            var category = categories[index];
            var skill = primarySkills[index % primarySkills.Count];
            var project = profile.Projects.Any() ? profile.Projects[index % profile.Projects.Count] : null;

            questions.Add(new AIInterviewQuestionPlanItem
            {
                SequenceNumber = index + 1,
                Category = category,
                Question = category switch
                {
                    "skill" => $"Your resume highlights {skill}. How would you apply that in a {request.Difficulty} {request.JobTitle} assignment?",
                    "project_scenario" when project != null => $"In your {project.Name} project, how would you handle a scenario where the core solution must scale while preserving reliability?",
                    "behavioral" => $"Describe a time you had to collaborate under pressure in work related to {request.JobTitle}. What did you do?",
                    _ => $"What part of this {request.JobTitle} role is the strongest match for your background, and where would you ramp up first?"
                },
                ResumeEvidence = category == "project_scenario" && project != null
                    ? string.IsNullOrWhiteSpace(project.Name) ? project.Domain : project.Name
                    : skill,
                ExpectedSignals = category switch
                {
                    "skill" => new List<string> { $"{skill} depth", "Tradeoff awareness", "Practical example" },
                    "project_scenario" => new List<string> { "Scenario reasoning", "Project-specific detail", "Outcome focus" },
                    "behavioral" => new List<string> { "Ownership", "Communication", "Reflection" },
                    _ => new List<string> { "Role alignment", "Learning plan", "Execution focus" }
                },
                Rubric = new AIInterviewQuestionRubric
                {
                    Technical = "Evaluate technical depth and decision quality.",
                    Communication = "Evaluate clarity and structure.",
                    Professionalism = "Evaluate ownership and judgment.",
                    PositiveAttitude = "Evaluate constructive and growth-oriented mindset."
                }
            });
        }

        return new AIInterviewQuestionPlanResponse
        {
            Success = true,
            Questions = questions,
            RawJson = JsonSerializer.Serialize(new { questions }, ResumePlanSerializerOptions)
        };
    }

    private static IList<string> BuildQuestionCategories(int questionCount, bool hasProjects)
    {
        int skillCount;
        int projectCount;

        if (questionCount == 1)
        {
            skillCount = 1;
            projectCount = 0;
        }
        else if (questionCount == 5)
        {
            skillCount = 2;
            projectCount = hasProjects ? 2 : 0;
        }
        else if (questionCount == 10)
        {
            skillCount = 4;
            projectCount = hasProjects ? 4 : 0;
        }
        else
        {
            skillCount = Math.Clamp((int)Math.Round(questionCount * 0.4m, MidpointRounding.AwayFromZero), questionCount >= 2 ? 1 : 0, questionCount);
            projectCount = hasProjects
                ? Math.Clamp((int)Math.Round(questionCount * 0.4m, MidpointRounding.AwayFromZero), questionCount >= 3 ? 1 : 0, Math.Max(0, questionCount - skillCount))
                : 0;
        }

        if (!hasProjects)
            projectCount = 0;

        while (skillCount + projectCount > questionCount && projectCount > 0)
            projectCount--;

        var remaining = Math.Max(0, questionCount - skillCount - projectCount);
        var categories = Enumerable.Repeat("skill", skillCount)
            .Concat(Enumerable.Repeat("project_scenario", projectCount))
            .ToList();

        for (var index = 0; index < remaining; index++)
            categories.Add(index % 2 == 0 ? "job_fit" : "behavioral");

        return categories.Take(questionCount).ToList();
    }

    private static IList<string> ExtractSkills(string resumeText)
    {
        var normalized = $" {resumeText?.ToLowerInvariant() ?? string.Empty} ";
        var found = KnownSkillKeywords
            .Where(keyword => normalized.Contains($" {keyword.ToLowerInvariant()} ", StringComparison.Ordinal) || normalized.Contains(keyword.ToLowerInvariant(), StringComparison.Ordinal))
            .Select(keyword => keyword.Equals(".net", StringComparison.OrdinalIgnoreCase) ? ".NET" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(keyword.Replace("c#", "C#")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return found.Any() ? found : new List<string> { "Problem solving", "System design" };
    }

    private static IList<AIResumeProjectProfile> ExtractProjects(string resumeText, IList<string> skills)
    {
        var lines = Regex.Split(resumeText ?? string.Empty, @"[\r\n\.]+")
            .Select(line => line.Trim())
            .Where(line => line.Length >= 12)
            .Where(line => line.Contains("project", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("application", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("platform", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("system", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();

        return lines.Select((line, index) => new AIResumeProjectProfile
        {
            Name = TruncateSafe(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5).Aggregate((left, right) => $"{left} {right}"), 120),
            Domain = "Resume project",
            Technologies = skills.Take(3).ToList(),
            Responsibilities = new List<string> { TruncateSafe(line, 160) },
            Impact = "Delivered project responsibilities from resume evidence."
        }).ToList();
    }
}
