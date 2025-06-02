using System.CommandLine;
using Apify.Models;
using Apify.Utils;
using Newtonsoft.Json; // Using Newtonsoft.Json for consistency if it's used elsewhere for ApiDefinition

namespace Apify.Commands
{
    public class CreateRequestCommand : Command
    {
        private const string DefaultApiDirectory = ".apify";
        private const string ApisSubDirectory = "apis"; // Subdirectory for API requests

        public CreateRequestCommand() : base("create", "Create a new API request file or mock response definition.")
        {
            var requestCommand = new Command("request", "Create a new API request definition file.");

            var fileOption = new Option<string>(
                aliases: new[] { "--file", "-f" },
                description: "The file path for the new API request definition (e.g., users.getUser). '.json' extension is added automatically. Stored under '.apify/apis/'."
            ) { IsRequired = true };

            var forceOption = new Option<bool>(
                aliases: new[] { "--force", "-o" }, // o for overwrite
                getDefaultValue: () => false,
                description: "Force overwrite if the file already exists."
            );

            // Options for non-interactive mode
            var nameOption = new Option<string>("--name", "API request name.");
            var methodOption = new Option<string>("--method", "HTTP method (e.g., GET, POST).");
            var uriOption = new Option<string>("--uri", "Request URI (can include {{variables}}).");

            var headersOption = new Option<string[]>(
                "--header",
                "Request headers in key=value format (e.g., X-Api-Key=mykey Content-Type=application/json)."
            ) { Arity = ArgumentArity.ZeroOrMore };

            var payloadJsonOption = new Option<string>("--payload-json", "JSON payload as a string for the request body.");
            var payloadTextOption = new Option<string>("--payload-text", "Text payload as a string for the request body.");
            var payloadFormOption = new Option<string[]>(
                "--payload-form",
                "Form data payload in key=value format (e.g., username=test password=secret)."
            ) { Arity = ArgumentArity.ZeroOrMore };

            var fileUploadsOption = new Option<string[]>(
                "--file-upload",
                "File uploads in fieldName:filePath:contentType format (e.g., avatar:./img.png:image/png)."
            ) { Arity = ArgumentArity.ZeroOrMore };

            var testsOption = new Option<string[]>(
                "--test",
                "Test assertions in Name:AssertType:Property:ExpectedValue format (e.g., \"Status Is 200:StatusCode::200\" \"Body Has ID:ContainsProperty:id:\"). Property can be empty."
            ) { Arity = ArgumentArity.ZeroOrMore };

            var nonInteractiveOption = new Option<bool>(
                "--non-interactive",
                getDefaultValue: () => false,
                description: "Enable non-interactive mode. Requires all necessary data to be provided via options."
            );

            requestCommand.AddOption(fileOption);
            requestCommand.AddOption(forceOption);
            requestCommand.AddOption(nameOption);
            requestCommand.AddOption(methodOption);
            requestCommand.AddOption(uriOption);
            requestCommand.AddOption(headersOption);
            requestCommand.AddOption(payloadJsonOption);
            requestCommand.AddOption(payloadTextOption);
            requestCommand.AddOption(payloadFormOption);
            requestCommand.AddOption(fileUploadsOption);
            requestCommand.AddOption(testsOption);
            requestCommand.AddOption(nonInteractiveOption);
            
            requestCommand.SetHandler(
                async (context) => {
                    var parseResult = context.ParseResult;
                    await ExecuteRequestAsync(
                        parseResult.GetValueForOption(fileOption)!,
                        parseResult.GetValueForOption(forceOption),
                        parseResult.GetValueForOption(RootCommand.DebugOption),
                        parseResult.GetValueForOption(nonInteractiveOption),
                        parseResult.GetValueForOption(nameOption),
                        parseResult.GetValueForOption(methodOption),
                        parseResult.GetValueForOption(uriOption),
                        parseResult.GetValueForOption(headersOption),
                        parseResult.GetValueForOption(payloadJsonOption),
                        parseResult.GetValueForOption(payloadTextOption),
                        parseResult.GetValueForOption(payloadFormOption),
                        parseResult.GetValueForOption(fileUploadsOption),
                        parseResult.GetValueForOption(testsOption)
                    );
                }
            );

            var mockCommand = new CreateMockCommand(); // Assuming CreateMockCommand exists and is set up similarly

            AddCommand(requestCommand);
            AddCommand(mockCommand);
        }

