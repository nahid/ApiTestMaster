using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Apify.Models;
using Apify.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Dynamic;

namespace Apify.Services
{
    public class MockServerService
    {
        private readonly string _mockDirectory;
        private readonly List<MockSchema> _mockSchemaDefinitions = new();
        private readonly ConfigService _configService;
        private readonly ConditionEvaluator _conditionEvaluator = new();
        private bool _verbose;
        private bool _debug;
        private HttpListener? _listener;
        private bool _isRunning;
        
        public MockServerService(string mockDirectory, bool debug = false)
        {
            _mockDirectory = mockDirectory;
            _debug = debug;
            _configService = new ConfigService(debug);
           
        }
        
        public async Task StartAsync(int port, bool verbose)
        {
            _verbose = verbose;

            if (port == 0)
            {
                port = _configService.LoadConfiguration()?.MockServer?.Port ?? 8080;
            }
            
            // Load all mock definitions
            LoadMockSchemaDefinitions();
            
            if (_mockSchemaDefinitions.Count == 0)
            {
                ConsoleHelper.WriteWarning("No mock API definitions found. Create .mock.json files in your .apify directory.");
                ConsoleHelper.WriteInfo("Example path: .apify/users/all.mock.json");
                return;
            }
            
            // Start HTTP listener
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{port}/");
            
            try
            {
                _listener.Start();
                _isRunning = true;
                
                ConsoleHelper.WriteSuccess($"Mock API Server started on http://0.0.0.0:{port}");
                ConsoleHelper.WriteInfo("Available endpoints:");
                
                // Display advanced mock endpoints
                foreach (var mock in _mockSchemaDefinitions)
                {
                    ConsoleHelper.WriteSuccess($"[{mock.Method}] {mock.Endpoint} - {mock.Name} (Advanced)");
                }
                
                Console.WriteLine("\nPress Ctrl+C to stop the server...");
                
                // Handle requests
                while (_isRunning)
                {
                    var context = await _listener.GetContextAsync();
                    _ = ProcessRequestAsync(context);
                }
            }
            catch (HttpListenerException ex) when (ex.Message.Contains("Access is denied") || ex.ErrorCode == 5)
            {
                ConsoleHelper.WriteError($"Error starting mock server: {ex.Message}");
                
                // Provide helpful guidance for Windows users
                if (OperatingSystem.IsWindows())
                {
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo("This error commonly occurs on Windows when binding to HTTP ports without administrator privileges.");
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo("To resolve this issue, you can:");
                    Console.WriteLine("1. Run your command prompt as Administrator");
                    Console.WriteLine("   Right-click on cmd/PowerShell and select 'Run as administrator'");
                    Console.WriteLine();
                    Console.WriteLine("2. Add a URL reservation (one-time setup, preferred solution):");
                    Console.WriteLine($"   Run this in an Administrator PowerShell: netsh http add urlacl url=http://+:{port}/ user=Everyone");
                    Console.WriteLine();
                    Console.WriteLine("3. Try using a port number above 1024 (e.g., 8080):");
                    Console.WriteLine($"   dotnet run mock-server --port 8080");
                    Console.WriteLine();
                }
                
                if (_debug)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Error starting mock server: {ex.Message}");
                if (_debug)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
        }
        
        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
        }
        
