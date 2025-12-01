using System;
using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Yaml;

try
{
    // Build configuration (using user secrets and appsettings.yaml)
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<ChatBot>() // ChatBot is defined in a separate file.
        .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
        .Build();

    // Choose subject
    // Priority: command line arg > SelectedSubject in config > hard coded default
    string subjectName =
        (args.Length > 0 ? args[0] : null)
        ?? configuration["SelectedSubject"]
        ?? "MetaphysicsSymposium";

    Console.WriteLine($"Using subject: {subjectName}");

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

    // Extract models and rounds from the subject
    int numberOfRounds = subjectConfig.NumberOfRounds;

    if (!subjectConfig.Models.TryGetValue("ModelA", out var modelA) ||
        !subjectConfig.Models.TryGetValue("ModelB", out var modelB))
    {
        throw new InvalidOperationException($"Subject '{subjectName}' must define Models:ModelA and Models:ModelB.");
    }

    // Load the model endpoint from configuration.
    string modelEndpointStr = configuration["ModelEndpoint"] ?? "https://models.inference.ai.azure.com";
    Uri modelEndpoint = new Uri(modelEndpointStr);

    // Create chat clients.
    IChatClient clientA = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelA.Name);
    IChatClient clientB = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelB.Name);

    // Instantiate ChatBot objects
    ChatBot botA = new ChatBot(modelA.Name, clientA, modelA.InitialPrompt);
    ChatBot botB = new ChatBot(modelB.Name, clientB, modelB.InitialPrompt);

    // Format initial prompts with partner names.
    string introA = botA.GetFormattedInitialPrompt(botB.Name);
    string introB = botB.GetFormattedInitialPrompt(botA.Name);

    // --- Introduction Rounds ---
    string responseA = await botA.SendAndLogResponseAsync(
        introA,
        $">>> Sending introduction prompt to {botA.Name}:",
        ConsoleColor.Green,
        logger);

    string responseB = await botB.SendAndLogResponseAsync(
        introB,
        $"\n\n>>> Sending introduction prompt to {botB.Name}:",
        ConsoleColor.Blue,
        logger);

    // Begin the conversation by sending botA's introduction response to botB.
    string lastResponse = await botB.SendAndLogResponseAsync(
        responseA,
        $"\n>>> {botB.Name} responding to {botA.Name}:",
        ConsoleColor.Blue,
        logger);

    // --- Conversation Loop ---
    for (int round = 2; round <= numberOfRounds; round++)
    {
        Console.WriteLine($"\n\n===== Conversation Round {round} =====");

        // Bot A responds to the previous message.
        string newResponseA = await botA.SendAndLogResponseAsync(
            lastResponse,
            $"\n>>> {botA.Name} responding to {botB.Name}:",
            ConsoleColor.Green,
            logger);

        // Bot B responds to Bot A's message.
        string newResponseB = await botB.SendAndLogResponseAsync(
            newResponseA,
            $"\n>>> {botB.Name} responding to {botA.Name}:",
            ConsoleColor.Blue,
            logger);

        lastResponse = newResponseB;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}