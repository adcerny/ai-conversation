using System;
using System.Linq;
using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Yaml;

// Helper method to get list of available models from configuration
static List<string> GetAvailableModels(IConfiguration configuration)
{
    var models = configuration.GetSection("AvailableModels").Get<List<string>>();
    if (models == null || models.Count == 0)
    {
        // Fallback to a basic list if not configured
        return new List<string> { "gpt-4o-mini", "gpt-4o", "Phi-4-reasoning" };
    }
    return models;
}

// Helper method to prompt user to select a model
static string SelectModel(List<string> availableModels, string botName, string? defaultModel)
{
    Console.WriteLine($"\nAvailable models for {botName}:");
    for (int i = 0; i < availableModels.Count; i++)
    {
        string marker = (defaultModel != null && availableModels[i] == defaultModel) ? " (default)" : "";
        Console.WriteLine($"  {i + 1}. {availableModels[i]}{marker}");
    }

    Console.Write($"\nEnter the number of the model for {botName}" + 
                  (defaultModel != null ? $" [default: {defaultModel}]: " : ": "));
    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) && defaultModel != null)
    {
        return defaultModel;
    }

    if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availableModels.Count)
    {
        return availableModels[choice - 1];
    }

    Console.WriteLine($"Invalid selection. Using default: {defaultModel ?? availableModels[0]}");
    return defaultModel ?? availableModels[0];
}

try
{
    // Build configuration (using user secrets and appsettings.yaml)
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<ChatBot>() // ChatBot is defined in a separate file.
        .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
        .Build();

    // Choose subject
    string subjectName;
    
    if (args.Length > 0)
    {
        // Use command line argument if provided
        subjectName = args[0];
    }
    else
    {
        // Get all available subjects from configuration
        var subjectsSection = configuration.GetSection("Subjects");
        var subjectChildren = subjectsSection.GetChildren().ToList();
        var availableSubjects = subjectChildren.Select(s => s.Key).ToList();
        var availableTitles = subjectChildren.Select(s => s["Title"] ?? s.Key).ToList();

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
        }
        else
        {
            // Fall back to SelectedSubject in config or default
            subjectName = configuration["SelectedSubject"] ?? "MetaphysicsSymposium";
            Console.WriteLine($"Invalid selection. Using default: {subjectName}");
        }
    }

    Console.WriteLine($"\nUsing subject: {subjectName}");

    // Choose number of rounds
    int numberOfRounds;
    
    if (args.Length > 1 && int.TryParse(args[1], out int argRounds))
    {
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

    // Configure conversation logging
    ConversationLogger.ConfigureLogging(configuration);
    var logger = new ConversationLogger();

    // Load the selected subject configuration
    var subjectSection = configuration.GetSection($"Subjects:{subjectName}");
    if (!subjectSection.Exists())
    {
        throw new InvalidOperationException($"Subject '{subjectName}' not found in configuration.");
    }

    var subjectConfig = subjectSection.Get<SubjectConfig>()
                        ?? throw new InvalidOperationException($"Failed to bind configuration for subject '{subjectName}'.");

    // Extract models from the subject
    if (!subjectConfig.Models.TryGetValue("ModelA", out var modelA) ||
        !subjectConfig.Models.TryGetValue("ModelB", out var modelB))
    {
        throw new InvalidOperationException($"Subject '{subjectName}' must define Models:ModelA and Models:ModelB.");
    }

    // Get subject title from configuration
    string subjectTitle = subjectSection["Title"] ?? subjectName;
    // Log introduction before conversation starts using the title
    string modelsList = $"{modelA.Name}, {modelB.Name}";
    logger.LogIntroduction(configuration, subjectTitle, modelsList, numberOfRounds);

    // Get available models and let user choose
    var availableModels = GetAvailableModels(configuration);
    
    // Prompt user to select models (or use command line args 3 and 4)
    string selectedModelA;
    string selectedModelB;
    
    if (args.Length > 2)
    {
        selectedModelA = args[2];
        Console.WriteLine($"Using ModelA from argument: {selectedModelA}");
    }
    else
    {
        selectedModelA = SelectModel(availableModels, "ModelA", modelA.Name);
    }
    
    if (args.Length > 3)
    {
        selectedModelB = args[3];
        Console.WriteLine($"Using ModelB from argument: {selectedModelB}");
    }
    else
    {
        selectedModelB = SelectModel(availableModels, "ModelB", modelB.Name);
    }
    
    Console.WriteLine($"\nModelA: {selectedModelA}");
    Console.WriteLine($"ModelB: {selectedModelB}");

    // Load the model endpoint from configuration.
    string modelEndpointStr = configuration["ModelEndpoint"] ?? "https://models.inference.ai.azure.com";
    Uri modelEndpoint = new Uri(modelEndpointStr);

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
        string newResponseA = await botA.SendAndLogResponseAsync(
            lastResponse,
            $"\n>>> {botA.Name} responding to {botB.Name}:",
            ConsoleColor.Green,
            logger,
            "#228B22",
            round);

        // Bot B responds to Bot A's message.
        string newResponseB = await botB.SendAndLogResponseAsync(
            newResponseA,
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