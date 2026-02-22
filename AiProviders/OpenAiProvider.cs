using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UiTestRunner.Constants;

namespace UiTestRunner.AiProviders
{
    public class OpenAiProvider : IAiModelProvider
    {
        private const int DefaultMaxTokensFallback = 250;

        private readonly IConfiguration _config;
        private readonly ILogger<OpenAiProvider> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UiTestRunner.Services.ITestRunTokenTracker? _tokenTracker;

        public OpenAiProvider(IConfiguration config, ILogger<OpenAiProvider> logger, IHttpClientFactory httpClientFactory, UiTestRunner.Services.ITestRunTokenTracker? tokenTracker = null)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _tokenTracker = tokenTracker;
        }

        public async Task<UiActionResponse> GetActionAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt = BuildActionPrompt(gherkinStep, ariaSnapshot);
            var actionModels = GetRequiredModelList("OpenAI:ActionModels");
            var result = await CallOpenAiApiAsync(prompt, true, actionModels, cancellationToken);
            return ParseActionResponse(result);
        }

        public async Task<UiVerifyResponse> VerifyAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt = BuildVerifyPrompt(gherkinStep, ariaSnapshot);
            var verifyModels = GetRequiredModelList("OpenAI:VerifyModels");
            var result = await CallOpenAiApiAsync(prompt, true, verifyModels, cancellationToken);

            try
            {
                var cleanJson = ExtractJson(result);
                return JsonSerializer.Deserialize<UiVerifyResponse>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new UiVerifyResponse { Passed = false, Reasoning = "Failed to parse JSON" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse OpenAi verify JSON. Raw response: {Result}", result);
                return new UiVerifyResponse { Passed = false, Reasoning = "Failed to parse OpenAi response: " + result };
            }
        }

        public async Task<string> GenerateGherkinStepAsync(string action, string selector, string? value, string ariaSnapshot, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prompt = BuildGherkinPrompt(action, selector, value, ariaSnapshot);
                var gherkinModels = GetRequiredModelList("OpenAI:GherkinModels");
                var result = await CallOpenAiApiAsync(prompt, false, gherkinModels, cancellationToken);

                if (result.StartsWith("Gherkin Step:", StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring("Gherkin Step:".Length).Trim();
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var safeError = ex.Message.Replace("\r", " ").Replace("\n", " ");
                return $"# AI Error: {safeError}\n    And I {action} on '{selector}'";
            }
        }

        /// <summary>Reads model list from config; throws if missing or empty so no hardcoded model names are used.</summary>
        private string[] GetRequiredModelList(string configKey)
        {
            var models = _config.GetSection(configKey).Get<string[]>();
            if (models == null || models.Length == 0)
                throw new InvalidOperationException($"Configure at least one model in appsettings under \"{configKey}\" (e.g. [ \"your-model-name\" ]).");
            return models;
        }

        private async Task<string> CallOpenAiApiAsync((string SystemInstruction, string UserPrompt) prompt, bool jsonMode, string[] models, CancellationToken cancellationToken = default)
        {
            var apiKey = _config["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("OpenAI API Key is missing. Set OpenAI:ApiKey configuration.");
            }

            var maxTokens = int.TryParse(_config["OpenAI:MaxTokens"], out var mt) && mt > 0 ? mt : DefaultMaxTokensFallback;
            var apiBaseUrl = _config["OpenAI:ApiBaseUrl"]?.Trim();
            var apiEndpoint = _config["OpenAI:ApiEndpoint"]?.Trim();
            var apiVersion = _config["OpenAI:ApiVersion"]?.Trim();
            var useAzureEndpoint = !string.IsNullOrEmpty(apiBaseUrl);
            if (useAzureEndpoint && string.IsNullOrEmpty(apiVersion))
                apiVersion = "2024-06-01"; // Azure API version; override in config if needed

            if (!useAzureEndpoint && string.IsNullOrEmpty(apiEndpoint))
                throw new InvalidOperationException("Set OpenAI:ApiEndpoint in appsettings (e.g. the chat completions URL) when not using Azure (OpenAI:ApiBaseUrl).");

            var client = _httpClientFactory.CreateClient();

            var errors = new List<string>();

            foreach (var model in models)
            {
                int maxRetries = int.TryParse(_config["AiRetryCount"], out var r) ? r : 3;
                int currentRetry = 0;
                int delayMs = 2000;

                while (currentRetry <= maxRetries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var requestUrl = useAzureEndpoint
                            ? $"{apiBaseUrl!.TrimEnd('/')}/{model}/chat/completions?api-version={apiVersion}"
                            : apiEndpoint!;

                        var requestBody = new
                        {
                            model = model,
                            messages = new[]
                            {
                                new { role = "system", content = prompt.SystemInstruction },
                                new { role = "user", content = prompt.UserPrompt }
                            },
                            response_format = jsonMode ? new { type = "json_object" } : null,
                            temperature = 0,
                            max_tokens = maxTokens
                        };

                        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                        if (useAzureEndpoint)
                            request.Headers.Add("api-key", apiKey);
                        else
                            request.Headers.Add("Authorization", $"Bearer {apiKey}");
                        request.Content = JsonContent.Create(requestBody);
                        var response = await client.SendAsync(request, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                            var content = json.GetProperty("choices")[0]
                                .GetProperty("message")
                                .GetProperty("content")
                                .GetString() ?? "";
                            if (_tokenTracker != null && json.TryGetProperty("usage", out var usage))
                            {
                                var p = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                                var c = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                                if (p > 0 || c > 0) _tokenTracker.AddUsage(p, c);
                            }
                            return content;
                        }
                        else if ((int)response.StatusCode == 429) // Too Many Requests
                        {
                            if (currentRetry >= maxRetries)
                            {
                                var errorBody = await response.Content.ReadAsStringAsync();
                                errors.Add($"Model {model} 429 Rate Limit Exceeded after {maxRetries} retries: {errorBody}");
                                _logger.LogWarning("OpenAI {Model} rate limit exhausted. Moving to next model.", model);
                                break;
                            }

                            var waitTime = TimeSpan.FromMilliseconds(delayMs);
                            if (response.Headers.RetryAfter?.Delta.HasValue == true)
                            {
                                waitTime = response.Headers.RetryAfter.Delta!.Value.Add(TimeSpan.FromSeconds(1));
                            }

                            _logger.LogWarning("OpenAI {Model} hit 429 (Rate Limit). Waiting {Wait}s before retry {Current}/{Max}...", model, waitTime.TotalSeconds, currentRetry + 1, maxRetries);
                            await Task.Delay(waitTime);

                            delayMs *= 2;
                            currentRetry++;
                            continue;
                        }
                        else
                        {
                            var errorBody = await response.Content.ReadAsStringAsync();
                            errors.Add($"Model {model} failed: {response.StatusCode} - {errorBody}");
                            _logger.LogWarning("OpenAI {Model} failed with {StatusCode}. Error: {ErrorBody}", model, response.StatusCode, errorBody);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Model {model} exception: {ex.Message}");
                        break;
                    }
                }
            }

            throw new Exception($"All OpenAI models failed. Errors: {string.Join("; ", errors)}");
        }

        private string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var startIndex = text.IndexOf('{');
            var endIndex = text.LastIndexOf('}');
            if (startIndex >= 0 && endIndex >= startIndex)
            {
                return text.Substring(startIndex, endIndex - startIndex + 1);
            }
            return text;
        }

        private UiActionResponse ParseActionResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("AI response text is null or empty.");
            }

            text = text.Replace("```json", "").Replace("```", "").Trim();

            var response = JsonSerializer.Deserialize<UiActionResponse>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                throw new InvalidOperationException($"Failed to parse AI response as UiActionResponse. Response text: {text.Substring(0, Math.Min(200, text.Length))}");
            }

            return response;
        }

        // --- Prompts ---
        private (string SystemInstruction, string UserPrompt) BuildActionPrompt(string step, string snapshot)
            => PromptBuilder.BuildActionPrompt(step, snapshot);

        private (string SystemInstruction, string UserPrompt) BuildVerifyPrompt(string step, string snapshot)
            => PromptBuilder.BuildVerifyPrompt(step, snapshot);

        private (string SystemInstruction, string UserPrompt) BuildGherkinPrompt(string action, string selector, string? value, string snapshot)
            => PromptBuilder.BuildGherkinPrompt(action, selector, value, snapshot);
    }
}
