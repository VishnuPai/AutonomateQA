using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace UiTestRunner.Services
{
    public interface ITestDataManager
    {
        string GetValue(string key);
        string ReplacePlaceholders(string input);
        void AddSecret(string key, string value);
        string MaskLiterals(string input);
    }

    public class TestDataManager : ITestDataManager
    {
        private static Dictionary<string, string>? _staticSecretsCache;
        private static readonly object _cacheLock = new object();
        
        // Static compiled regex for placeholder replacement
        private static readonly Regex PlaceholderRegex = new Regex(@"\{\{(.*?)\}\}", RegexOptions.Compiled);
        
        // Instance-level dictionary for dynamic secrets (like specific UI test variables)
        private readonly Dictionary<string, string> _scopedSecrets = new Dictionary<string, string>();
        private readonly ILogger<TestDataManager> _logger;
        private readonly IConfiguration _configuration;

        public TestDataManager(IConfiguration configuration, IWebHostEnvironment env, ILogger<TestDataManager> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Load secrets from User Secrets (preferred) or fallback to test_secrets.json for backward compatibility
            if (_staticSecretsCache == null)
            {
                lock (_cacheLock)
                {
                    if (_staticSecretsCache == null)
                    {
                        _staticSecretsCache = new Dictionary<string, string>();
                        
                        // First, try to load from User Secrets (via IConfiguration)
                        // User Secrets are accessed via configuration with "TestSecrets:" prefix
                        var testSecretsSection = _configuration.GetSection("TestSecrets");
                        if (testSecretsSection.Exists())
                        {
                            foreach (var secret in testSecretsSection.GetChildren())
                            {
                                var value = secret.Value;
                                if (!string.IsNullOrEmpty(value))
                                {
                                    _staticSecretsCache[secret.Key] = value;
                                }
                            }
                            _logger.LogInformation($"Loaded {_staticSecretsCache.Count} secrets from User Secrets.");
                        }
                        
                        // Fallback to test_secrets.json for backward compatibility (only if User Secrets not found)
                        if (_staticSecretsCache.Count == 0)
                        {
                            var secretsPath = Path.Combine(env.ContentRootPath, "test_secrets.json");
                            if (File.Exists(secretsPath))
                            {
                                try
                                {
                                    var json = File.ReadAllText(secretsPath);
                                    var fileSecrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                                    foreach (var secret in fileSecrets)
                                    {
                                        _staticSecretsCache[secret.Key] = secret.Value;
                                    }
                                    _logger.LogInformation($"Loaded {_staticSecretsCache.Count} secrets from test_secrets.json (fallback). Consider migrating to User Secrets.");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to load test secrets from file.");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("No secrets found in User Secrets or test_secrets.json. Initialize secrets using: dotnet user-secrets set \"TestSecrets:KeyName\" \"value\"");
                            }
                        }
                    }
                }
            }
        }

        public void AddSecret(string key, string value)
        {
            _scopedSecrets[key] = value;
        }

        public string GetValue(string key)
        {
            if (_scopedSecrets.ContainsKey(key)) return _scopedSecrets[key];
            if (_staticSecretsCache != null && _staticSecretsCache.ContainsKey(key)) return _staticSecretsCache[key];
            return key;
        }

        public string ReplacePlaceholders(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Use static compiled regex for better performance
            return PlaceholderRegex.Replace(input, match =>
            {
                var key = match.Groups[1].Value;
                if (_scopedSecrets.TryGetValue(key, out var scopedValue)) return scopedValue;
                if (_staticSecretsCache != null && _staticSecretsCache.TryGetValue(key, out var staticValue)) return staticValue;
                
                return match.Value; // Return original if not found
            });
        }

        public string MaskLiterals(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = input;
            
            // Mask UI scoped variables
            foreach (var secret in _scopedSecrets)
            {
                if (!string.IsNullOrEmpty(secret.Value))
                {
                    result = result.Replace(secret.Value, $"{{{{{secret.Key}}}}}", StringComparison.OrdinalIgnoreCase);
                }
            }
            
            // Mask JSON disk variables
            if (_staticSecretsCache != null)
            {
                foreach (var secret in _staticSecretsCache)
                {
                    if (!string.IsNullOrEmpty(secret.Value))
                    {
                        result = result.Replace(secret.Value, $"{{{{{secret.Key}}}}}", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            return result;
        }
    }
}
