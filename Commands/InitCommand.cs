using System.CommandLine;
using Apify.Models;
using Apify.Utils;
using Newtonsoft.Json; // Preferred for TestEnvironmentConfig serialization due to existing use and flexibility

namespace Apify.Commands
{
    public class InitCommand : Command
    {
        private const string DefaultConfigFileName = "apify-config.json";
        private const string DefaultApiDirectoryName = ".apify";
        private const string DefaultSamplesApiDirectoryName = ".apify/apis";
        private const string DefaultSamplesMockDirectoryName = ".apify/mocks";

        public InitCommand() : base("init", "Initialize a new API testing project in the current directory")
        {
            var forceOption = new Option<bool>(
                aliases: new[] { "--force", "-f" },
                getDefaultValue: () => false,
                description: "Force overwrite of existing configuration and sample files."
            );

            var projectNameOption = new Option<string>(
                name: "--project-name",
                description: "The name of the project."
            );

            var baseUrlOption = new Option<string>(
                name: "--base-url",
                description: "The base URL for the default environment."
            );

            var defaultEnvNameOption = new Option<string>(
                name: "--default-env-name",
                getDefaultValue: () => "Development",
                description: "The name for the default environment."
            );

            var createSamplesOption = new Option<bool>(
                name: "--create-samples",
                getDefaultValue: () => true,
                description: "Whether to create sample API and mock files."
            );

            var additionalVariablesOption = new Option<string[]>(
                name: "--additional-variables",
                description: "Additional environment variables for the default environment, in key=value format (e.g., apiKey=yourKey secret=yourSecret)."
            )
            {
                Arity = ArgumentArity.ZeroOrMore
            };

            var nonInteractiveOption = new Option<bool>(
                name: "--non-interactive",
                getDefaultValue: () => false,
                description: "Enable non-interactive mode for CI/automation. Uses defaults if specific options are not provided."
            );

            AddOption(forceOption);
            AddOption(projectNameOption);
            AddOption(baseUrlOption);
            AddOption(defaultEnvNameOption);
            AddOption(createSamplesOption);
            AddOption(additionalVariablesOption);
            AddOption(nonInteractiveOption);

            this.SetHandler(
                (force, projName, bUrl, defEnvName, samples, addVars, nonInter) =>
                    ExecuteAsync(force, projName, bUrl, defEnvName, samples, addVars, nonInter),
                forceOption, projectNameOption, baseUrlOption, defaultEnvNameOption,
                createSamplesOption, additionalVariablesOption, nonInteractiveOption
            );
        }

        private async Task ExecuteAsync(bool force, string? projectName, string? baseUrl, string defaultEnvName,
                                        bool createSamples, string[]? additionalVariablesRaw, bool nonInteractive)
        {
            ConsoleHelper.WriteHeader("Initializing API Testing Project");

            if (nonInteractive)
            {
                force = true; // In non-interactive mode, always behave as if force is true
                projectName ??= "DefaultApifyProject";
                baseUrl ??= "http://localhost:8080";
                ConsoleHelper.WriteInfo($"Non-interactive mode enabled. Project: {projectName}, Base URL: {baseUrl}");
            }

            string configFilePath = Path.Combine(Directory.GetCurrentDirectory(), DefaultConfigFileName);
            
            if (File.Exists(configFilePath) && !force && !nonInteractive)
            {
                if (PromptYesNo($"Configuration file '{DefaultConfigFileName}' already exists. Overwrite?"))
                {
                    force = true;
                }
                else
                {
                    ConsoleHelper.WriteWarning("Initialization canceled by user.");
                    return;
                }
            }

            string apiDirPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultApiDirectoryName);
            if (Directory.Exists(apiDirPath) && !force && !nonInteractive)
            {
                if (PromptYesNo($"Directory '{DefaultApiDirectoryName}' already exists. Overwrite sample files within it?"))
                {
                    force = true; // Allow overwriting samples if directory exists
                }
                // If user says no, we can still proceed to create/overwrite apify-config.json
            }


