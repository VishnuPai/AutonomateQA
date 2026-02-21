using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http.Json;
using UiTestRunner.Constants;

namespace UiTestRunner.AiProviders
{
    public class GeminiProvider : IAiModelProvider
    {
        private readonly IConfiguration _config;
        private readonly ILogger<GeminiProvider> _logger;

        public GeminiProvider(IConfiguration config, ILogger<GeminiProvider> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<UiActionResponse> GetActionAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apiKey = _config["Gemini:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                 // Use Google AI Studio REST API
                 return await GetActionViaRestAsync(apiKey, gherkinStep, ariaSnapshot, cancellationToken);
            }

            var projectId = _config["VertexAI:ProjectId"];
            var location = _config["VertexAI:Location"];
            var modelId = _config["VertexAI:ModelId"] ?? "gemini-1.5-flash-001";
            var publisher = "google"; 

            if (string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(location))
             {
                  _logger.LogError("VertexAI ProjectId or Location is missing in configuration, and no Gemini:ApiKey found.");
                  throw new InvalidOperationException("AI configuration is missing. Set Gemini:ApiKey or VertexAI settings.");
             }

            try 
            {
                var endpoint = $"{location}-aiplatform.googleapis.com";
                var client = await new PredictionServiceClientBuilder
                {
                    Endpoint = endpoint
                }.BuildAsync();

                var prompt = BuildActionPrompt(gherkinStep, ariaSnapshot);

                var generateContentRequest = new GenerateContentRequest
                {
                    Model = $"projects/{projectId}/locations/{location}/publishers/{publisher}/models/{modelId}",
                    SystemInstruction = new Content { Role = "system", Parts = { new Part { Text = prompt.SystemInstruction } } },
                    Contents = { new Content { Role = "user", Parts = { new Part { Text = prompt.UserPrompt } } } },
                    GenerationConfig = new GenerationConfig { Temperature = 0.1f, ResponseMimeType = "application/json" }
                };

                var response = await client.GenerateContentAsync(generateContentRequest);
                var responseText = response.Candidates[0].Content.Parts[0].Text;
                return ParseActionResponse(responseText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini Provider via AIPlatform V1");
                throw;
            }
        }

        public async Task<UiVerifyResponse> VerifyAsync(string gherkinStep, string ariaSnapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apiKey = _config["Gemini:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                 return await VerifyViaRestAsync(apiKey, gherkinStep, ariaSnapshot, cancellationToken);
            }
            
            // ... (Existing Vertex AI implementation preserved for backward compatibility/enterprise usage)
            var projectId = _config["VertexAI:ProjectId"];
            var location = _config["VertexAI:Location"];
            var modelId = _config["VertexAI:ModelId"] ?? "gemini-1.5-flash-001";
            var publisher = "google";

            try
            {
                 var endpoint = $"{location}-aiplatform.googleapis.com";
                 var client = await new PredictionServiceClientBuilder { Endpoint = endpoint }.BuildAsync();

                 var prompt = BuildVerifyPrompt(gherkinStep, ariaSnapshot);
                 
                 var generateContentRequest = new GenerateContentRequest
                {
                    Model = $"projects/{projectId}/locations/{location}/publishers/{publisher}/models/{modelId}",
                    SystemInstruction = new Content { Role = "system", Parts = { new Part { Text = prompt.SystemInstruction } } },
                    Contents = { new Content { Role = "user", Parts = { new Part { Text = prompt.UserPrompt } } } },
                    GenerationConfig = new GenerationConfig { Temperature = 0.0f, ResponseMimeType = "application/json" }
                };

                 var response = await client.GenerateContentAsync(generateContentRequest);
                 var responseText = response.Candidates[0].Content.Parts[0].Text?.Trim();
                 
                 var cleanJson = ExtractJson(responseText ?? "{}");
                 return JsonSerializer.Deserialize<UiVerifyResponse>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                        ?? new UiVerifyResponse { Passed = false, Reasoning = "Failed to parse JSON" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini Provider for Verification");
                return new UiVerifyResponse { Passed = false, Reasoning = "Error calling Vertex AI: " + ex.Message }; 
            }
        }

        public async Task<string> GenerateGherkinStepAsync(string actionType, string selector, string? value, string snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apiKey = _config["Gemini:ApiKey"];
            if (!string.IsNullOrEmpty(apiKey))
            {
                 return await GenerateGherkinViaRestAsync(apiKey, actionType, selector, value, snapshot, cancellationToken);
            }

            var projectId = _config["VertexAI:ProjectId"];
            var location = _config["VertexAI:Location"];
            var modelId = _config["VertexAI:ModelId"] ?? "gemini-1.5-flash-001";
            var publisher = "google";

            try
            {
                var endpoint = $"{location}-aiplatform.googleapis.com";
                var client = await new PredictionServiceClientBuilder { Endpoint = endpoint }.BuildAsync();

                var prompt = BuildGherkinPrompt(actionType, selector, value, snapshot);
                
                var generateContentRequest = new GenerateContentRequest
                {
                    Model = $"projects/{projectId}/locations/{location}/publishers/{publisher}/models/{modelId}",
                    SystemInstruction = new Content { Role = "system", Parts = { new Part { Text = prompt.SystemInstruction } } },
                    Contents = { new Content { Role = "user", Parts = { new Part { Text = prompt.UserPrompt } } } },
                    GenerationConfig = new GenerationConfig { Temperature = 0.2f, ResponseMimeType = "text/plain" }
                };

                var response = await client.GenerateContentAsync(generateContentRequest);
                var responseText = response.Candidates[0].Content.Parts[0].Text?.Trim() ?? "";
                
                if (responseText.StartsWith("Gherkin Step:", StringComparison.OrdinalIgnoreCase))
                {
                    responseText = responseText.Substring("Gherkin Step:".Length).Trim();
                }

                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating Gherkin step");
                var safeError = ex.Message.Replace("\r", " ").Replace("\n", " ");
                return $"# AI Error: {safeError}\n    And I {actionType} on '{selector}'" + (string.IsNullOrEmpty(value) ? "" : $" with value '{value}'");
            }
        }

        // --- REST Implementation (Google AI Studio) ---
        // Using HttpClient because we don't want to add another dependency just for this fallback
        
        private async Task<UiActionResponse> GetActionViaRestAsync(string apiKey, string step, string snapshot, CancellationToken cancellationToken = default)
        {
            var prompt = BuildActionPrompt(step, snapshot);
            var actionModels = _config.GetSection("Gemini:ActionModels").Get<string[]>() ?? new[] { "gemini-2.0-flash", "gemini-2.5-flash", "gemini-1.5-flash" };
            var result = await CallGeminiRestApi(apiKey, prompt, true, actionModels, cancellationToken);
            return ParseActionResponse(result);
        }

        private async Task<UiVerifyResponse> VerifyViaRestAsync(string apiKey, string step, string snapshot, CancellationToken cancellationToken = default)
        {
            var prompt = BuildVerifyPrompt(step, snapshot);
            var verifyModels = _config.GetSection("Gemini:VerifyModels").Get<string[]>() ?? new[] { "gemini-1.5-pro", "gemini-2.5-flash" };
            var result = await CallGeminiRestApi(apiKey, prompt, true, verifyModels, cancellationToken);
            
            try 
            {
                var cleanJson = ExtractJson(result);
                return JsonSerializer.Deserialize<UiVerifyResponse>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                       ?? new UiVerifyResponse { Passed = false, Reasoning = "Failed to parse JSON" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Gemini verify JSON. Raw response: {Result}", result);
                return new UiVerifyResponse { Passed = false, Reasoning = "Failed to parse Gemini response: " + result };
            }
        }

        private async Task<string> GenerateGherkinViaRestAsync(string apiKey, string action, string selector, string? value, string snapshot, CancellationToken cancellationToken = default)
        {
            try 
            {
                var prompt = BuildGherkinPrompt(action, selector, value, snapshot);
                var gherkinModels = _config.GetSection("Gemini:GherkinModels").Get<string[]>() ?? new[] { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash" };
                var result = await CallGeminiRestApi(apiKey, prompt, false, gherkinModels, cancellationToken);
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

        private async Task<string> CallGeminiRestApi(string apiKey, (string SystemInstruction, string UserPrompt) prompt, bool jsonMode, string[]? overrideModels = null, CancellationToken cancellationToken = default)
        {
            using var client = new HttpClient();
            
            var models = overrideModels ?? _config.GetSection("Gemini:GherkinModels").Get<string[]>() ?? new[] { "gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash" };
            
            var errors = new List<string>();

            foreach (var model in models)
            {
                int maxRetries = int.TryParse(_config["AiRetryCount"], out var r) ? r : 3;
                int currentRetry = 0;
                int delayMs = 2000; // Initial backoff 2s

                while (currentRetry <= maxRetries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    try 
                    {
                        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                        var requestBody = new
                        {
                            system_instruction = new { parts = new[] { new { text = prompt.SystemInstruction } } },
                            contents = new[] { new { parts = new[] { new { text = prompt.UserPrompt } } } },
                            generationConfig = jsonMode ? new { responseMimeType = "application/json" } : null
                        };

                        var response = await client.PostAsJsonAsync(url, requestBody, cancellationToken);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                            return json.GetProperty("candidates")[0]
                                       .GetProperty("content")
                                       .GetProperty("parts")[0]
                                       .GetProperty("text")
                                       .GetString() ?? "";
                        }
                        else if ((int)response.StatusCode == 429) // Too Many Requests
                        {
                            if (currentRetry >= maxRetries)
                            {
                                var errorBody = await response.Content.ReadAsStringAsync();
                                errors.Add($"Model {model} 429 Rate Limit Exceeded after {maxRetries} retries: {errorBody}");
                                _logger.LogWarning($"Gemini {model} rate limit exhausted. Moving to next model.");
                                break; // Break retry loop, move to next model
                            }

                            // Calculate wait time
                            var waitTime = TimeSpan.FromMilliseconds(delayMs);
                            
                            // Respect Retry-After header if present
                            if (response.Headers.RetryAfter?.Delta.HasValue == true)
                            {
                                waitTime = response.Headers.RetryAfter.Delta.Value;
                                // Add a small buffer
                                waitTime = waitTime.Add(TimeSpan.FromSeconds(1));
                            }

                            _logger.LogWarning($"Gemini {model} hit 429 (Rate Limit). Waiting {waitTime.TotalSeconds:F1}s before retry {currentRetry + 1}/{maxRetries}...");
                            await Task.Delay(waitTime);

                            // Exponential backoff for next attempt (if header wasn't used)
                            delayMs *= 2;
                            currentRetry++;
                            continue;
                        }
                        else 
                        {
                            var errorBody = await response.Content.ReadAsStringAsync();
                            errors.Add($"Model {model} failed: {response.StatusCode} - {errorBody}");
                            _logger.LogWarning($"Gemini {model} failed with {response.StatusCode}. Error: {errorBody}");
                            break; // Non-transient error, move to next model
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Model {model} exception: {ex.Message}");
                        break; // Exception, move to next model
                    }
                }
            }
            
            // If all failed, try to list available models to help debug
            string availableModels = "Could not list models";
            try 
            {
                var listUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
                var listResponse = await client.GetStringAsync(listUrl);
                availableModels = listResponse; // Dumping raw JSON for inspection
            }
            catch (Exception ex)
            {
                availableModels = ex.Message;
            }

            // Clean up newlines for the Gherkin comment
            var safeModels = availableModels.Replace("\r", " ").Replace("\n", " ");
            throw new Exception($"All Gemini models failed. Errors: {string.Join("; ", errors)}. AVAILABLE MODELS: {safeModels}");
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
        // Using shared PromptBuilder to avoid code duplication
        private (string SystemInstruction, string UserPrompt) BuildActionPrompt(string step, string snapshot) 
            => PromptBuilder.BuildActionPrompt(step, snapshot);

        private (string SystemInstruction, string UserPrompt) BuildVerifyPrompt(string step, string snapshot) 
            => PromptBuilder.BuildVerifyPrompt(step, snapshot);

        private (string SystemInstruction, string UserPrompt) BuildGherkinPrompt(string action, string selector, string? value, string snapshot) 
            => PromptBuilder.BuildGherkinPrompt(action, selector, value, snapshot);
    }
}
