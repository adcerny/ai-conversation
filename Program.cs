using System;
using System.Collections.Generic;
using System.Linq;
using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Yaml;
using NLog;

// Helper method to provide a minimal fallback list when the API is unavailable
static List<ModelMetadata> GetFallbackModels(ChatModelConfig modelA, ChatModelConfig modelB)
{
    var defaults = new List<string?> { modelA.Name, modelB.Name, "gpt-4o-mini", "gpt-4o" };

    return defaults
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(name => new ModelMetadata { Name = name! })
        .ToList();
}

static string AppendReminderIfNeeded(string prompt, string reminderPrompt, int round, int reminderInterval)
{
    if (reminderInterval <= 0 || string.IsNullOrWhiteSpace(reminderPrompt))
    {
        return prompt;
    }

    if (round % reminderInterval != 0)
    {
        return prompt;
    }

    return $"{prompt}\n\nReminder: {reminderPrompt}";
}

// Helper method to prompt user to select a model
static string SelectModel(List<ModelMetadata> availableModels, string botName, string? defaultModel)
{
    Console.WriteLine($"\nAvailable models for {botName}:");
    for (int i = 0; i < availableModels.Count; i++)
    {
        var metadata = availableModels[i];
        var description = string.IsNullOrWhiteSpace(metadata.Description)
            ? string.Empty
            : $" - {metadata.Description}";
        var owner = string.IsNullOrWhiteSpace(metadata.Owner) ? string.Empty : $" ({metadata.Owner})";
        Console.WriteLine($"  {i + 1}. {metadata.Name}{owner}{description}");
    }

    Console.Write($"\nEnter the number of the model for {botName}: ");
    string? input = Console.ReadLine();

    while (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out int choice) || choice < 1 || choice > availableModels.Count)
    {
        Console.WriteLine("Invalid selection. Please enter a valid number.");
        Console.Write($"Enter the number of the model for {botName}: ");
        input = Console.ReadLine();
    }

    return availableModels[int.Parse(input) - 1].Name;
}