            try
            {
                Dictionary<string, string> parsedAdditionalVariables = new Dictionary<string, string>();
                if (additionalVariablesRaw != null)
                {
                    foreach (var variablePair in additionalVariablesRaw)
                    {
                        var parts = variablePair.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            parsedAdditionalVariables[parts[0]] = parts[1];
                        }
                        else
                        {
                            ConsoleHelper.WriteWarning($"Skipping invalid variable format: {variablePair}. Use key=value.");
                        }
                    }
                }

                if (!nonInteractive)
                {
                    projectName = PromptForInput("Enter project name:", () => projectName ?? "My Apify Project");
                    defaultEnvName = PromptForInput("Enter default environment name:", () => defaultEnvName ?? "Development");
                    baseUrl = PromptForInput("Enter base URL for API endpoints (e.g., https://api.example.com):", () => baseUrl ?? "https://api.example.com");

                    if (PromptYesNo("Configure additional environment variables for the default environment?"))
                    {
                        ConsoleHelper.WriteInfo("Enter environment variables (empty name to finish):");
                        while (true)
                        {
                            string variableName = PromptForInput("Variable name:", false);
                            if (string.IsNullOrWhiteSpace(variableName)) break;
                            string variableValue = PromptForInput($"Value for {variableName}:");
                            parsedAdditionalVariables[variableName] = variableValue;
                        }
                    }
                }

                // Ensure these have values after potential prompts or defaults
                projectName ??= "DefaultApifyProject";
                baseUrl ??= "http://localhost:8080";
                defaultEnvName ??= "Development";


                // Create API directory and subdirectories if they don't exist
                EnsureDirectoryExists(DefaultApiDirectoryName);
                if (createSamples) {
                    EnsureDirectoryExists(DefaultSamplesApiDirectoryName);
                    EnsureDirectoryExists(DefaultSamplesMockDirectoryName);
                }


                var defaultEnvVariables = new Dictionary<string, string>
                {
                    { "baseUrl", baseUrl },
                    { "timeout", "30000" }
                };
                foreach (var kvp in parsedAdditionalVariables) defaultEnvVariables[kvp.Key] = kvp.Value;

                var defaultEnv = new TestEnvironment
                {
                    Name = defaultEnvName,
                    Description = $"{defaultEnvName} environment",
                    Variables = defaultEnvVariables
                };

                var productionEnvVariables = new Dictionary<string, string>
                {
                    { "baseUrl", baseUrl }, // Typically overridden later
                    { "timeout", "10000" }
                };
                 foreach (var kvp in parsedAdditionalVariables) productionEnvVariables[kvp.Key] = kvp.Value; // Carry over for consistency

                var productionEnvironment = new TestEnvironment
                {
                    Name = "Production",
                    Description = "Production environment",
                    Variables = productionEnvVariables
                };

                List<TestEnvironment> environments = new List<TestEnvironment> { defaultEnv, productionEnvironment };
                
                if (!nonInteractive && PromptYesNo("Add more environments (e.g., Staging, QA)?"))
                {
                    while (true)
                    {
                        string envName = PromptForInput("Environment name (empty to finish):", false);
                        if (string.IsNullOrWhiteSpace(envName)) break;
                        
                        string envDescription = PromptForInput($"Description for {envName}:", () => $"{envName} environment");
                        var currentEnvBaseUrl = PromptForInput($"Base URL for {envName}:", () => baseUrl);
                        var currentEnvVars = new Dictionary<string, string>
                        {
                            { "baseUrl", currentEnvBaseUrl },
                            { "timeout", PromptForInput($"Timeout for {envName} (ms):", () => "20000") }
                        };
                        // Add initially provided additional variables, allowing override
                        foreach (var kvp in parsedAdditionalVariables)
                        {
                           currentEnvVars[kvp.Key] = PromptForInput($"{kvp.Key} for {envName}:", () => kvp.Value);
                        }
                        
                        environments.Add(new TestEnvironment 
                        { 
                            Name = envName, 
                            Description = envDescription,
                            Variables = currentEnvVars
                        });
                    }
                }