        private async Task ExecuteRequestAsync(
            string filePath, bool force, bool debug, bool nonInteractive,
            string? nameNonInteractive, string? methodNonInteractive, string? uriNonInteractive,
            string[]? headersNonInteractive, string? payloadJsonNonInteractive, string? payloadTextNonInteractive,
            string[]? payloadFormNonInteractive, string[]? fileUploadsNonInteractive, string[]? testsNonInteractive)
        {
            ConsoleHelper.WriteHeader("Creating New API Request Definition");

            if (debug) ConsoleHelper.WriteDebug($"File path from option: {filePath}, Force: {force}, NonInteractive: {nonInteractive}");

            string name, method, uri;
            var headers = new Dictionary<string, string>();
            PayloadType payloadType = PayloadType.None;
            object? payload = null;
            var files = new List<FileUpload>();
            var tests = new List<TestAssertion>();

            if (nonInteractive)
            {
                if (string.IsNullOrWhiteSpace(nameNonInteractive) ||
                    string.IsNullOrWhiteSpace(methodNonInteractive) ||
                    string.IsNullOrWhiteSpace(uriNonInteractive))
                {
                    ConsoleHelper.WriteError("In non-interactive mode, --name, --method, and --uri options are required.");
                    Environment.Exit(1); // Exit with error for CI
                    return;
                }
                name = nameNonInteractive;
                method = methodNonInteractive.ToUpperInvariant();
                uri = uriNonInteractive;

                if (headersNonInteractive != null)
                {
                    foreach (var h in headersNonInteractive)
                    {
                        var parts = h.Split(new[] { '=' }, 2);
                        if (parts.Length == 2) headers[parts[0]] = parts[1];
                        else ConsoleHelper.WriteWarning($"Skipping invalid header format: {h}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(payloadJsonNonInteractive))
                {
                    payloadType = PayloadType.Json;
                    try { payload = JsonConvert.DeserializeObject(payloadJsonNonInteractive); }
                    catch { ConsoleHelper.WriteWarning("Failed to parse --payload-json, storing as raw string."); payload = payloadJsonNonInteractive; }
                }
                else if (!string.IsNullOrWhiteSpace(payloadTextNonInteractive))
                {
                    payloadType = PayloadType.Text;
                    payload = payloadTextNonInteractive;
                }
                else if (payloadFormNonInteractive != null && payloadFormNonInteractive.Length > 0)
                {
                    payloadType = PayloadType.FormData;
                    var formData = new Dictionary<string, string>();
                    foreach (var item in payloadFormNonInteractive)
                    {
                        var parts = item.Split(new[] { '=' }, 2);
                        if (parts.Length == 2) formData[parts[0]] = parts[1];
                        else ConsoleHelper.WriteWarning($"Skipping invalid form data format: {item}");
                    }
                    payload = formData;
                }
                
                if (fileUploadsNonInteractive != null)
                {
                    foreach(var fu in fileUploadsNonInteractive)
                    {
                        var parts = fu.Split(':');
                        if (parts.Length == 3)
                        {
                            files.Add(new FileUpload { FieldName = parts[0], FilePath = parts[1], ContentType = parts[2], Name = Path.GetFileName(parts[1])});
                        } else ConsoleHelper.WriteWarning($"Skipping invalid file upload format: {fu}");
                    }
                }

                if (testsNonInteractive != null)
                {
                    foreach (var t in testsNonInteractive)
                    {
                        var parts = t.Split(new[] { ':' }, 4); // Name:AssertType:Property:ExpectedValue
                        if (parts.Length >= 2) // Minimum Name and AssertType
                        {
                            tests.Add(new TestAssertion {
                                Name = parts[0],
                                AssertType = parts[1],
                                Property = parts.Length > 2 ? parts[2] : string.Empty,
                                ExpectedValue = parts.Length > 3 ? parts[3] : string.Empty
                            });
                        } else ConsoleHelper.WriteWarning($"Skipping invalid test format: {t}");
                    }
                }
            }
            else // Interactive mode
            {
                name = PromptForInput("API request name (e.g., Get User):");
                method = PromptForHttpMethod();
                uri = PromptForInput("URI (e.g., {{baseUrl}}/users/{{userId}}):");

                if (PromptYesNo("Add request headers?"))
                {
                    ConsoleHelper.WriteInfo("Enter headers (empty name to finish):");
                    while (true)
                    {
                        string headerName = PromptForInput("Header name (e.g., Content-Type):", false);
                        if (string.IsNullOrWhiteSpace(headerName)) break;
                        headers[headerName] = PromptForInput($"Value for {headerName}:");
                    }
                }

                bool needsPayload = method == "POST" || method == "PUT" || method == "PATCH";
                if (needsPayload && PromptYesNo("Add request payload?"))
                {
                    string[] payloadOptions = { "JSON", "Text", "FormData" };
                    int choice = PromptChoice("Payload type:", payloadOptions);
                    switch (choice)
                    {
                        case 0:
                            payloadType = PayloadType.Json;
                            string jsonStr = PromptForInput("Enter JSON payload:");
                            try { payload = JsonConvert.DeserializeObject(jsonStr); }
                            catch { ConsoleHelper.WriteWarning("Invalid JSON, storing as raw string."); payload = jsonStr; }
                            break;
                        case 1:
                            payloadType = PayloadType.Text;
                            payload = PromptForInput("Enter text payload:");
                            break;
                        case 2:
                            payloadType = PayloadType.FormData;
                            var formData = new Dictionary<string, string>();
                            ConsoleHelper.WriteInfo("Enter form fields (empty name to finish):");
                            while (true)
                            {
                                string fieldName = PromptForInput("Field name:", false);
                                if (string.IsNullOrWhiteSpace(fieldName)) break;
                                formData[fieldName] = PromptForInput($"Value for {fieldName}:");
                            }
                            payload = formData;
                            break;
                    }
                }
                
                if (needsPayload && (payloadType == PayloadType.FormData || files.Count > 0 || PromptYesNo("Add file uploads (common with FormData)?")))
                {
                     ConsoleHelper.WriteInfo("Enter file uploads (empty field name to finish):");
                    while (true)
                    {
                        string fieldName = PromptForInput("Field name for the file:", false);
                        if (string.IsNullOrWhiteSpace(fieldName)) break;
                        files.Add(new FileUpload {
                            FieldName = fieldName,
                            FilePath = PromptForInput("Path to file:"),
                            ContentType = PromptForInput("Content type (e.g., image/jpeg):"),
                            Name = Path.GetFileName(PromptForInput("Descriptive name for file (optional):",false)) // Name is optional here
                        });
                    }
                }


                if (PromptYesNo("Add tests?"))
                {
                    ConsoleHelper.WriteInfo("Enter tests (empty name to finish):");
                    while (true)
                    {
                        string testName = PromptForInput("Test name:", false);
                        if (string.IsNullOrWhiteSpace(testName)) break;
                        string[] assertionTypes = { "StatusCode", "ContainsProperty", "HeaderContains", "ResponseTimeBelow", "Equal", "IsArray", "ArrayNotEmpty" };
                        string assertionType = assertionTypes[PromptChoice("Assertion type:", assertionTypes)];
                        string property = string.Empty;
                        if (new[]{"ContainsProperty", "HeaderContains", "Equal", "IsArray", "ArrayNotEmpty"}.Contains(assertionType))
                        {
                            property = PromptForInput("Property (JSONPath or Header name):");
                        }
                        string expectedValue = string.Empty;
                         if (new[]{"StatusCode", "HeaderContains", "ResponseTimeBelow", "Equal"}.Contains(assertionType))
                        {
                           expectedValue = PromptForInput("Expected value:");
                        }
                        tests.Add(new TestAssertion { Name = testName, AssertType = assertionType, Property = property, ExpectedValue = expectedValue });
                    }
                }
            }

            string processedPath = ProcessRequestFilePath(filePath);
            if (File.Exists(processedPath) && !force)
            {
                ConsoleHelper.WriteError($"File already exists: {processedPath}. Use --force or -o to overwrite.");
                if (nonInteractive) Environment.Exit(1);
                return;
            }

            EnsureDirectoryExists(Path.GetDirectoryName(processedPath));

            var apiDefinition = new ApiDefinition
            {
                Name = name, Uri = uri, Method = method,
                Headers = headers.Count > 0 ? headers : null,
                PayloadType = payloadType, Payload = payload,
                Files = files.Count > 0 ? files : null,
                Tests = tests.Count > 0 ? tests : null
            };

            try
            {
                string jsonContent = JsonConvert.SerializeObject(apiDefinition, Formatting.Indented);
                await File.WriteAllTextAsync(processedPath, jsonContent);
                ConsoleHelper.WriteSuccess($"API request definition saved to: {processedPath}");
                ConsoleHelper.WriteInfo($"You can run it with: apify run {filePath.Replace(".json", "")}");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to save API request: {ex.Message}");
                if (nonInteractive) Environment.Exit(1);
            }
        }

        private string ProcessRequestFilePath(string filePath)
        {
            string processedPath = filePath;
            if (!processedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                processedPath += ".json";
            }

            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), DefaultApiDirectory, ApisSubDirectory);
            
            // If filePath is just a filename or dot notation without explicit base directory
            if (!Path.IsPathRooted(processedPath) && !processedPath.StartsWith(DefaultApiDirectory))
            {
                 // Convert dot notation to directory structure relative to '.apify/apis/'
                string filenameWithoutExt = Path.GetFileNameWithoutExtension(filePath); // use original filePath for dot splitting
                if (filenameWithoutExt.Contains('.'))
                {
                    string[] parts = filenameWithoutExt.Split('.');
                    string filename = parts.Last() + ".json";
                    string subDirs = Path.Combine(parts.Take(parts.Length -1).ToArray());
                    processedPath = Path.Combine(baseDir, subDirs, filename);
                }
                else
                {
                    processedPath = Path.Combine(baseDir, processedPath);
                }
            }
            // If it already somehow includes .apify but not apis
            else if (processedPath.Contains(DefaultApiDirectory) && !processedPath.Contains(Path.Combine(DefaultApiDirectory, ApisSubDirectory)))
            {
                 // This case is tricky, might indicate user trying to save outside 'apis'.
                 // For simplicity, we'll assume if .apify is in path, it's intentional, but ideally guide to 'apis'.
                 // For now, let it pass, or enforce it to be under 'apis'.
                 // To enforce: if (!processedPath.StartsWith(Path.Combine(Directory.GetCurrentDirectory(), DefaultApiDirectory, ApisSubDirectory)))
                 // then processedPath = Path.Combine(baseDir, Path.GetFileName(processedPath));
            }
             // else, path is absolute or already correctly relative to project root including .apify/apis

            if (debug) ConsoleHelper.WriteDebug($"Processed file path: {processedPath}");
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
                    // Potentially exit in non-interactive if directory creation is critical
                }
            }
        }

        // Keep existing prompt methods, they are used for interactive mode
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
                {
                    return selection - 1;
                }
                ConsoleHelper.WriteWarning($"Please enter a number between 1 and {options.Length}.");
            }
        }
        private bool debug = false; // Add a class level debug field
    }
}