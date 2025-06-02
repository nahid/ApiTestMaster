using System.CommandLine;
using Apify.Models;
using Apify.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Apify.Commands
{
    public class CreateMockCommand : Command
    {
        private const string DefaultApiDirectory = ".apify";
        private const string MocksSubDirectory = "mocks"; // Subdirectory for mock definitions

        public CreateMockCommand() : base("mock", "Create a new mock API response definition.")
        {
            var fileOption = new Option<string>(
                aliases: new[] { "--file", "-f" },
                description: "The file path for the new mock API definition (e.g., users.getUser). '.mock.json' extension is added automatically. Stored under '.apify/mocks/'."
            ) { IsRequired = true };

            var forceOption = new Option<bool>(
                aliases: new[] { "--force", "-o" },
                getDefaultValue: () => false,
                description: "Force overwrite if the file already exists."
            );

            // Options for non-interactive mode
            var nameOption = new Option<string>("--name", "Mock API name.");
            var endpointOption = new Option<string>("--endpoint", "Endpoint path (e.g., /api/users/:id).");
            var methodOption = new Option<string>("--method", "HTTP method (e.g., GET, POST).");
            var statusCodeOption = new Option<int>("--status-code", () => 200, "HTTP status code for the response.");
            var contentTypeOption = new Option<string>("--content-type", () => "application/json", "Content type of the response.");
            var responseBodyOption = new Option<string>("--response-body", "Response body as a string. For JSON, provide a valid JSON string.");
            var headersOption = new Option<string[]>("--header", "Response headers in key=value format (e.g., X-Custom-Header=value).")
                { Arity = ArgumentArity.ZeroOrMore };
            var delayOption = new Option<int>("--delay", () => 0, "Response delay in milliseconds.");
            var definitionFileOption = new Option<string>("--definition-file", "Path to a JSON file containing the complete mock definition (AdvancedMockApiDefinition or MockApiDefinition). If provided, other data options are ignored in non-interactive mode.");
            var nonInteractiveOption = new Option<bool>("--non-interactive", () => false, "Enable non-interactive mode. Requires data via options or --definition-file.");

            AddOption(fileOption);
            AddOption(forceOption);
            AddOption(nameOption);
            AddOption(endpointOption);
            AddOption(methodOption);
            AddOption(statusCodeOption);
            AddOption(contentTypeOption);
            AddOption(responseBodyOption);
            AddOption(headersOption);
            AddOption(delayOption);
            AddOption(definitionFileOption);
            AddOption(nonInteractiveOption);
            
            this.SetHandler(
                 async (context) => {
                    var parseResult = context.ParseResult;
                    await ExecuteAsync(
                        parseResult.GetValueForOption(fileOption)!,
                        parseResult.GetValueForOption(forceOption),
                        parseResult.GetValueForOption(RootCommand.DebugOption),
                        parseResult.GetValueForOption(nonInteractiveOption),
                        parseResult.GetValueForOption(nameOption),
                        parseResult.GetValueForOption(endpointOption),
                        parseResult.GetValueForOption(methodOption),
                        parseResult.GetValueForOption(statusCodeOption),
                        parseResult.GetValueForOption(contentTypeOption),
                        parseResult.GetValueForOption(responseBodyOption),
                        parseResult.GetValueForOption(headersOption),
                        parseResult.GetValueForOption(delayOption),
                        parseResult.GetValueForOption(definitionFileOption)
                    );
                }
            );
        }

        private async Task ExecuteAsync(string filePath, bool force, bool debug, bool nonInteractive,
                                        string? nameNi, string? endpointNi, string? methodNi,
                                        int statusCodeNi, string contentTypeNi, string? responseBodyNi,
                                        string[]? headersNi, int delayNi, string? definitionFileNi)
        {
            if(debug) this.debug = true; // Set class level debug flag
            ConsoleHelper.WriteHeader("Creating New Mock API Definition");
            if (debug) ConsoleHelper.WriteDebug($"Target file path from option: {filePath}, Force: {force}, NonInteractive: {nonInteractive}");

            string processedPath = ProcessFilePath(filePath);
            if (debug) ConsoleHelper.WriteDebug($"Processed file path for output: {processedPath}");

            if (File.Exists(processedPath) && !force && !nonInteractive) // In non-interactive, force is implied if definitionFile or other options are set
            {
                 if (!PromptYesNo($"File '{processedPath}' already exists. Overwrite?"))
                 {
                    ConsoleHelper.WriteWarning("Operation cancelled by user.");
                    return;
                 }
                 force = true; // User agreed to overwrite
            }
             if (nonInteractive && File.Exists(processedPath) && !force && string.IsNullOrWhiteSpace(definitionFileNi) && string.IsNullOrWhiteSpace(nameNi))
            {
                // If non-interactive, file exists, no force, and no definition data provided, then error out.
                ConsoleHelper.WriteError($"File '{processedPath}' already exists. Use --force or provide mock data options to overwrite in non-interactive mode.");
                Environment.Exit(1);
                return;
            }


            EnsureDirectoryExists(Path.GetDirectoryName(processedPath));

            string jsonContent;

            if (nonInteractive)
            {
                if (!string.IsNullOrWhiteSpace(definitionFileNi))
                {
                    if (!File.Exists(definitionFileNi))
                    {
                        ConsoleHelper.WriteError($"Definition file not found: {definitionFileNi}");
                        Environment.Exit(1);
                        return;
                    }
                    if(debug) ConsoleHelper.WriteDebug($"Loading mock definition from file: {definitionFileNi}");
                    jsonContent = await File.ReadAllTextAsync(definitionFileNi);
                    // Validate if it's a valid JSON
                    try {
                        JsonConvert.DeserializeObject(jsonContent); // Test deserialization
                    } catch (JsonException ex) {
                        ConsoleHelper.WriteError($"Invalid JSON in definition file {definitionFileNi}: {ex.Message}");
                        Environment.Exit(1);
                        return;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(nameNi) || string.IsNullOrWhiteSpace(endpointNi) || string.IsNullOrWhiteSpace(methodNi))
                    {
                        ConsoleHelper.WriteError("In non-interactive mode without --definition-file, options --name, --endpoint, and --method are required.");
                        Environment.Exit(1);
                        return;
                    }

                    var mockApi = new MockApiDefinition // Using MockApiDefinition for simplicity here. Advanced features would need more options.
                    {
                        Name = nameNi,
                        Endpoint = endpointNi.StartsWith("/") ? endpointNi : "/" + endpointNi,
                        Method = methodNi.ToUpperInvariant(),
                        StatusCode = statusCodeNi,
                        ContentType = contentTypeNi,
                        Delay = delayNi
                    };

                    if (headersNi != null)
                    {
                        mockApi.Headers = new Dictionary<string, string>();
                        foreach (var h in headersNi)
                        {
                            var parts = h.Split(new[] { '=' }, 2);
                            if (parts.Length == 2) mockApi.Headers[parts[0]] = parts[1];
                            else ConsoleHelper.WriteWarning($"Skipping invalid header format: {h}");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(responseBodyNi))
                    {
                        if (contentTypeNi.Contains("json"))
                        {
                            try { mockApi.Response = JsonConvert.DeserializeObject(responseBodyNi); }
                            catch { ConsoleHelper.WriteWarning("Failed to parse --response-body as JSON, storing as raw string."); mockApi.Response = responseBodyNi; }
                        }
                        else
                        {
                            mockApi.Response = responseBodyNi;
                        }
                    }
                    jsonContent = JsonConvert.SerializeObject(mockApi, Formatting.Indented);
                }
            }
            else // Interactive mode
            {
                MockApiDefinition mockApiInteractive = await GatherMockApiInformation();
                jsonContent = JsonConvert.SerializeObject(mockApiInteractive, Formatting.Indented);
            }

            try
            {
                await File.WriteAllTextAsync(processedPath, jsonContent);
                ConsoleHelper.WriteSuccess($"Mock API definition saved to: {processedPath}");
                ConsoleHelper.WriteInfo($"If you created a basic mock, you can test it with: apify mock-server");
                // To provide a more useful endpoint, we'd need to parse the endpoint from jsonContent if from --definition-file
                // For now, this general message is fine.
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to save mock API definition: {ex.Message}");
                if (nonInteractive) Environment.Exit(1);
            }
        }

        private Task<MockApiDefinition> GatherMockApiInformation() // This remains for interactive mode
        {
            string name = PromptForInput("Mock API name (e.g., Get User):");
            string endpoint = PromptForInput("Endpoint path (e.g., /api/users/:id or /users):");
            if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;
            string method = PromptForHttpMethod();
            int statusCode = PromptForStatusCode();
            string contentType = PromptForContentType();
            object? responseBody = contentType.Contains("json") ? PromptForJsonResponse() : PromptForInput("Response body (plain text):");
            
            Dictionary<string, string>? headers = null;
            if (PromptYesNo("Add custom response headers?"))
            {
                headers = new Dictionary<string, string>();
                ConsoleHelper.WriteInfo("Enter headers (empty name to finish):");
                while (true)
                {
                    string headerName = PromptForInput("Header name:", false);
                    if (string.IsNullOrWhiteSpace(headerName)) break;
                    headers[headerName] = PromptForInput($"Value for {headerName}:");
                }
            }
            
            int delay = 0;
            if (PromptYesNo("Add response delay (ms)?"))
            {
                while (!int.TryParse(PromptForInput("Delay in milliseconds:"), out delay) || delay < 0)
                    ConsoleHelper.WriteWarning("Please enter a valid non-negative number.");
            }
            
            // For simplicity in this refactor, GatherMockApiInformation will create a basic MockApiDefinition.
            // Supporting full AdvancedMockApiDefinition interactively is complex and can be a future enhancement.
            // Conditions are omitted for this interactive path to keep it aligned with simpler non-interactive option path.
            
            return Task.FromResult(new MockApiDefinition
            {
                Name = name, Endpoint = endpoint, Method = method, StatusCode = statusCode,
                ContentType = contentType, Response = responseBody, Headers = headers, Delay = delay,
                Conditions = null // Explicitly null for this basic interactive path
            });
        }

        private string ProcessFilePath(string filePath)
        {
            string processedPath = filePath;
            if (!processedPath.EndsWith(".mock.json", StringComparison.OrdinalIgnoreCase))
            {
                if (processedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) // if user typed .json
                    processedPath = processedPath.Substring(0, processedPath.Length - 5);
                processedPath += ".mock.json";
            }

            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), DefaultApiDirectory, MocksSubDirectory);

            if (!Path.IsPathRooted(processedPath) && !processedPath.StartsWith(DefaultApiDirectory))
            {
                string filenameWithoutExtAndMock = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath)); // original filePath
                 if (filenameWithoutExtAndMock.Contains('.'))
                {
                    string[] parts = filenameWithoutExtAndMock.Split('.');
                    string filename = parts.Last() + ".mock.json"; // ensure .mock.json
                    string subDirs = Path.Combine(parts.Take(parts.Length -1).ToArray());
                    processedPath = Path.Combine(baseDir, subDirs, filename);
                }
                else
                {
                    processedPath = Path.Combine(baseDir, processedPath);
                }
            }
            // Potentially add more logic here if paths like '.apify/customfolder/mymock' are given
            // and we want to ensure they are under '.apify/mocks/customfolder/mymock'
            
            if(debug) ConsoleHelper.WriteDebug($"Path after processing for mock: {processedPath}");
            return processedPath;
        }
        
        private void EnsureDirectoryExists(string? dirPath)
        {
            if (string.IsNullOrEmpty(dirPath)) return;
            if (!Directory.Exists(dirPath))
            {
                try
                {
                    Directory.CreateDirectory(dirPath);
                    ConsoleHelper.WriteInfo($"Created directory: {dirPath}");
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"Failed to create directory '{dirPath}': {ex.Message}");
                }
            }
        }

        private string PromptForInput(string prompt, bool required = true)
        {
            while (true)
            {
                Console.Write($"{prompt} ");
                string? input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (!required) return string.Empty;
                    ConsoleHelper.WriteWarning("This field is required. Please try again.");
                }
                else return input;
            }
        }

        private string PromptForHttpMethod()
        {
            string[] methods = { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };
            return methods[PromptChoice("HTTP method:", methods)];
        }

        private string PromptForContentType()
        {
            string[] contentTypes = { "application/json", "text/plain", "text/html", "application/xml" };
            return contentTypes[PromptChoice("Content Type:", contentTypes)];
        }

        private int PromptForStatusCode()
        {
            string[] statusOptions = { "200 - OK", "201 - Created", "204 - No Content", "400 - Bad Request", "401 - Unauthorized", "403 - Forbidden", "404 - Not Found", "500 - Server Error", "Custom" };
            int[] statusCodes = { 200, 201, 204, 400, 401, 403, 404, 500, 0 };
            int choice = PromptChoice("Status Code:", statusOptions);
            if (statusCodes[choice] == 0) // Custom
            {
                while (true)
                {
                    if (int.TryParse(PromptForInput("Enter custom status code (100-599):"), out int customCode) && customCode >= 100 && customCode <= 599)
                        return customCode;
                    ConsoleHelper.WriteWarning("Invalid status code.");
                }
            }
            return statusCodes[choice];
        }

        private object? PromptForJsonResponse()
        {
            ConsoleHelper.WriteInfo("Enter JSON response (can be multi-line, finish with a blank line):");
            var lines = new List<string>();
            string? line;
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine())) lines.Add(line);
            string jsonInput = string.Join(Environment.NewLine, lines);
            if (string.IsNullOrWhiteSpace(jsonInput)) return null;
            try { return JsonConvert.DeserializeObject(jsonInput); } // Let it be JObject, JArray or simple value
            catch (JsonException) { ConsoleHelper.WriteWarning("Invalid JSON. Storing as raw text."); return jsonInput; }
        }

        private bool PromptYesNo(string prompt)
        {
            while (true)
            {
                Console.Write($"{prompt} (y/n): ");
                string? input = Console.ReadLine()?.Trim().ToLower();
                if (input == "y" || input == "yes") return true;
                if (input == "n" || input == "no") return false;
                ConsoleHelper.WriteWarning("Please enter 'y' or 'n'.");
            }
        }

        private int PromptChoice(string prompt, string[] options)
        {
            Console.WriteLine(prompt);
            for (int i = 0; i < options.Length; i++) Console.WriteLine($"{i + 1}. {options[i]}");
            while (true)
            {
                Console.Write($"Enter selection (1-{options.Length}): ");
                string? input = Console.ReadLine();
                if (int.TryParse(input, out int selection) && selection >= 1 && selection <= options.Length)
                    return selection - 1;
                ConsoleHelper.WriteWarning($"Please enter a number between 1 and {options.Length}.");
            }
        }
        private bool debug = false; // Add a class level debug field
    }
}