try
{
    // Build configuration (using user secrets and appsettings.yaml)
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<ChatBot>() // ChatBot is defined in a separate file.
        .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
        .Build();

    ConversationLogger.ConfigureLogging(configuration);
    var diagnostics = LogManager.GetLogger("Diagnostics");

    bool showModelSummary = args.Any(arg => string.Equals(arg, "--model-summary", StringComparison.OrdinalIgnoreCase));
    var positionalArgs = args
        .Where(arg => !string.Equals(arg, "--model-summary", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    diagnostics.Info("Configuration loaded. Diagnostics logging enabled={enabled}.", configuration["Logging:EnableDiagnostics"]);
    diagnostics.Info("Starting program with {argCount} argument(s). showModelSummary={showModelSummary}", positionalArgs.Length, showModelSummary);

    var logger = new ConversationLogger();

    // Choose subject
    string subjectName;
    
    if (positionalArgs.Length > 0)
    {
        diagnostics.Info("Selecting subject from command-line argument: {arg}", positionalArgs[0]);
        // Use command line argument if provided
        subjectName = positionalArgs[0];
    }
    else
    {
        diagnostics.Info("No subject argument provided; reading available subjects from configuration.");
        // Get all available subjects from configuration
        var subjectsSection = configuration.GetSection("Subjects");
        var subjectChildren = subjectsSection.GetChildren().ToList();
        var availableSubjects = subjectChildren.Select(s => s.Key).ToList();
        var availableTitles = subjectChildren.Select(s => s["Title"] ?? s.Key).ToList();

        diagnostics.Info("Found {count} subject(s) in configuration.", availableSubjects.Count);

        if (availableSubjects.Count == 0)
        {
            throw new InvalidOperationException("No subjects found in configuration.");
        }

        // Display available subjects using their Title
        Console.WriteLine("Available subjects:");
        for (int i = 0; i < availableTitles.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {availableTitles[i]}");
        }

        Console.Write("\nEnter the number of the subject you want to use: ");
        string? input = Console.ReadLine();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availableSubjects.Count)
        {
            subjectName = availableSubjects[choice - 1];
            diagnostics.Info("User selected subject by number: {subject}", subjectName);
        }
        else
        {
            // Fall back to SelectedSubject in config or default
            subjectName = configuration["SelectedSubject"] ?? "MetaphysicsSymposium";
            Console.WriteLine($"Invalid selection. Using default: {subjectName}");
            diagnostics.Info("Invalid subject selection; falling back to default {subject}", subjectName);
        }
    }

    Console.WriteLine($"\nUsing subject: {subjectName}");

    // Choose number of rounds
    int numberOfRounds;
    
    if (positionalArgs.Length > 1 && int.TryParse(positionalArgs[1], out int argRounds))
    {
        diagnostics.Info("Parsing number of rounds from argument: {rounds}", argRounds);
        // Use command line argument if provided
        numberOfRounds = argRounds;
        if (numberOfRounds < 1 || numberOfRounds > 500)
        {
            throw new ArgumentException("Number of rounds must be between 1 and 500.");
        }
    }
    else
    {
        // Prompt user for number of rounds
        Console.Write("\nEnter the number of rounds (1-500) [default: 25]: ");
        string? roundsInput = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(roundsInput))
        {
            numberOfRounds = 25; // Default
        }
        else if (int.TryParse(roundsInput, out int userRounds) && userRounds >= 1 && userRounds <= 500)
        {
            numberOfRounds = userRounds;
            diagnostics.Info("User entered number of rounds: {rounds}", numberOfRounds);
        }
        else
        {
            throw new ArgumentException("Invalid number of rounds. Must be between 1 and 500.");
        }
    }
    
    Console.WriteLine($"Number of rounds: {numberOfRounds}");

    // Retrieve GitHub token (from configuration or environment)
    string token = configuration["GITHUB_TOKEN"] ??
                   Environment.GetEnvironmentVariable("GITHUB_TOKEN") ??
                   throw new InvalidOperationException("Make sure to add GITHUB_TOKEN value to the user secrets or environment variables.");
    diagnostics.Info("Retrieved GitHub token from configuration/environment (length: {length}).", token.Length);

    var logger = new ConversationLogger();

    // Load the selected subject configuration
    var subjectSection = configuration.GetSection($"Subjects:{subjectName}");
    if (!subjectSection.Exists())
    {
        throw new InvalidOperationException($"Subject '{subjectName}' not found in configuration.");
    }

    var subjectConfig = subjectSection.Get<SubjectConfig>()
                        ?? throw new InvalidOperationException($"Failed to bind configuration for subject '{subjectName}'.");
    diagnostics.Info("Loaded subject configuration for {subject}", subjectName);

    // Extract models from the subject
    if (!subjectConfig.Models.TryGetValue("ModelA", out var modelA) ||
        !subjectConfig.Models.TryGetValue("ModelB", out var modelB))
    {
        throw new InvalidOperationException($"Subject '{subjectName}' must define Models:ModelA and Models:ModelB.");
    }
    diagnostics.Info("Subject models loaded: ModelA={modelA}, ModelB={modelB}", modelA.Name, modelB.Name);

    // Get subject title from configuration
    string subjectTitle = subjectSection["Title"] ?? subjectName;
    // Log introduction before conversation starts using the title
    string modelsList = $"{modelA.Name}, {modelB.Name}";
    logger.LogIntroduction(configuration, subjectTitle, modelsList, numberOfRounds);

    // Configure reminder prompt and interval
    string reminderPrompt = (subjectConfig.ReminderPrompt ?? string.Empty)
        .Replace("{subject}", subjectTitle)
        .Trim();

    int reminderInterval;
    if (positionalArgs.Length > 4 && int.TryParse(positionalArgs[4], out int argReminderInterval) && argReminderInterval > 0 && argReminderInterval <= 500)
    {
        reminderInterval = argReminderInterval;
        Console.WriteLine($"Using reminder interval from argument: every {reminderInterval} rounds.");
        diagnostics.Info("Reminder interval provided via argument: {interval}", reminderInterval);
    }
    else if (subjectConfig.ReminderInterval.HasValue && subjectConfig.ReminderInterval.Value > 0 && subjectConfig.ReminderInterval.Value <= 500)
    {
        reminderInterval = subjectConfig.ReminderInterval.Value;
        diagnostics.Info("Reminder interval provided via configuration: {interval}", reminderInterval);
    }
    else
    {
        Console.Write("\nEnter the reminder interval in rounds (1-500) [default: 5]: ");
        string? reminderInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(reminderInput))
        {
            reminderInterval = 5;
        }
        else if (int.TryParse(reminderInput, out int userInterval) && userInterval >= 1 && userInterval <= 500)
        {
            reminderInterval = userInterval;
            diagnostics.Info("User entered reminder interval: {interval}", reminderInterval);
        }
        else
        {
            throw new ArgumentException("Invalid reminder interval. Must be between 1 and 500.");
        }
    }

    if (string.IsNullOrEmpty(reminderPrompt))
    {
        reminderPrompt = $"Please keep the discussion focused on {subjectTitle}.";
        diagnostics.Info("No reminder prompt configured; using default for subject {subject}", subjectTitle);
    }

    // Get available models and let user choose
    var modelCatalogClient = new ModelCatalogClient(token);
    diagnostics.Info("Created ModelCatalogClient; beginning model fetch.");
    List<ModelMetadata> availableModels;
    try
    {
        var catalogModels = await modelCatalogClient.GetModelsAsync();
        availableModels = catalogModels.ToList();
        diagnostics.Info("Received {count} models from API.", availableModels.Count);
        if (availableModels.Count == 0)
        {
            diagnostics.Info("No models returned from API. Falling back to defaults.");
            availableModels = GetFallbackModels(modelA, modelB);
        }
    }
    catch (Exception ex)
    {
        diagnostics.Error(ex, "Failed to load models from API; using fallback list.");
        availableModels = GetFallbackModels(modelA, modelB);
    }

    diagnostics.Info("Final available model count: {count}", availableModels.Count);

    if (showModelSummary)
    {
        Console.WriteLine("\nModel catalog summary:");
        foreach (var model in availableModels.OrderBy(m => m.Name))
        {
            Console.WriteLine($"- {model.Name}");
            if (!string.IsNullOrWhiteSpace(model.Description))
            {
                Console.WriteLine($"  Description: {model.Description}");
            }

            if (!string.IsNullOrWhiteSpace(model.Owner))
            {
                Console.WriteLine($"  Owner: {model.Owner}");
            }

            if (model.ContextLength.HasValue)
            {
                Console.WriteLine($"  Context length: {model.ContextLength.Value}");
            }

            if (!string.IsNullOrWhiteSpace(model.Modalities))
            {
                Console.WriteLine($"  Modalities: {model.Modalities}");
            }

            if (!string.IsNullOrWhiteSpace(model.Source))
            {
                Console.WriteLine($"  Source: {model.Source}");
            }

            Console.WriteLine();
        }
    }

    // Prompt user to select models (or use command line args 3 and 4)
    string selectedModelA;
    string selectedModelB;

    if (positionalArgs.Length > 2)
    {
        selectedModelA = positionalArgs[2];
        Console.WriteLine($"Using ModelA from argument: {selectedModelA}");
        diagnostics.Info("ModelA provided by argument: {model}", selectedModelA);
    }
    else
    {
        selectedModelA = SelectModel(availableModels, "ModelA", modelA.Name);
        diagnostics.Info("ModelA selected via prompt: {model}", selectedModelA);
    }

    if (positionalArgs.Length > 3)
    {
        selectedModelB = positionalArgs[3];
        Console.WriteLine($"Using ModelB from argument: {selectedModelB}");
        diagnostics.Info("ModelB provided by argument: {model}", selectedModelB);
    }
    else
    {
        selectedModelB = SelectModel(availableModels, "ModelB", modelB.Name);
        diagnostics.Info("ModelB selected via prompt: {model}", selectedModelB);
    }
    
    Console.WriteLine($"\nModelA: {selectedModelA}");
    Console.WriteLine($"ModelB: {selectedModelB}");

    // Load the model endpoint from configuration.
    string modelEndpointStr = configuration["ModelEndpoint"] ?? "https://models.inference.ai.azure.com";
    Uri modelEndpoint = new Uri(modelEndpointStr);
    diagnostics.Info("Using model endpoint: {endpoint}", modelEndpoint);

    // Create chat clients.
    IChatClient clientA = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(selectedModelA);
    IChatClient clientB = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(selectedModelB);

    // Instantiate ChatBot objects
    ChatBot botA = new ChatBot(selectedModelA, clientA, modelA.InitialPrompt);
    ChatBot botB = new ChatBot(selectedModelB, clientB, modelB.InitialPrompt);

    // Format initial prompts with partner names.
    string introA = botA.GetFormattedInitialPrompt(botB.Name);
    string introB = botB.GetFormattedInitialPrompt(botA.Name);

    // --- Introduction Rounds ---
    string responseA = await botA.SendAndLogResponseAsync(
        introA,
        $">>> Sending introduction prompt to {botA.Name}:",
        ConsoleColor.Green,
        logger,
        "#228B22", // green
        1);

    string responseB = await botB.SendAndLogResponseAsync(
        introB,
        $"\n\n>>> Sending introduction prompt to {botB.Name}:",
        ConsoleColor.Blue,
        logger,
        "#1E90FF", // blue
        1);

    // Begin the conversation by sending botA's introduction response to botB.
    string lastResponse = await botB.SendAndLogResponseAsync(
        responseA,
        $"\n>>> {botB.Name} responding to {botA.Name}:",
        ConsoleColor.Blue,
        logger,
        "#1E90FF",
        1);

    // --- Conversation Loop ---
    for (int round = 2; round <= numberOfRounds; round++)
    {
        Console.WriteLine($"\n\n===== Conversation Round {round} =====");

        // Bot A responds to the previous message.
        string promptForA = AppendReminderIfNeeded(lastResponse, reminderPrompt, round, reminderInterval);

        string newResponseA = await botA.SendAndLogResponseAsync(
            promptForA,
            $"\n>>> {botA.Name} responding to {botB.Name}:",
            ConsoleColor.Green,
            logger,
            "#228B22",
            round);

        // Bot B responds to Bot A's message.
        string promptForB = AppendReminderIfNeeded(newResponseA, reminderPrompt, round, reminderInterval);

        string newResponseB = await botB.SendAndLogResponseAsync(
            promptForB,
            $"\n>>> {botB.Name} responding to {botA.Name}:",
            ConsoleColor.Blue,
            logger,
            "#1E90FF",
            round);

        lastResponse = newResponseB;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}