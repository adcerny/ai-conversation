using Microsoft.Extensions.AI;

/// <summary>
/// Represents a chatbot with configuration and response handling.
/// </summary>
public class ChatBot
{
    public string Name { get; }
    public IChatClient Client { get; }
    public string InitialPromptTemplate { get; }

    public ChatBot(string name, IChatClient client, string initialPromptTemplate)
    {
        Name = name;
        Client = client;
        InitialPromptTemplate = initialPromptTemplate;
    }

    /// <summary>
    /// Formats the initial prompt using this bot's name and its partner's name.
    /// </summary>
    public string GetFormattedInitialPrompt(string partnerName)
    {
        return string.Format(InitialPromptTemplate, Name, partnerName);
    }

    /// <summary>
    /// Sends the given prompt to the chat client, streams and logs the response, then returns it.
    /// </summary>
    public async Task<string> SendAndLogResponseAsync(
        string prompt,
        string header,
        ConsoleColor consoleColor,
        ConversationLogger logger)
    {
        Console.WriteLine(header);
        Console.ForegroundColor = consoleColor;
        string response = "";

        await foreach (var item in Client.CompleteStreamingAsync(prompt))
        {
            Console.Write(item);
            response += item;
        }

        Console.ResetColor();
        return response;
    }
}