                var config = new TestEnvironmentConfig
                {
                    Name = projectName, // Use the project name for the config name
                    Description = $"Configuration for {projectName}",
                    Variables = new Dictionary<string, string> // Project-level variables
                    {
                        { "projectId", $"{projectName.ToLower().Replace(" ", "-")}" },
                        { "globalAdminEmail", "admin@example.com" }
                    },
                    DefaultEnvironment = defaultEnvName,
                    Environments = environments,
                    MockServer = new MockServerConfig // Add default MockServer config
                    {
                        Port = 8080,
                        Directory = DefaultSamplesMockDirectoryName, // Point to the mocks samples dir
                        Verbose = false,
                        EnableCors = true,
                        DefaultHeaders = new Dictionary<string, string>
                        {
                            {"X-Apify-Mock", "true"},
                            {"Access-Control-Allow-Origin", "*"}
                        },
                        FileStoragePath = Path.Combine(DefaultApiDirectoryName, "uploads", "mock-server")
                    }
                };

                if (createSamples)
                {
                    await CreateSampleApiFilesAsync(DefaultSamplesApiDirectoryName);
                    await CreateSampleMockFilesAsync(DefaultSamplesMockDirectoryName);
                }

                string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                await File.WriteAllTextAsync(configFilePath, configJson);
                ConsoleHelper.WriteSuccess($"Created/Updated configuration file: {configFilePath}");
                