        private void LoadMockSchemaDefinitions()
        {
            if (!Directory.Exists(_mockDirectory))
            {
                Directory.CreateDirectory(_mockDirectory);
                ConsoleHelper.WriteInfo($"Created mock API directory: {_mockDirectory}");
                return;
            }
            
            // Find all .mock.json files in the directory and subdirectories
            var mockFiles = Directory.GetFiles(_mockDirectory, "*.mock.json", SearchOption.AllDirectories);
            
            // Use debug flag for detailed logs, but show count regardless
            Console.WriteLine($"Found {mockFiles.Length} mock API definition files");
            
            foreach (var file in mockFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);

                    if (_debug)
                    {
                        ConsoleHelper.WriteInfo($"Attempting to load mock from: {file}");
                    }

                    // First try to parse as advanced mock definition
                    MockSchema mockDef;
                    
                    mockDef = JsonConvert.DeserializeObject<MockSchema>(json) ??
                                      new MockSchema();

                    if (_debug)
                    {
                        ConsoleHelper.WriteInfo($"Successfully parsed {file} as MockSchema");
                    }

                    if (mockDef != null && mockDef.Responses != null &&
                        mockDef.Responses.Count > 0)
                    {
                        _mockSchemaDefinitions.Add(mockDef);

                        // Always show loaded API info
                        ConsoleHelper.WriteInfo(
                            $"Loaded advanced mock API: {mockDef.Name} [{mockDef.Method}] {mockDef.Endpoint}");
                    }

                }
                catch (FileLoadException ex)
                {
                    ConsoleHelper.WriteError($"Error loading mock definition from {file}: {ex.Message}");

                    if (_debug)
                    {
                        Console.WriteLine(ex.StackTrace);
                    }
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"Error parsing {file} as MockSchema: {ex.Message}");
                    if (_debug)
                    {
                        Console.WriteLine(ex.StackTrace);
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        }
                    }
                }
            }
        }
        
        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            
            string requestUrl = request.Url?.AbsolutePath ?? string.Empty;
            string method = request.HttpMethod;
            
            Console.WriteLine($"Received request: {method} {requestUrl}");
                
            if (_debug)
            {
                foreach (var headerKey in request.Headers.AllKeys)
                {
                    ConsoleHelper.WriteInfo($"  Header: {headerKey}: {request.Headers[headerKey]}");
                }
            }
            
            // Look for advanced mock definition first
            var mockDefinition = FindMatchingMockDefinition(request);
            Dictionary<string, string> pathParams = new Dictionary<string, string>();
            
            if (mockDefinition != null)
            {
                await ProcessMockResponseAsync(context, mockDefinition, pathParams);
                return;
            }
 
            // No matching mock definition found
            response.StatusCode = 404;
            response.ContentType = "application/json";
            
            // Combine both legacy and advanced mock endpoints in the error message
            var availableEndpoints = new List<object>();
            
            availableEndpoints.AddRange(_mockSchemaDefinitions.Select(m => new { 
                method = m.Method, 
                endpoint = m.Endpoint,
                name = m.Name,
                type = "advanced"
            }));
            
            string notFoundResponse = JsonConvert.SerializeObject(new
            {
                error = "Not Found",
                message = $"No mock defined for {method} {requestUrl}",
                availableEndpoints = availableEndpoints
            }, Formatting.Indented);
            
            byte[] buffer = Encoding.UTF8.GetBytes(notFoundResponse);
            response.ContentLength64 = buffer.Length;
            
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            ConsoleHelper.WriteWarning($"No mock found for {method} {requestUrl}");
            
            response.Close();
        
        }
        
        private MockCondition? FindMatchingCondition(MockApiDefinition mockDef, HttpListenerRequest request)
        {
            if (mockDef.Conditions == null || mockDef.Conditions.Count == 0)
                return null;
                
            foreach (var condition in mockDef.Conditions)
            {
                bool matches = true;
                
                // Check query parameters
                if (condition.QueryParams != null && condition.QueryParams.Count > 0)
                {
                    foreach (var param in condition.QueryParams)
                    {
                        if (request.QueryString[param.Key] != param.Value)
                        {
                            matches = false;
                            break;
                        }
                    }
                    
                    if (!matches) continue;
                }
                
                // Check for exact header matches
                if (condition.Headers != null && condition.Headers.Count > 0)
                {
                    foreach (var header in condition.Headers)
                    {
                        if (request.Headers[header.Key] != header.Value)
                        {
                            matches = false;
                            break;
                        }
                    }
                    
                    if (!matches) continue;
                }
                
                // Check for header value contains 
                if (condition.HeadersContain != null && condition.HeadersContain.Count > 0)
                {
                    foreach (var header in condition.HeadersContain)
                    {
                        string? headerValue = request.Headers[header.Key];
                        if (string.IsNullOrEmpty(headerValue) || !headerValue.Contains(header.Value))
                        {
                            matches = false;
                            break;
                        }
                    }
                    
                    if (!matches) continue;
                }
                
                // Check if specified headers exist
                if (condition.HeaderExists != null && condition.HeaderExists.Count > 0)
                {
                    foreach (var headerName in condition.HeaderExists)
                    {
                        if (string.IsNullOrEmpty(request.Headers[headerName]))
                        {
                            matches = false;
                            break;
                        }
                    }
                    
                    if (!matches) continue;
                }
                
                // Check for body content if applicable
                if (condition.Body != null || condition.BodyContains != null || condition.BodyMatches != null)
                {
                    // Try to read request body (if it has one)
                    string requestBody = string.Empty;
                    
                    try
                    {
                        if (request.ContentLength64 > 0 && request.HasEntityBody)
                        {
                            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                requestBody = reader.ReadToEnd();
                            }
                            
                            // Reset the stream position for further reads
                            request.InputStream.Position = 0;
                        }
                        
                        // Check for exact body match
                        if (condition.Body != null)
                        {
                            string expectedBody = condition.Body is string strBody 
                                ? strBody 
                                : JsonConvert.SerializeObject(condition.Body);
                                
                            if (!string.Equals(requestBody, expectedBody, StringComparison.OrdinalIgnoreCase))
                            {
                                matches = false;
                            }
                            
                            if (!matches) continue;
                        }
                        
                        // Check if body contains specific strings
                        if (condition.BodyContains != null && condition.BodyContains.Count > 0)
                        {
                            foreach (var fragment in condition.BodyContains)
                            {
                                if (!requestBody.Contains(fragment))
                                {
                                    matches = false;
                                    break;
                                }
                            }
                            
                            if (!matches) continue;
                        }
                        
                        // Check if body matches specific patterns (property values in JSON)
                        if (condition.BodyMatches != null && condition.BodyMatches.Count > 0 &&
                            !string.IsNullOrEmpty(requestBody) && IsJsonObject(requestBody))
                        {
                            try
                            {
                                var bodyObject = JsonConvert.DeserializeObject<JObject>(requestBody);
                                
                                foreach (var match in condition.BodyMatches)
                                {
                                    // Use JPath-like expressions to find values
                                    var token = bodyObject?.SelectToken(match.Key);
                                    
                                    if (token == null || !token.ToString().Equals(match.Value))
                                    {
                                        matches = false;
                                        break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (_debug)
                                {
                                    ConsoleHelper.WriteWarning($"Failed to parse JSON body for matching: {ex.Message}");
                                }
                                matches = false;
                            }
                            
                            if (!matches) continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_debug)
                        {
                            ConsoleHelper.WriteWarning($"Error reading request body: {ex.Message}");
                        }
                        matches = false;
                        continue;
                    }
                }
                
                if (matches)
                    return condition;
            }
            
            return null;
        }
        
        private bool IsJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
                
            text = text.Trim();
            return text.StartsWith("{") && text.EndsWith("}");
        }
        
        private MockSchema? FindMatchingMockDefinition(HttpListenerRequest request)
        {
            string requestUrl = request.Url?.AbsolutePath ?? string.Empty;
            string method = request.HttpMethod;
            
            // First try exact match
            var exactMatch = _mockSchemaDefinitions.FirstOrDefault(m => 
                string.Equals(m.Endpoint, requestUrl, StringComparison.OrdinalIgnoreCase) && 
                string.Equals(m.Method, method, StringComparison.OrdinalIgnoreCase));
                
            if (exactMatch != null)
                return exactMatch;
                
            // Try wildcard/pattern match (for path parameters like /users/{id})
            foreach (var mockDef in _mockSchemaDefinitions)
            {
                if (!string.Equals(mockDef.Method, method, StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                // Convert endpoint pattern to regex
                // Example: "/users/{id}" becomes "^/users/[^/]+$"
                if (mockDef.Endpoint.Contains("{") && mockDef.Endpoint.Contains("}"))
                {
                    string pattern = "^" + Regex.Escape(mockDef.Endpoint)
                        .Replace("\\{", "{")
                        .Replace("\\}", "}")
                        .Replace("{[^/]+}", "[^/]+") + "$";
                        
                    // Convert parameters like {id} or {name} to regex capture groups
                    pattern = Regex.Replace(pattern, "{([^/]+)}", "([^/]+)");
                    
                    if (Regex.IsMatch(requestUrl, pattern))
                    {
                        return mockDef;
                    }
                }
            }
            
            return null;
        }
        
        private async Task ProcessMockResponseAsync(HttpListenerContext context, MockSchema mockDef, Dictionary<string, string> pathParams)
        {
            var request = context.Request;
            var response = context.Response;
            string requestUrl = request.Url?.AbsolutePath ?? string.Empty;
            string method = request.HttpMethod;
            
            // Extract path parameters for use in templates
            ExtractPathParameters(mockDef.Endpoint, requestUrl, out pathParams);
            
            // Get Headers
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (request.Headers.AllKeys != null)
            {
                foreach (string? key in request.Headers.AllKeys)
                {
                    if (!string.IsNullOrEmpty(key) && request.Headers[key] != null)
                    {
                        string headerValue = request.Headers[key] ?? string.Empty;
                        headers[key] = headerValue;
                    }
                }
            }
            
            // Get Query Parameters
            Dictionary<string, string> queryParams = new Dictionary<string, string>();
            if (request.Url?.Query != null && request.Url.Query.Length > 0)
            {
                // Process the query string
                
                // Process query string manually to ensure correct handling
                string query = request.Url.Query.TrimStart('?');
                string[] pairs = query.Split('&');
                
                foreach (string pair in pairs)
                {
                    string[] keyValue = pair.Split('=');
                    if (keyValue.Length >= 2)
                    {
                        string key = keyValue[0];
                        string value = keyValue[1];
                        queryParams[key] = Uri.UnescapeDataString(value);

                    }
                }
            }
            
            // Get Body as JToken (null if not a valid JSON)
            JToken? bodyContent = null;
            string bodyString = string.Empty;
            
            if (request.HasEntityBody && request.ContentLength64 > 0)
            {
                try
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        request.InputStream.CopyTo(memoryStream);
                        memoryStream.Position = 0; // rewind for reading

                        using (var reader = new StreamReader(memoryStream, request.ContentEncoding, leaveOpen: true))
                        {
                            bodyString = reader.ReadToEnd();
                        }

                        memoryStream.Position = 0; // rewind again if needed later

                        if (IsJsonObject(bodyString) || bodyString.Trim().StartsWith("["))
                        {
                            bodyContent = JsonConvert.DeserializeObject<JToken>(bodyString);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error reading JSON body: {e.Message}");
                    bodyContent = JToken.Parse("{}");
                }
            }
            else
            {
                bodyContent = JToken.Parse("{}");
            }
            
            // First process the responses in two groups: defaults and non-defaults
            var defaultResponses = new List<ConditionalResponse>();
            var regularResponses = new List<ConditionalResponse>();
            
            // Sort the responses into appropriate lists
            foreach (var resp in mockDef.Responses)
            {
                if (_conditionEvaluator.IsDefaultCondition(resp.Condition))
                {
                    defaultResponses.Add(resp);
                }
                else
                {
                    regularResponses.Add(resp);
                }
            }
            
            if (_debug)
            {
                Console.WriteLine($"DEBUG: Found {regularResponses.Count} regular condition responses and {defaultResponses.Count} default responses.");
            }
            
            // First try to match any regular (non-default) condition
            ConditionalResponse? matchedResponse = null;
            
            foreach (var resp in regularResponses)
            {
                bool conditionMet = _conditionEvaluator.EvaluateCondition(
                    resp.Condition, 
                    headers, 
                    bodyContent ?? JToken.Parse("{}"), 
                    queryParams,
                    pathParams);
                    
                if (conditionMet)
                {
                    matchedResponse = resp;
                    if (_debug)
                    {
                        Console.WriteLine($"DEBUG: Matched condition: '{resp.Condition}'");
                    }
                    break;
                }
            }
            
            // If no regular condition matched, use the first default response if available
            if (matchedResponse == null)
            {
                // Debug - log all available conditions
                if (_debug)
                {
                    Console.WriteLine($"DEBUG: No regular conditions matched. Looking for default response.");
                }

                if (defaultResponses.Count > 0)
                {
                    matchedResponse = defaultResponses[0]; // Take the first default response
                    
                    // Write additional debug info
                    if (_debug)
                    {
                        Console.WriteLine($"DEBUG: Using default response with condition '{matchedResponse.Condition ?? "null"}'");
                    }
                }
                else
                {
                    // If no explicit default, use the last response as fallback
                    matchedResponse = mockDef.Responses.LastOrDefault();
                    if (matchedResponse != null && _debug)
                    {
                        Console.WriteLine($"DEBUG: No explicit default found, using last response with condition '{matchedResponse.Condition ?? "null"}' as fallback");
                    }
                }
            }
            
            if (matchedResponse == null)
            {
                ConsoleHelper.WriteWarning($"No matching response found for {method} {requestUrl} and no default response.");
                
                // Return 500 error if no matching response
                response.StatusCode = 500;
                response.ContentType = "application/json";
                
                string errorContent = JsonConvert.SerializeObject(new
                {
                    error = "No Response Match",
                    message = "No condition matched the request and no default response was defined"
                }, Formatting.Indented);
                
                byte[] errorBuffer = Encoding.UTF8.GetBytes(errorContent);
                response.ContentLength64 = errorBuffer.Length;
                await response.OutputStream.WriteAsync(errorBuffer);
                response.Close();
                return;
            }
            
            // Process the matched response
            response.StatusCode = matchedResponse.StatusCode;
            response.ContentType = "application/json"; // Default

            if (_configService?.LoadConfiguration().MockServer?.DefaultHeaders != null)
            {
                foreach (var header in _configService?.LoadConfiguration().MockServer?.DefaultHeaders ?? new Dictionary<string, string>())
                {
                    response.Headers.Add(header.Key, header.Value);
                    
                    // Set content type if it's in the headers
                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        response.ContentType = header.Value;
                    }
                }
            }
            // Add headers
            if (matchedResponse.Headers != null)
            {
                foreach (var header in matchedResponse.Headers)
                {
                    var envVars = _configService.GetDefaultEnvironment()?.Variables ?? new Dictionary<string, string>();
                    
                    string headerValue = StubManager.Replace(header.Value, new Dictionary<string, object>
                    {
                        {"env", envVars},
                        {"headers", headers},
                        {"path", pathParams},
                        {"query", queryParams},
                        {"body", bodyContent ?? new JObject()}
                    });
                    
                    response.Headers.Add(header.Key, headerValue);
                    
                    // Set content type if it's in the headers
                    if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        response.ContentType = headerValue;
                    }
                }
            }
            
            // Process response template
            string responseContent;
            
            if (matchedResponse.ResponseTemplate != null)
            {
                if (matchedResponse.ResponseTemplate is string str)
                {
                    responseContent = str;
                }
                else
                {
                    responseContent = JsonConvert.SerializeObject(matchedResponse.ResponseTemplate, Formatting.Indented);
                }

                
                // Replace any template variables with actual values
                // responseContent = ApplyTemplateVariables(responseContent, pathParams);
                var envVars = _configService.GetDefaultEnvironment();
                responseContent = StubManager.Replace(responseContent, new System.Collections.Generic.Dictionary<string, object>
                {
                    {"env", envVars},
                    {"headers", headers},
                    {"path", pathParams},
                    {"query", queryParams},
                    {"body", bodyContent ?? new JObject()}
                });
                
                // Process dynamic template expressions (e.g., {{$random:int:1000:1999}})
                responseContent = ProcessDynamicTemplate(responseContent, request);
                
                // Apply advanced template replacements
                
                // 1. Replace body.X references with actual body values
                if (bodyContent != null && bodyContent.Type == JTokenType.Object)
                {
                    foreach (var prop in (JObject)bodyContent)
                    {
                        string placeholder = $"{{{{body.{prop.Key}}}}}";
                        if (responseContent.Contains(placeholder))
                        {
                            string replacement = prop.Value?.ToString() ?? string.Empty;
                            responseContent = responseContent.Replace(placeholder, replacement);
                        }
                    }
                }
            }
            else
            {
                responseContent = "{}";
            }
            
            byte[] responseBuffer = Encoding.UTF8.GetBytes(responseContent);
            response.ContentLength64 = responseBuffer.Length;
            
            try
            {
                await response.OutputStream.WriteAsync(responseBuffer, 0, responseBuffer.Length);
                
                // Always show basic response info
                Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss")} - {method} {requestUrl} - {response.StatusCode} (Advanced)");
                
                // Show detailed info only in debug mode
                if (_debug)
                {
                    ConsoleHelper.WriteSuccess($"Responded to {method} {requestUrl} with status {response.StatusCode} (Advanced Mock)");
                    ConsoleHelper.WriteInfo($"Response body: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                // Always show basic error
                ConsoleHelper.WriteError($"Error sending response: {ex.Message}");
                
                // Show detailed stack trace only in debug mode
                if (_debug)
                {
                    Console.WriteLine(ex.StackTrace);
                }
            }
            finally
            {
                response.Close();
            }
        }
        
        private async Task ProcessMultipartFormDataAsync(HttpListenerRequest request, 
            Dictionary<string, string> formFields, 
            Dictionary<string, (string FileName, byte[] Data)> files)
        {
            // Get the boundary from the Content-Type header
            string boundary = GetBoundaryFromContentType(request.ContentType ?? string.Empty);
            if (string.IsNullOrEmpty(boundary))
            {
                throw new ArgumentException("Content-Type header does not contain boundary");
            }
            
            using (var memoryStream = new MemoryStream())
            {
                // Copy the request stream to a memory stream so we can read it multiple times
                byte[] fileBuffer = new byte[4096];
                int bytesRead;
                
                while ((bytesRead = await request.InputStream.ReadAsync(fileBuffer, 0, fileBuffer.Length)) > 0)
                {
                    await memoryStream.WriteAsync(fileBuffer, 0, bytesRead);
                }
                
                // Reset the stream position so we can read from the beginning
                memoryStream.Position = 0;
                
                // Parse the multipart form data
                try
                {
                    string boundaryMarker = "--" + boundary;
                    string endBoundaryMarker = boundaryMarker + "--";
                    
                    using (var reader = new StreamReader(memoryStream, request.ContentEncoding))
                    {
                        string? line;
                        bool isFirstBoundary = true;
                        
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (isFirstBoundary && line.StartsWith(boundaryMarker))
                            {
                                isFirstBoundary = false;
                                continue;
                            }
                            
                            if (line.StartsWith(boundaryMarker))
                            {
                                if (line.Equals(endBoundaryMarker))
                                {
                                    // End of form data
                                    break;
                                }
                                
                                // Parse headers
                                string? contentDisposition = null;
                                string? contentType = null;
                                
                                while ((line = reader.ReadLine()) != null && !string.IsNullOrEmpty(line))
                                {
                                    if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        contentDisposition = line.Substring("Content-Disposition:".Length).Trim();
                                    }
                                    else if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        contentType = line.Substring("Content-Type:".Length).Trim();
                                    }
                                }
                                
                                // Parse content disposition to get name and filename
                                string? name = null;
                                string? filename = null;
                                
                                if (contentDisposition != null)
                                {
                                    var parts = contentDisposition.Split(';');
                                    foreach (var part in parts)
                                    {
                                        var trimmedPart = part.Trim();
                                        
                                        if (trimmedPart.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                                        {
                                            name = trimmedPart.Substring(5).Trim('"');
                                        }
                                        else if (trimmedPart.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                                        {
                                            filename = trimmedPart.Substring(9).Trim('"');
                                        }
                                    }
                                }
                                
                                // Read content
                                var contentBuilder = new StringBuilder();
                                MemoryStream? fileData = null;
                                
                                if (!string.IsNullOrEmpty(filename))
                                {
                                    fileData = new MemoryStream();
                                }
                                
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (line.StartsWith(boundaryMarker))
                                    {
                                        break;
                                    }
                                    
                                    if (fileData != null)
                                    {
                                        byte[] lineBytes = request.ContentEncoding.GetBytes(line + "\r\n");
                                        await fileData.WriteAsync(lineBytes, 0, lineBytes.Length);
                                    }
                                    else
                                    {
                                        contentBuilder.AppendLine(line);
                                    }
                                }
                                
                                // Add to collection
                                if (!string.IsNullOrEmpty(name))
                                {
                                    if (fileData != null && !string.IsNullOrEmpty(filename))
                                    {
                                        files[name] = (filename, fileData.ToArray());
                                        fileData.Dispose();
                                    }
                                    else
                                    {
                                        formFields[name] = contentBuilder.ToString().Trim();
                                    }
                                }
                                
                                if (line?.Equals(endBoundaryMarker) == true)
                                {
                                    // End of form data
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Always show basic error
                    ConsoleHelper.WriteError($"Error parsing multipart form data: {ex.Message}");
                    
                    // Show detailed stack trace only in debug mode
                    if (_debug)
                    {
                        Console.WriteLine(ex.StackTrace);
                    }
                    throw;
                }
            }
        }
        
        private string GetBoundaryFromContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return string.Empty;
                
            int index = contentType.IndexOf("boundary=");
            if (index == -1)
                return string.Empty;
                
            string boundary = contentType.Substring(index + 9); // 9 is the length of "boundary="
            
            if (boundary.StartsWith("\"") && boundary.EndsWith("\""))
                boundary = boundary.Substring(1, boundary.Length - 2);
                
            return boundary;
        }
        
        private string ProcessDynamicTemplate(string template, HttpListenerRequest request)
        {
            // Parse request parameters and populate template variables
            var templateVars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            
            // Add URL path parameters
            if (request.Url != null)
            {
                string requestPath = request.Url.AbsolutePath;
                
                
                // Add query parameters
                foreach (string key in request.QueryString.Keys)
                {
                    if (key != null)
                    {
                        templateVars["query." + key] = request.QueryString[key];
                    }
                }
            }
            
            // Add headers
            foreach (string key in request.Headers.Keys)
            {
                if (key != null)
                {
                    templateVars["header." + key] = request.Headers[key];
                }
            }
            
            // Add request body (if any)
            if (request.HasEntityBody)
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    templateVars["body"] = body;
                    
                    // Try to parse as JSON
                    try
                    {
                        var jsonObj = JsonConvert.DeserializeObject<JObject>(body);
                        if (jsonObj != null)
                        {
                            foreach (var prop in jsonObj.Properties())
                            {
                                templateVars["body." + prop.Name] = prop.Value?.ToString();
                            }
                        }
                    }
                    catch
                    {
                        // If not JSON, just use the raw body
                    }
                }
            }
            
            // Generate random ID values
            templateVars["random.id"] = Guid.NewGuid().ToString();
            templateVars["random.number"] = new Random().Next(10000).ToString();
            templateVars["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            
            // Replace template variables in the template
            string result = template;
            foreach (var kv in templateVars)
            {
                result = result.Replace($"{{{{{kv.Key}}}}}", kv.Value ?? string.Empty);
            }
            
            return result;
        }
        
        // Method to apply template variables to a string
        private string ApplyTemplateVariables(string input, Dictionary<string, string>? pathParams = null)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            
            // Create combined dictionary with path parameters and environment variables
            var allVariables = new Dictionary<string, string>();
            
            // Add path parameters (highest priority)
            if (pathParams != null)
            {
                foreach (var param in pathParams)
                {
                    allVariables[param.Key] = param.Value;
                }
            }
            
            // Add random/built-in values
            allVariables["random.id"] = Guid.NewGuid().ToString();
            allVariables["random.number"] = new Random().Next(10000).ToString();
            allVariables["timestamp"] = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            
            // Add environment variables (lowest priority, won't override path params)
            if (_configService.GetDefaultEnvironment()?.Variables != null)
            {
                foreach (var envVar in _configService.GetDefaultEnvironment()?.Variables ?? new Dictionary<string, string>())
                {
                    if (!allVariables.ContainsKey(envVar.Key))
                    {
                        allVariables[envVar.Key] = envVar.Value;
                    }
                }
            }
            
            // First, process special templates like {{$random:int:1000:1999}}
            var specialTemplatePattern = new Regex(@"{{(\$random:int:(\d+):(\d+))}}", RegexOptions.Compiled);
            input = specialTemplatePattern.Replace(input, match =>
            {
                if (match.Groups.Count >= 4)
                {
                    int minValue = int.Parse(match.Groups[2].Value);
                    int maxValue = int.Parse(match.Groups[3].Value);
                    return new Random().Next(minValue, maxValue + 1).ToString();
                }
                return match.Value;
            });
            
            // Then, use regex to find and replace standard {{variable}} patterns
            var variablePattern = new Regex(@"{{(.+?)}}", RegexOptions.Compiled);
            
            return variablePattern.Replace(input, match =>
            {
                var variableName = match.Groups[1].Value.Trim();
                if (allVariables.TryGetValue(variableName, out var value))
                {
                    return value;
                }
                return match.Value; // Keep the original {{variable}} if not found
            });
        }
        
        // Extract path parameters from a URL
        private bool ExtractPathParameters(string pattern, string path, out Dictionary<string, string> pathParams)
        {
            pathParams = new Dictionary<string, string>();
            
            if (!pattern.Contains("{") || !pattern.Contains("}"))
                return false;
                
            // Extract parameter names from pattern
            var paramNames = new List<string>();
            int paramIndex = 0;
            while ((paramIndex = pattern.IndexOf('{', paramIndex)) != -1)
            {
                int endIndex = pattern.IndexOf('}', paramIndex);
                if (endIndex == -1) break;
                
                string paramName = pattern.Substring(paramIndex + 1, endIndex - paramIndex - 1);
                paramNames.Add(paramName);
                paramIndex = endIndex + 1;
            }
            
            if (paramNames.Count == 0)
                return false;
                
            // Convert pattern to regex with named capture groups
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\{", "{")
                .Replace("\\}", "}");
                
            for (int i = 0; i < paramNames.Count; i++)
            {
                regexPattern = regexPattern.Replace("{" + paramNames[i] + "}", "([^/]+)");
            }
            
            regexPattern += "$";
            
            // Match against path
            var match = Regex.Match(path, regexPattern);
            if (match.Success && match.Groups.Count > 1)
            {
                for (int i = 0; i < paramNames.Count; i++)
                {
                    if (i + 1 < match.Groups.Count)
                    {
                        pathParams[paramNames[i]] = match.Groups[i + 1].Value;
                    }
                }
                
                return pathParams.Count > 0;
            }
            
            return false;
        }
        
        private bool PatternMatchesPath(string pattern, string path, out Dictionary<string, string>? pathParams)
        {
            pathParams = null;
            
            if (!pattern.Contains("{") && !pattern.Contains("}"))
            {
                return string.Equals(pattern, path, StringComparison.OrdinalIgnoreCase);
            }
            
            // Extract parameter names from pattern
            var paramNames = new List<string>();
            int paramIndex = 0;
            while ((paramIndex = pattern.IndexOf('{', paramIndex)) != -1)
            {
                int endIndex = pattern.IndexOf('}', paramIndex);
                if (endIndex == -1) break;
                
                string paramName = pattern.Substring(paramIndex + 1, endIndex - paramIndex - 1);
                paramNames.Add(paramName);
                paramIndex = endIndex + 1;
            }
            
            // Convert pattern to regex
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\{", "{")
                .Replace("\\}", "}");
                
            for (int i = 0; i < paramNames.Count; i++)
            {
                regexPattern = regexPattern.Replace("{" + paramNames[i] + "}", "([^/]+)");
            }
            
            regexPattern += "$";
            
            // Match against path
            var match = Regex.Match(path, regexPattern);
            if (match.Success)
            {
                pathParams = new Dictionary<string, string>();
                for (int i = 0; i < paramNames.Count; i++)
                {
                    pathParams[paramNames[i]] = match.Groups[i + 1].Value;
                }
                
                return true;
            }
            
            return false;
        }
    }
}