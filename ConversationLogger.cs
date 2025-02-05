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
    /// Configures NLog to log messages into an HTML document using a Bootstrap-based template.
    /// The directory for the log file is loaded from configuration, and the file name is set in code.
    /// </summary>
    public static void ConfigureLogging(IConfiguration configuration)
    {
        var config = new LoggingConfiguration();

        // Load the directory path from configuration.
        string logDirectory = configuration["Logger:Directory"] ?? ".";
        // Ensure the directory path ends with a directory separator.
        if (!logDirectory.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            logDirectory += Path.DirectorySeparatorChar;
        }

        // Construct the file name with a datestamp.
        string dateStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string fileName = $"conversationLog-{dateStamp}.html";

        // Combine directory and file name to get the full file path.
        string logFilePath = Path.Combine(logDirectory, fileName);

        var htmlTarget = new FileTarget("htmlLog")
        {
            FileName = logFilePath,
            // Use your Bootstrap-based HTML template for the header.
            Header = @"<!doctype html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <link rel=""icon"" href=""https://getbootstrap.com/docs/4.0/assets/img/favicons/favicon.ico"">
    <title>Cover Template for Bootstrap</title>
    <!-- Bootstrap core CSS -->
    <link href=""https://getbootstrap.com/docs/4.0/dist/css/bootstrap.min.css"" rel=""stylesheet"">
    <!-- Custom styles for this template -->
    <link href=""https://getbootstrap.com/docs/4.0/examples/cover/cover.css"" rel=""stylesheet"">
</head>
<body class=""text-center"">
    <div class=""d-flex h-100 p-3 mx-auto flex-column"">
        <main class=""col-12 col-md-9 col-xl-8 py-md-3 pl-md-5 bd-content"" role=""main"">
",
            Footer = @"
        </main>
    </div>
</body>
</html>",
            // Output the message directly.
            Layout = "${message}",
            KeepFileOpen = true,
            ConcurrentWrites = true
        };

        config.AddTarget(htmlTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, htmlTarget);
        LogManager.Configuration = config;
    }

    /// <summary>
    /// Logs a chatbot's response as a left-aligned paragraph with the specified color,
    /// preserving any line breaks from the response.
    /// </summary>
    /// <param name="modelName">The name of the model sending the response.</param>
    /// <param name="response">The text of the response.</param>
    /// <param name="color">The HTML color to use for the text.</param>
    public void LogResponse(string modelName, string response, string color)
    {
        // Replace newline characters with <br> tags so line breaks appear in HTML.
        string htmlResponse = Regex.Replace(response, @"(\r\n|\n|\r)", "<br>");
        string logEntry = $"<p class=\"lead text-left fw-bold\" style=\"color:{color};\">Response from {modelName}: {htmlResponse}</p>";
        Logger.Info(logEntry);
    }
}