                ConsoleHelper.WriteSuccess("\nProject initialized successfully!");
                ConsoleHelper.DisplayQuickStartGuide(configFilePath, DefaultApiDirectoryName, !Environment.CommandLine.Contains("dotnet run"));
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Error initializing project: {ex.Message}");
                if (nonInteractive) Environment.Exit(1); // Exit with error code in CI
            }
        }

        private void EnsureDirectoryExists(string dirPath)
        {
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
                ConsoleHelper.WriteSuccess($"Created directory: {dirPath}");
            }
            else
            {
                ConsoleHelper.WriteInfo($"Directory already exists: {dirPath}");
            }
        }

        private async Task CreateSampleApiFilesAsync(string apiDir)
        {
            var sampleApiTest = new ApiDefinition
            {
                Name = "Sample GET API Test",
                Uri = "{{baseUrl}}/get", // Using a common placeholder from httpbin.org or similar
                Method = "GET",
                Headers = new Dictionary<string, string> { { "Accept", "application/json" } },
                PayloadType = PayloadType.None,
                Tags = new List<string> { "sample", "get" },
                Tests = new List<TestAssertion>
                {
                    new TestAssertion { Name = "Status code is 200", AssertType = "StatusCode", ExpectedValue = "200" },
                    new TestAssertion { Name = "Content-Type is JSON", AssertType = "HeaderContains", Property = "Content-Type", ExpectedValue = "application/json" },
                    new TestAssertion { Name = "Response contains args", AssertType = "ContainsProperty", Property = "args" }
                }
            };
            string sampleApiFilePath = Path.Combine(apiDir, "sample-get-request.json");
            await File.WriteAllTextAsync(sampleApiFilePath, JsonConvert.SerializeObject(sampleApiTest, Formatting.Indented));
            ConsoleHelper.WriteSuccess($"Created sample API test: {sampleApiFilePath}");

            var samplePostTest = new ApiDefinition
            {
                Name = "Sample POST API Test",
                Uri = "{{baseUrl}}/post",
                Method = "POST",
                Headers = new Dictionary<string, string> { { "Accept", "application/json" }, { "Content-Type", "application/json" } },
                Payload = new Dictionary<string, object> { { "sampleKey", "sampleValue" }, { "number", 123 } },
                PayloadType = PayloadType.Json,
                Tags = new List<string> { "sample", "post" },
                Tests = new List<TestAssertion>
                {
                    new TestAssertion { Name = "Status code is 200", AssertType = "StatusCode", ExpectedValue = "200" }, // httpbin.org returns 200 for POST
                    new TestAssertion { Name = "Response contains data", AssertType = "ContainsProperty", Property = "data" },
                    new TestAssertion { Name = "Response JSON matches payload", AssertType = "Equal", Property = "json.sampleKey", ExpectedValue = "sampleValue" }
                }
            };
            string samplePostFilePath = Path.Combine(apiDir, "sample-post-request.json");
            await File.WriteAllTextAsync(samplePostFilePath, JsonConvert.SerializeObject(samplePostTest, Formatting.Indented));
            ConsoleHelper.WriteSuccess($"Created sample POST API test: {samplePostFilePath}");
        }

        private async Task CreateSampleMockFilesAsync(string mockDir)
        {
            var sampleGetUserMock = new AdvancedMockApiDefinition
            {
                Name = "Sample Get User Mock",
                Method = "GET",
                Endpoint = "/api/users/:id",
                Responses = new List<ConditionalResponse>
                {
                    new ConditionalResponse
                    {
                        Condition = "path.id == \"1\"",
                        StatusCode = 200,
                        ContentType = "application/json",
                        Headers = new Dictionary<string, string> { { "X-Mock-Source", "User1-Static" } },
                        ResponseTemplate = new { id = 1, name = "John Doe (Mock)", email = "john.doe@example.mock" }
                    },
                    new ConditionalResponse
                    {
                        Condition = "query.dynamic == \"true\"",
                        StatusCode = 200,
                        ContentType = "application/json",
                        ResponseTemplate = new {
                            id = "{{path.id}}",
                            name = "Dynamic User {{path.id}}",
                            randomValue = "{{$random:int:100:200}}",
                            requestSource = "{{header.User-Agent}}"
                        }
                    },
                    new ConditionalResponse // Default fallback
                    {
                        Condition = "true",
                        StatusCode = 404,
                        ContentType = "application/json",
                        ResponseTemplate = new { error = "User not found", userId = "{{path.id}}" }
                    }
                }
            };
            string sampleUserMockPath = Path.Combine(mockDir, "sample-user.mock.json");
            await File.WriteAllTextAsync(sampleUserMockPath, JsonConvert.SerializeObject(sampleGetUserMock, Formatting.Indented));
            ConsoleHelper.WriteSuccess($"Created sample mock API definition: {sampleUserMockPath}");

             var samplePostMock = new AdvancedMockApiDefinition
            {
                Name = "Sample Post Echo Mock",
                Method = "POST",
                Endpoint = "/api/echo",
                Responses = new List<ConditionalResponse>
                {
                    new ConditionalResponse
                    {
                        Condition = "body.message != null",
                        StatusCode = 201,
                        ContentType = "application/json",
                        ResponseTemplate = new {
                            receivedMessage = "{{body.message}}",
                            originalPayload = "{{body}}",
                            timestamp = "{{datetime}}"
                        }
                    },
                     new ConditionalResponse
                    {
                        Condition = "true", // Default if body.message is not present
                        StatusCode = 400,
                        ContentType = "application/json",
                        ResponseTemplate = new { error = "Missing 'message' in request body" }
                    }
                }
            };
            string samplePostMockPath = Path.Combine(mockDir, "sample-echo.mock.json");
            await File.WriteAllTextAsync(samplePostMockPath, JsonConvert.SerializeObject(samplePostMock, Formatting.Indented));
            ConsoleHelper.WriteSuccess($"Created sample echo mock API definition: {samplePostMockPath}");
            ConsoleHelper.WriteInfo($"To start the mock server (if samples created): apify mock-server");
        }


        private string PromptForInput(string prompt, bool required = true)
        {
            while (true)
            {
                string? input = ConsoleHelper.PromptInput(prompt);
                if (string.IsNullOrWhiteSpace(input))
                {
                    if (!required) return string.Empty;
                    ConsoleHelper.WriteWarning("This field is required. Please try again.");
                }
                else
                {
                    return input;
                }
            }
        }

        private string PromptForInput(string prompt, Func<string> defaultValueProvider, bool required = true)
        {
            string defaultValue = defaultValueProvider();
            string input = ConsoleHelper.PromptInput(prompt, defaultValue);
            if (string.IsNullOrWhiteSpace(input))
            {
                if (!required) return string.Empty;
                return defaultValue; // Return default if input is empty and required
            }
            return input;
        }

        private bool PromptYesNo(string prompt)
        {
            while (true)
            {
                string? input = ConsoleHelper.PromptInput($"{prompt} (y/n)").Trim().ToLower();
                if (input.StartsWith("y")) return true;
                if (input.StartsWith("n")) return false;
                ConsoleHelper.WriteWarning("Please enter 'y' or 'n'.");
            }
        }
        // PromptChoice is not used in this version of InitCommand, can be removed or kept for future use.
    }
}