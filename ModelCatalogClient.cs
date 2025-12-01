using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using NLog;

internal class ModelCatalogClient
{
    private static readonly Logger Diagnostics = LogManager.GetLogger("Diagnostics");
    private readonly HttpClient _httpClient;

    public ModelCatalogClient(string token)
    {
        Diagnostics.Info("Initializing ModelCatalogClient with masked token length: {length}", token?.Length ?? 0);
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ai-conversation/1.0");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<ModelMetadata>> GetModelsAsync()
    {
        Diagnostics.Info("Starting model retrieval from GitHub Models API...");
        var requestUri = new Uri("https://api.github.com/models");
        Diagnostics.Info("GET {uri}", requestUri);

        using var response = await _httpClient.GetAsync(requestUri);
        Diagnostics.Info("Model API response status: {status} ({reason})", (int)response.StatusCode, response.ReasonPhrase);

        var content = await response.Content.ReadAsStringAsync();
        Diagnostics.Info("Model API content length: {length} characters", content?.Length ?? 0);
        response.EnsureSuccessStatusCode();

        Diagnostics.Info("Parsing model payload...");
        using var document = JsonDocument.Parse(content);
        var models = ParseModels(document.RootElement).ToList();
        Diagnostics.Info("Parsed {count} models from API response", models.Count);
        return models;
    }

    private static IEnumerable<ModelMetadata> ParseModels(JsonElement root)
    {
        IEnumerable<JsonElement> modelElements = Enumerable.Empty<JsonElement>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            Diagnostics.Info("Root payload is an array; using root elements as models.");
            modelElements = root.EnumerateArray();
        }
        else if (root.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
        {
            Diagnostics.Info("Found 'models' array in payload; using nested models.");
            modelElements = modelsProp.EnumerateArray();
        }
        else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
        {
            Diagnostics.Info("Found 'data' array in payload; using nested models.");
            modelElements = dataProp.EnumerateArray();
        }
        else
        {
            Diagnostics.Info("No recognizable model array shape found; returning empty set.");
        }

        foreach (var element in modelElements)
        {
            yield return ParseModel(element);
        }
    }

    private static ModelMetadata ParseModel(JsonElement element)
    {
        static string GetString(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        static int? GetInt(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        static string GetModalities(JsonElement el)
        {
            if (el.TryGetProperty("modalities", out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
            {
                return string.Join(", ", arrayProp.EnumerateArray()
                    .Where(p => p.ValueKind == JsonValueKind.String)
                    .Select(p => p.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim()));
            }

            return GetString(el, "modality");
        }

        var name = GetString(element, "name", "id");
        var description = GetString(element, "description", "summary");
        var owner = GetString(element, "owned_by", "publisher", "provider");
        var source = GetString(element, "source", "url", "endpoint_url");
        var contextLength = GetInt(element, "context_length", "context_window");
        var modalities = GetModalities(element);

        Diagnostics.Info("Parsed model: {name}, owner={owner}, contextLength={context}, modalities={modalities}, source={source}",
            string.IsNullOrWhiteSpace(name) ? "<unknown>" : name,
            string.IsNullOrWhiteSpace(owner) ? "<none>" : owner,
            contextLength?.ToString() ?? "<null>",
            string.IsNullOrWhiteSpace(modalities) ? "<none>" : modalities,
            string.IsNullOrWhiteSpace(source) ? "<none>" : source);

        return new ModelMetadata
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<unknown>" : name,
            Description = description,
            Owner = owner,
            Source = source,
            ContextLength = contextLength,
            Modalities = modalities,
        };
    }
}

internal class ModelMetadata
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public int? ContextLength { get; set; }

    public string Modalities { get; set; } = string.Empty;
}
