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

    // Retrieve GitHub token (from configuration or environment)
    string token = configuration["GITHUB_TOKEN"] ??
                   Environment.GetEnvironmentVariable("GITHUB_TOKEN") ??
                   throw new InvalidOperationException("Make sure to add GITHUB_TOKEN value to the user secrets or environment variables.");

    // Configure conversation logging
    ConversationLogger.ConfigureLogging(configuration);
    var logger = new ConversationLogger();

    // Load model configurations for both chatbots
    var modelA = configuration.GetSection("Models:ModelA").Get<ChatModelConfig>();
    var modelB = configuration.GetSection("Models:ModelB").Get<ChatModelConfig>();

    // Get the number of conversation rounds from configuration.
    int numberOfRounds = configuration.GetValue<int>("NumberOfRounds");

    // Load the model endpoint from configuration.
    string modelEndpointStr = configuration["ModelEndpoint"] ?? "https://models.inference.ai.azure.com";
    Uri modelEndpoint = new Uri(modelEndpointStr);

    // Create chat clients.
    IChatClient clientA = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelA.Name);
    IChatClient clientB = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelB.Name);

    // Instantiate ChatBot objects (ChatBot is defined in a separate file).
    ChatBot botA = new ChatBot(modelA.Name, clientA, modelA.InitalPrompt);
    ChatBot botB = new ChatBot(modelB.Name, clientB, modelB.InitalPrompt);

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
    // Loop for the configured number of rounds.
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
