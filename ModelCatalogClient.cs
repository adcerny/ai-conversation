using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System;

internal class ModelCatalogClient
{
    private readonly HttpClient _httpClient;

    public ModelCatalogClient(string token)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "ai-conversation/1.0");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<ModelMetadata>> GetModelsAsync()
    {
        Console.WriteLine("Fetching models from GitHub Models API...");
        using var response = await _httpClient.GetAsync("https://api.github.com/models");
        Console.WriteLine($"Model API response: {(int)response.StatusCode} {response.ReasonPhrase}");

        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Model API content length: {content?.Length ?? 0} characters");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(content);
        var models = ParseModels(document.RootElement).ToList();
        Console.WriteLine($"Parsed {models.Count} models from API response.");
        return models;
    }

    private static IEnumerable<ModelMetadata> ParseModels(JsonElement root)
    {
        IEnumerable<JsonElement> modelElements = Enumerable.Empty<JsonElement>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            modelElements = root.EnumerateArray();
        }
        else if (root.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
        {
            modelElements = modelsProp.EnumerateArray();
        }
        else if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
        {
            modelElements = dataProp.EnumerateArray();
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
