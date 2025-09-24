using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Start
{
    public class Function1
    {
        private readonly ILogger<Function1> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
        }

        private string SerializeToJson(object obj)
        {
            return JsonSerializer.Serialize(obj, _jsonOptions);
        }

        private string GenerateHtmlResponse(string userQuestion, string aiResponse)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>AI Chat Response</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 800px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
            color: #333;
        }}
        .container {{
            background: white;
            border-radius: 12px;
            padding: 30px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }}
        h1 {{
            color: #2c3e50;
            text-align: center;
            margin-bottom: 30px;
            border-bottom: 3px solid #3498db;
            padding-bottom: 15px;
        }}
        .chat-message {{
            margin: 20px 0;
            padding: 15px;
            border-radius: 8px;
            border-left: 4px solid;
        }}
        .user-message {{
            background-color: #e3f2fd;
            border-left-color: #2196f3;
        }}
        .ai-message {{
            background-color: #f1f8e9;
            border-left-color: #4caf50;
        }}
        .message-label {{
            font-weight: bold;
            margin-bottom: 8px;
            font-size: 14px;
            text-transform: uppercase;
            letter-spacing: 1px;
        }}
        .message-content {{
            line-height: 1.6;
            white-space: pre-wrap;
        }}
        .timestamp {{
            color: #666;
            font-size: 12px;
            margin-top: 10px;
            text-align: right;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 14px;
        }}
        .format-toggle {{
            text-align: center;
            margin-bottom: 20px;
        }}
        .format-link {{
            display: inline-block;
            margin: 0 10px;
            padding: 8px 16px;
            background: #3498db;
            color: white;
            text-decoration: none;
            border-radius: 6px;
            font-size: 14px;
        }}
        .format-link:hover {{
            background: #2980b9;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>🤖 AI Chat Response</h1>
        
        <div class='format-toggle'>
            <a href='?question={System.Uri.EscapeDataString(userQuestion)}&format=html' class='format-link'>HTML View</a>
            <a href='?question={System.Uri.EscapeDataString(userQuestion)}&format=json' class='format-link'>JSON View</a>
        </div>
        
        <div class='chat-message user-message'>
            <div class='message-label'>👤 Your Question:</div>
            <div class='message-content'>{System.Net.WebUtility.HtmlEncode(userQuestion)}</div>
            <div class='timestamp'>{DateTime.Now.ToString("dd-MM-yyyy hh:mm tt")}</div>
        </div>
        
        <div class='chat-message ai-message'>
            <div class='message-label'>🤖 AI Response:</div>
            <div class='message-content'>{System.Net.WebUtility.HtmlEncode(aiResponse)}</div>
            <div class='timestamp'>{DateTime.Now.ToString("dd-MM-yyyy hh:mm tt")}</div>
        </div>
        
        <div class='footer'>
            <p>Powered by Google Gemini AI • Azure Functions</p>
        </div>
    </div>
</body>
</html>";
        }

        [Function("AI")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            string? question = req.Query["question"];
            ResponseFormat format = req.Query.ContainsKey("format") && Enum.TryParse(req.Query["format"], true, out ResponseFormat parsedFormat)
                ? parsedFormat
                : ResponseFormat.Html; // Default to HTML

            if (string.IsNullOrEmpty(question))
            {
                if (format == ResponseFormat.Json)
                {
                    return new BadRequestObjectResult(new
                    {
                        error = "Please pass a question in the query string",
                        example = "?question=What is AI?&format=json",
                        success = false
                    });
                }
                else
                {
                    return new ContentResult
                    {
                        Content = GenerateErrorPage("Please pass a question in the query string (e.g., ?question=What is AI?)"),
                        ContentType = "text/html",
                        StatusCode = 400
                    };
                }
            }

            try
            {
                var gemini = new Gemini();
                var response = await gemini.GetResponseAsync(question);

                // Create chat objects
                var userMessage = new ChatUser(ChatRole.USER, question);
                var aiMessage = new ChatUser(ChatRole.AI, response);

                // Return based on requested format
                if (format == ResponseFormat.Json)
                {
                    var jsonResponse = new
                    {
                        userMessage = new
                        {
                            role = userMessage.Role.ToString(),
                            message = userMessage.message,
                            timestamp = userMessage.Timestamp
                        },
                        aiMessage = new
                        {
                            role = aiMessage.Role.ToString(),
                            message = aiMessage.message,
                            timestamp = aiMessage.Timestamp
                        },
                        format = "json",
                        success = true
                    };

                    string jsonResult = SerializeToJson(jsonResponse);
                    return new ContentResult
                    {
                        Content = jsonResult,
                        ContentType = "application/json",
                        StatusCode = 200
                    };
                }
                else
                {
                    string htmlContent = GenerateHtmlResponse(question, response);
                    return new ContentResult
                    {
                        Content = htmlContent,
                        ContentType = "text/html",
                        StatusCode = 200
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API");

                if (format == ResponseFormat.Json)
                {
                    return new ObjectResult(new
                    {
                        error = $"Error: {ex.Message}",
                        success = false,
                        format = "json"
                    })
                    {
                        StatusCode = 500
                    };
                }
                else
                {
                    return new ContentResult
                    {
                        Content = GenerateErrorPage($"Error: {ex.Message}"),
                        ContentType = "text/html",
                        StatusCode = 500
                    };
                }
            }
        }

        private string GenerateErrorPage(string errorMessage)
        {
            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Error - AI Chat</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
            color: #333;
        }}
        .error-container {{
            background: white;
            border-radius: 12px;
            padding: 30px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            text-align: center;
            border-left: 5px solid #e74c3c;
        }}
        .error-icon {{
            font-size: 48px;
            margin-bottom: 20px;
        }}
        .error-message {{
            color: #e74c3c;
            font-size: 18px;
            margin-bottom: 20px;
        }}
        .example {{
            background: #f8f9fa;
            padding: 15px;
            border-radius: 8px;
            font-family: monospace;
            color: #666;
            margin: 10px 0;
        }}
    </style>
</head>
<body>
    <div class='error-container'>
        <div class='error-icon'>❌</div>
        <div class='error-message'>{System.Net.WebUtility.HtmlEncode(errorMessage)}</div>
        <div class='example'>
            HTML: /api/AI?question=What is AI?&format=html
        </div>
        <div class='example'>
            JSON: /api/AI?question=What is AI?&format=json
        </div>
    </div>
</body>
</html>";
        }
    }

    public class Gemini
    {
        private readonly GoogleAI _google;
        private readonly GenerativeModel _model;
        private readonly string _geminiApiKey;

        public Gemini()
        {
            _google = new GoogleAI(_geminiApiKey);
            _geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
            _model = _google.GenerativeModel(model: Model.Gemini25Flash);
        }

        public async Task<string> GetResponseAsync(string prompt)
        {
            var response = await _model.GenerateContent(prompt);
            return response.Text ?? string.Empty;
        }
    }

    public enum ResponseFormat
    {
        Json,
        Html
    }

    public enum ChatRole
    {
        USER,
        AI
    }

    public class ChatUser
    {
        public ChatRole Role { get; set; } = ChatRole.USER;
        public string message { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;

        public ChatUser()
        {
            Timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm tt");
        }

        public ChatUser(ChatRole role, string message)
        {
            Role = role;
            this.message = message;
            Timestamp = DateTime.Now.ToString("dd-MM-yyyy hh:mm tt");
        }
    }
}
