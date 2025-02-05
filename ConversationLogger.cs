using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

public class ConversationLogger
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Configures NLog to log messages into a Markdown document.
    /// The directory for the log file is loaded from configuration, and the file name is generated in code.
    /// </summary>
    public static void ConfigureLogging(IConfiguration configuration)
    {
        var config = new LoggingConfiguration();

        // Load the directory path from configuration (default to current directory if not set).
        string logDirectory = configuration["Logger:Directory"] ?? ".";
        if (!logDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            logDirectory += Path.DirectorySeparatorChar;
        }

        // Construct the file name with a datestamp.
        string dateStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string fileName = $"conversationLog-{dateStamp}.md";
        string logFilePath = Path.Combine(logDirectory, fileName);

        // Configure a file target for Markdown output.
        var mdTarget = new FileTarget("mdLog")
        {
            FileName = logFilePath,
            // A simple Markdown header. You can adjust as needed.
            Header = "# Conversation Log\n\n---\n\n",
            Footer = "\n\n---\n_End of Conversation Log_",
            Layout = "${message}",
            KeepFileOpen = true,
            ConcurrentWrites = true
        };

        config.AddTarget(mdTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, mdTarget);
        LogManager.Configuration = config;
    }

    /// <summary>
    /// Logs a chatbot's response as a Markdown entry.
    /// The response is output with the model name in bold and with an inline HTML span to color the text.
    /// Newline characters are preserved.
    /// </summary>
    /// <param name="modelName">The name of the model sending the response.</param>
    /// <param name="response">The text of the response.</param>
    /// <param name="color">The HTML color to use for the text.</param>
    public void LogResponse(string modelName, string response, string color)
    {
        // Replace newline characters with Markdown line breaks (two spaces then newline)
        // Alternatively, you can leave the raw newlines if your Markdown viewer preserves them.
        string mdResponse = Regex.Replace(response, @"(\r\n|\n|\r)", "  \n");

        // Build the Markdown log entry.
        // We use a bold header, and then an inline HTML span for the colored text.
        string logEntry = $"**Response from {modelName}:**\n\n" +
                          $"<span style=\"color:{color};\">{mdResponse}</span>\n\n";
        Logger.Info(logEntry);
    }
}
