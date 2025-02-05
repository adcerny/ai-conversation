﻿using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

try
{
    // Build configuration (user secrets and appsettings.json)
    var builder = new ConfigurationBuilder()
        .AddUserSecrets<Program>()
        .AddJsonFile("appsettings.json", optional: true);
    var configuration = builder.Build();

    // Get the GitHub token (or throw an exception if missing)
    string token = configuration["GITHUB_TOKEN"] ??
        Environment.GetEnvironmentVariable("GITHUB_TOKEN") ??
        throw new InvalidOperationException("Make sure to add GITHUB_TOKEN value to the user secrets or environment variables.");

    // Configure conversation logging using configuration.
    ConversationLogger.ConfigureLogging(configuration);
    var logger = new ConversationLogger();

    // Load model configuration from the nested configuration sections.
    var modelAConfig = configuration.GetSection("Models:ModelA").Get<ChatModelConfig>();
    var modelBConfig = configuration.GetSection("Models:ModelB").Get<ChatModelConfig>();

    // Load the model endpoint from configuration.
    string modelEndpointStr = configuration["ModelEndpoint"] ?? "https://models.inference.ai.azure.com";
    Uri modelEndpoint = new Uri(modelEndpointStr);

    // Create two chat clients using the configured model names.
    IChatClient clientA = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelAConfig.Name);
    IChatClient clientB = new ChatCompletionsClient(modelEndpoint, new AzureKeyCredential(token))
        .AsChatClient(modelBConfig.Name);

    // Set up initial prompts.
    string introA = String.Format(modelBConfig.InitalPrompt, modelAConfig.Name, modelBConfig.Name);
    string introB = String.Format(modelBConfig.InitalPrompt, modelBConfig.Name, modelAConfig.Name);

    // --- Introduction for Model A ---
    Console.WriteLine($">>> Sending introduction prompt to {modelAConfig.Name}:");
    Console.ForegroundColor = ConsoleColor.Red;
    string responseA = "";
    await foreach (var item in clientA.CompleteStreamingAsync(introA))
    {
        Console.Write(item);
        responseA += item;
    }
    Console.ResetColor();
    logger.LogResponse(modelAConfig.Name, responseA, modelAConfig.Color);

    // --- Introduction for Model B ---
    Console.WriteLine($"\n\n>>> Sending introduction prompt to {modelBConfig.Name}:");
    Console.ForegroundColor = ConsoleColor.Blue;
    string responseB = "";
    await foreach (var item in clientB.CompleteStreamingAsync(introB))
    {
        Console.Write(item);
        responseB += item;
    }
    Console.ResetColor();
    logger.LogResponse(modelBConfig.Name, responseB, modelBConfig.Color);

    Console.WriteLine();

    // Let the two models converse for 5 rounds.
    // In each round, one model's entire response becomes the prompt for the other.
    for (int round = 1; round <= 5; round++)
    {
        Console.WriteLine($"\n\n===== Conversation Round {round} =====");

        // Model A responds to Model B's message.
        Console.WriteLine($"\n>>> {modelAConfig.Name} responding to {modelBConfig.Name}:");
        Console.ForegroundColor = ConsoleColor.Red;
        string newResponseA = "";
        await foreach (var item in clientA.CompleteStreamingAsync(responseB))
        {
            Console.Write(item);
            newResponseA += item;
        }
        Console.ResetColor();
        logger.LogResponse(modelAConfig.Name, newResponseA, modelAConfig.Color);

        // Model B responds to Model A's message.
        Console.WriteLine($"\n\n>>> {modelBConfig.Name} responding to {modelAConfig.Name}:");
        Console.ForegroundColor = ConsoleColor.Blue;
        string newResponseB = "";
        await foreach (var item in clientB.CompleteStreamingAsync(newResponseA))
        {
            Console.Write(item);
            newResponseB += item;
        }
        Console.ResetColor();
        logger.LogResponse(modelBConfig.Name, newResponseB, modelBConfig.Color);

        // Update responses for the next round.
        responseA = newResponseA;
        responseB = newResponseB;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}