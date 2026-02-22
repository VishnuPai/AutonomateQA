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
        void LoadCsvForCurrentRun(string csvPath);
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
        /// <summary>When true, env-specific CSV was requested but file was missing; do not use static cache to avoid wrong credentials.</summary>
        private bool _refuseStaticCacheForThisRun;
        private readonly ILogger<TestDataManager> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _contentRootPath;

        public TestDataManager(IConfiguration configuration, IWebHostEnvironment env, ILogger<TestDataManager> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _contentRootPath = env?.ContentRootPath ?? Directory.GetCurrentDirectory();
            
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
                                    _logger.LogInformation("Loaded {Count} secrets from test_secrets.json (fallback). Consider migrating to User Secrets.", _staticSecretsCache.Count);
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

                        // Optional: load test data from CSV (per-environment). Overrides/merges with existing keys.
                        var csvPath = _configuration["TestData:CsvPath"]?.Trim();
                        if (!string.IsNullOrEmpty(csvPath))
                        {
                            var fullPath = Path.IsPathRooted(csvPath) ? csvPath : Path.Combine(env.ContentRootPath, csvPath);
                            if (File.Exists(fullPath))
                            {
                                try
                                {
                                    var csvCount = LoadCsvIntoCache(fullPath, _staticSecretsCache);
                                    _logger.LogInformation("Loaded {Count} test data entries from CSV: {Path}", csvCount, csvPath);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to load test data CSV: {Path}", fullPath);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Test data CSV not found: {Path}", fullPath);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Loads CSV into the cache. Supports two formats:
        /// (1) Key,Value with one row per variable; (2) Single data row with header as variable names.
        /// </summary>
        private static int LoadCsvIntoCache(string fullPath, Dictionary<string, string> cache)
        {
            var lines = File.ReadAllLines(fullPath);
            if (lines.Length == 0) return 0;
            var added = 0;
            var header = ParseCsvLine(lines[0]);
            if (header.Count >= 2 && string.Equals(header[0], "Key", StringComparison.OrdinalIgnoreCase) && string.Equals(header[1], "Value", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 1; i < lines.Length; i++)
                {
                    var row = ParseCsvLine(lines[i]);
                    if (row.Count >= 2 && !string.IsNullOrWhiteSpace(row[0]))
                    {
                        cache[row[0].Trim()] = row[1].Trim();
                        added++;
                    }
                }
            }
            else if (lines.Length >= 2)
            {
                var keys = header;
                var values = ParseCsvLine(lines[1]);
                for (var j = 0; j < keys.Count && j < values.Count; j++)
                {
                    var k = keys[j].Trim();
                    if (string.IsNullOrEmpty(k)) continue;
                    cache[k] = values[j].Trim();
                    added++;
                }
            }
            return added;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var list = new List<string>();
            var inQuotes = false;
            var current = new System.Text.StringBuilder();
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (inQuotes)
                {
                    current.Append(c);
                }
                else if (c == ',')
                {
                    list.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            list.Add(current.ToString().Trim());
            return list;
        }

        public void LoadCsvForCurrentRun(string csvPath)
        {
            _refuseStaticCacheForThisRun = false;
            if (string.IsNullOrWhiteSpace(csvPath)) return;
            var fullPath = Path.IsPathRooted(csvPath) ? csvPath : Path.Combine(_contentRootPath, csvPath.Trim());
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Test data CSV for run not found: {Path}. Will not use static/default credentials for this run to avoid wrong env.", fullPath);
                _refuseStaticCacheForThisRun = true;
                return;
            }
            _scopedSecrets.Clear();
            var count = LoadCsvIntoCache(fullPath, _scopedSecrets);
            _logger.LogInformation("Using test data for this run from: {Path} ({Count} entries)", csvPath, count);
        }

        public void AddSecret(string key, string value)
        {
            _scopedSecrets[key] = value;
        }

        public string GetValue(string key)
        {
            if (_scopedSecrets.TryGetValue(key, out var scoped)) return scoped;
            if (!_refuseStaticCacheForThisRun && _staticSecretsCache != null && _staticSecretsCache.TryGetValue(key, out var stat)) return stat;
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
                if (!_refuseStaticCacheForThisRun && _staticSecretsCache != null && _staticSecretsCache.TryGetValue(key, out var staticValue)) return staticValue;

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
