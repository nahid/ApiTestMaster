# Apify

A powerful C# CLI application for comprehensive API testing and mocking, enabling developers to streamline API validation and development workflows with rich configuration and execution capabilities.

## Features

- **Comprehensive API Testing**: Define and run detailed API tests.
    - **Multiple Request Methods**: Support for GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS.
    - **Rich Payload Types**: JSON, Text, Form Data.
    - **File Upload Support**: Test multipart/form-data requests with file uploads.
    - **Detailed Assertions**: Validate response status, headers, body content (JSON properties, arrays, values), and response time.
- **Integrated Mock Server**: Simulate API endpoints for development and testing.
    - **Dynamic & Conditional Responses**: Define mock responses based on request parameters, headers, or body content.
    - **Template Variables**: Use built-in (random data, timestamps) and custom (request-derived) variables in mock responses.
    - **File-based Configuration**: Manage mock definitions in simple `.mock.json` files.
- **Environment Management**: Use different configurations for development, staging, production, etc.
    - **Variable Overriding**: Project, Environment, and Request-level variable support with clear precedence.
- **User-Friendly CLI**:
    - **Interactive Creation**: Commands to interactively create test and mock definitions.
    - **Detailed Reports**: Comprehensive output with request, response, and assertion details.
    - **Visual Progress Indicators**: Animated progress display for running multiple tests.
- **Deployment & Extensibility**:
    - **Single File Deployment**: Simplified deployment as a single executable file.
    - **.NET 8.0 & .NET 9 Ready**: Built with .NET 8.0, with automatic multi-targeting for .NET 9.0 when available.

## Getting Started

### Prerequisites

- .NET 8.0 SDK (required)
- .NET 9.0 SDK (optional, for building with .NET 9.0 when available)

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/apify.git # Replace with actual URL
cd apify

# Build the project
dotnet build

# Run the application (from the project directory)
dotnet run -- [command] [options]
# Example:
dotnet run -- --help
dotnet run -- init --name "My API Project" --base-url "https://api.example.com"
```

For global installation or easier access, build as a single file and place it in your PATH:

### Single File Build

For simplified deployment, you can build a single executable file:

```bash
# Build a single file executable for your platform (e.g., linux-x64)
dotnet publish -c Release -r linux-x64 --self-contained true
# Windows:
# dotnet publish -c Release -r win-x64 --self-contained true
# macOS (Intel):
# dotnet publish -c Release -r osx-x64 --self-contained true
# macOS (ARM64):
# dotnet publish -c Release -r osx-arm64 --self-contained true

# Then, you can copy the executable (e.g., ./bin/Release/net8.0/linux-x64/publish/apify)
# to a directory in your PATH, for example /usr/local/bin/
# cp ./bin/Release/net8.0/linux-x64/publish/apify /usr/local/bin/apify
# After this, you can run 'apify' directly from any terminal.
```

## Core Concepts

### Project Initialization (`apify init`)

To start using Apify in your project, navigate to your project's root directory and run:

```bash
apify init --name "My Project API Tests" --base-url "https://api.myproject.com"
```

This command interactively prompts for:
- Project Name
- Base URL for your API
- Default Environment Name (e.g., "Development")
- Additional environments (e.g., "Staging,Production")

It creates:
- `apify-config.json`: The main configuration file.
- `.apify/`: A directory to store your API test definitions (`.json`) and mock definitions (`.mock.json`).
- Sample API test and mock definition files within `.apify/apis/` and `.apify/mocks/` respectively.
- A `MockServer` configuration block in `apify-config.json`.

### Configuration (`apify-config.json`)

This file stores project-level settings, environments, and mock server configuration.

```json
{
  "Name": "My Project API Tests",
  "Description": "API Tests for My Project",
  "DefaultEnvironment": "Development",
  "Variables": {
    "globalProjectVar": "This variable is available in all environments and tests"
  },
  "Environments": [
    {
      "Name": "Development",
      "Description": "Development environment specific variables",
      "Variables": {
        "baseUrl": "https://dev-api.myproject.com",
        "apiKey": "dev-secret-key"
      }
    },
    {
      "Name": "Production",
      "Description": "Production environment specific variables",
      "Variables": {
        "baseUrl": "https://api.myproject.com",
        "apiKey": "prod-secret-key"
      }
    }
  ],
  "MockServer": {
    "Port": 8080,
    "Directory": ".apify/mocks",
    "Verbose": false,
    "EnableCors": true,
    "DefaultHeaders": {
      "X-Mock-Server": "Apify"
    },
    "FileStoragePath": ".apify/uploads/mock-server"
  }
}
```

- **`Name`, `Description`**: Project metadata.
- **`DefaultEnvironment`**: The environment used if `--env` is not specified.
- **`Variables` (Project-Level)**: Key-value pairs available across all tests and environments unless overridden.
- **`Environments`**: An array of environment objects.
    - **`Name`**: Unique name for the environment (e.g., "Development", "Staging").
    - **`Variables`**: Key-value pairs specific to this environment. These override project-level variables.
- **`MockServer`**: Configuration for the mock server.
    - **`Port`**: Port for the mock server.
    - **`Directory`**: Directory containing mock definition files.
    - **`Verbose`**: Enable verbose logging for the mock server.
    - **`EnableCors`**: Enable CORS headers (defaults to allow all).
    - **`DefaultHeaders`**: Headers to be added to all mock responses.
    - **`FileStoragePath`**: Path to store files uploaded to mock endpoints (if configured in the mock definition).

### API Test Definitions (`.json`)

API tests are defined in JSON files (e.g., `.apify/users/get-users.json`).

Structure:
```json
{
  "Name": "Get All Users",
  "Description": "Fetches the list of all users",
  "Uri": "{{baseUrl}}/users?page={{defaultPage}}",
  "Method": "GET",
  "Headers": {
    "Accept": "application/json",
    "X-Api-Key": "{{apiKey}}"
  },
  "Payload": null,
  "PayloadType": "none", // "json", "text", "formData"
  "Files": [], // For "multipart/form-data"
  "Timeout": 30000, // Optional, in milliseconds
  "Tags": ["users", "smoke"], // Optional, for filtering tests
  "Variables": { // Request-specific variables (highest precedence)
    "defaultPage": "1"
  },
  "Tests": [
    {
      "Name": "Status code is 200 OK",
      "AssertType": "StatusCode",
      "ExpectedValue": "200"
    },
    {
      "Name": "Response body is an array",
      "AssertType": "IsArray",
      "Property": "data" // Path to property, e.g., "user.details.name" or "users[0].id"
    },
    {
      "Name": "First user has an ID",
      "AssertType": "ContainsProperty",
      "Property": "data[0].id"
    },
    {
      "Name": "Content-Type header is application/json",
      "AssertType": "HeaderContains",
      "Property": "Content-Type", // Header name
      "ExpectedValue": "application/json"
    },
    {
      "Name": "Response time is below 500ms",
      "AssertType": "ResponseTimeBelow",
      "ExpectedValue": "500" // Milliseconds
    },
    {
      "Name": "User count matches expected",
      "AssertType": "Equal",
      "Property": "meta.total_items",
      "ExpectedValue": "25" // Expected value (can be string, number, boolean)
    },
    {
      "Name": "User list is not empty",
      "AssertType": "ArrayNotEmpty",
      "Property": "data"
    }
  ]
}
```

- **`Variables` (Request-Level)**: Override environment and project variables.
- **`Tags`**: Used for filtering tests with `apify tests --tag <tagname>`.
- **`Tests` (Assertions)**:
    - **`AssertType`**: The type of assertion. Supported types:
        - `StatusCode`: Checks the HTTP status code. `ExpectedValue` is the code (e.g., "200", "4xx").
        - `ContainsProperty`: Checks if a JSON property exists. `Property` is the JSONPath-like path.
        - `HeaderContains`: Checks if a response header contains a value. `Property` is header name, `ExpectedValue` is the content.
        - `ResponseTimeBelow`: Checks if response time is below `ExpectedValue` (ms).
        - `Equal`: Checks if a JSON property's value equals `ExpectedValue`. `Property` is the JSONPath-like path.
        - `IsArray`: Checks if a JSON property is an array. `Property` is the JSONPath-like path.
        - `ArrayNotEmpty`: Checks if a JSON array property is not empty. `Property` is the JSONPath-like path.
    - **`Property`**: For JSON assertions, this is a JSONPath-like expression (e.g., `user.id`, `items[0].name`, `response.data.info`). For header assertions, it's the header name.
    - **`ExpectedValue`**: The value to assert against.

### Mock API Definitions (`.mock.json`)

Mock APIs are defined in `.mock.json` files (e.g., `.apify/mocks/users/get-user-by-id.mock.json`). Apify uses `AdvancedMockApiDefinition`.

Structure (`AdvancedMockApiDefinition`):
```json
{
  "Name": "Mock User by ID",
  "Method": "GET",
  "Endpoint": "/api/users/:id", // Path parameters with :param or {param}
  "Responses": [
    {
      "Condition": "path.id == \"1\"", // JavaScript-like condition
      "StatusCode": 200,
      "ContentType": "application/json",
      "Headers": {
        "X-Source": "Mock-Conditional-User1"
      },
      "ResponseTemplate": {
        "id": 1,
        "name": "John Doe (Mocked)",
        "email": "john.mock@example.com",
        "requested_id": "{{path.id}}",
        "random_code": "{{$random:int:1000:9999}}"
      }
    },
    {
      "Condition": "query.type == \"admin\" && header.X-Admin-Token == \"SUPER_SECRET\"",
      "StatusCode": 200,
      "ResponseTemplate": {
        "id": "{{path.id}}",
        "name": "Admin User (Mocked)",
        "email": "admin.mock@example.com",
        "role": "admin",
        "token_used": "{{header.X-Admin-Token}}",
        "uuid": "{{$random:uuid}}"
      }
    },
    {
      "Condition": "body.status == \"pending\"", // Example for POST/PUT
      "StatusCode": 202,
      "ResponseTemplate": {
        "message": "Request for user {{path.id}} with status 'pending' accepted.",
        "received_payload": "{{body}}" // Full request body
      }
    },
    {
      "Condition": "true", // Default response if no other conditions match
      "StatusCode": 404,
      "ResponseTemplate": {
        "error": "User not found",
        "id_searched": "{{path.id}}"
      }
    }
  ]
}
```

- **`Endpoint`**: The URL path for the mock. Supports path parameters like `/users/:id` or `/users/{id}`.
- **`Responses`**: An array of conditional response objects. They are evaluated in order.
    - **`Condition`**: A JavaScript-like expression to determine if this response should be used.
        - Access request data:
            - `path.paramName` (e.g., `path.id`)
            - `query.paramName` (e.g., `query.page`)
            - `header.HeaderName` (e.g., `header.Authorization`, case-insensitive)
            - `body.fieldName` (e.g., `body.username`, for JSON bodies)
        - `true` can be used for a default fallback response.
    - **`StatusCode`**: The HTTP status code to return.
    - **`ContentType`**: The `Content-Type` header for the response.
    - **`Headers`**: An object of response headers.
    - **`ResponseTemplate`**: The body of the response. Can be a JSON object or a string.
        - **Template Variables**:
            - `{{path.paramName}}`: Value of a path parameter.
            - `{{query.paramName}}`: Value of a query parameter.
            - `{{header.HeaderName}}`: Value of a request header (case-insensitive).
            - `{{body.fieldName}}`: Value of a field from the JSON request body.
            - `{{body}}`: The full raw request body (string).
            - `{{$random:int:min:max}}`: A random integer between min and max (inclusive). E.g., `{{$random:int:1:100}}`.
            - `{{$random:uuid}}`: A random UUID.
            - `{{timestamp}}`: Current Unix timestamp (seconds).
            - `{{datetime}}`: Current datetime in ISO 8601 format.
            - Any environment or project variable (e.g., `{{baseUrl}}`).

## Commands

Apify commands are run as `apify <command> [subcommand] [options]` if installed globally, or `dotnet run -- <command> [subcommand] [options]` if run from the project directory.

### `apify init`
Initializes a new API testing project in the current directory.

```bash
apify init --name "My API" --base-url "https://api.example.com" [--environment "CustomDev"] [--force]
```
- `--name`: (Required) The name of the API testing project.
- `--base-url`: (Required) The base URL for API endpoints.
- `--environment`: The name for the default environment (default: "Development").
- `--force`: Overwrite existing `apify-config.json` and `.apify` directory if they exist.
- Prompts for project name, base URL, default environment name, and other environments to create.
- Creates `apify-config.json`, `.apify/` directory with sample test and mock files.

### `apify create request`
Interactively creates a new API test definition file.

```bash
apify create request --file users.getById [--force]
```
- `--file`: (Required) The file path for the new API request definition (e.g., `users.all` becomes `.apify/users/all.json`). The `.json` extension is added automatically.
- `--force`: Overwrite if the file already exists.
- Prompts for request name, HTTP method, URI, headers, payload, and basic assertions.

### `apify create mock`
Interactively creates a new mock API definition file.

```bash
apify create mock --file mocks.users.getById [--force]
```
- `--file`: (Required) The file path for the new mock API definition (e.g., `users.get` becomes `.apify/users/get.mock.json`). The `.mock.json` extension is added automatically.
- `--force`: Overwrite if the file already exists.
- Prompts for mock name, HTTP method, endpoint path, status code, content type, response body, headers, and conditional responses.

### `apify run`
Executes API tests from specified definition files.

```bash
apify run <file_or_pattern_1> [file_or_pattern_2...] [--env <environment_name>] [--profile <profile_name>] [--verbose]
```
- `files`: (Required) One or more API definition file paths or glob patterns (e.g., `users/all.json`, `.apify/orders/*.json`). Dot notation like `users.all` is also supported.
- `--env <environment_name>`: Specifies the environment to use (e.g., "Production"). Uses default from `apify-config.json` if not set.
- `--profile <profile_name>`: Specifies the configuration profile name (default: "Default").
- `--verbose` or `-v`: Displays detailed output, including request and response bodies.

### `apify tests`
Runs all API tests found in the `.apify` directory and its subdirectories.

```bash
apify tests [--env <environment_name>] [--tag <tag_name>] [--verbose]
```
- `--env <environment_name>`: Specifies the environment.
- `--tag <tag_name>`: Filters and runs only tests that have the specified tag in their definition.
- `--verbose` or `-v`: Displays detailed output.
- Shows visual progress indicators and a summary at the end.

### `apify mock-server`
Starts a local API mock server using mock definition files.

```bash
apify mock-server [--port <port_number>] [--directory <path_to_mocks>] [--verbose]
```
- `--port <port_number>`: Port for the mock server (default: 8080 or as configured in `apify-config.json`).
- `--directory <path_to_mocks>`: Directory containing mock definition files (default: `.apify/mocks` or as configured).
- `--verbose` or `-v`: Enable verbose logging for the mock server.
- Reads configuration from the `MockServer` block in `apify-config.json` but command-line options take precedence.

### `apify list-env`
Lists all available environments and their variables from `apify-config.json`.

```bash
apify list-env
```

### Global Options
These options can be used with most commands.

- `--debug`: Show detailed debug output, including stack traces and internal logging. Useful for troubleshooting Apify itself.
- `--verbose` or `-v` (for `run`, `tests`, `mock-server`): Enables more detailed output for the specific command.

## CI/CD with GitHub Actions

This project includes GitHub Actions workflows for continuous integration and releases.

### Continuous Integration

The CI workflow (if included in your project, typically in `.github/workflows/ci.yml`) runs on every push to the main branch and pull requests. It usually:
1. Builds the project.
2. Runs API tests (e.g., `apify tests --env ci`) to verify functionality against a deployed or mocked environment.

### Release Process

To create a release (if configured, typically in `.github/workflows/release.yml`):
1. **Tag-based release**: Create and push a new tag: `git tag -a v1.0.0 -m "Version 1.0.0" && git push origin v1.0.0`
2. **Manual release**: Via GitHub Actions UI.

This process typically builds single file executables for various platforms and attaches them to a GitHub release.

## Examples

### Example 1: Testing and Mocking a User API

1.  **Initialize Project:**
    ```bash
    apify init --name "User API" --base-url "http://localhost:8080/api"
    # (Assuming mock server will run on port 8080 for testing against mocks)
    ```

2.  **Create a Mock for `GET /api/users/:id`:**
    Use `apify create mock --file users.getById` and define it like this in `.apify/mocks/users/getById.mock.json`:
    ```json
    {
      "Name": "Mock User by ID",
      "Method": "GET",
      "Endpoint": "/api/users/:id",
      "Responses": [
        {
          "Condition": "path.id == \"1\"",
          "StatusCode": 200,
          "ContentType": "application/json",
          "ResponseTemplate": { "id": 1, "name": "Mocked Alice", "email": "alice.mock@example.com" }
        },
        {
          "Condition": "true",
          "StatusCode": 404,
          "ResponseTemplate": { "error": "MockUser not found" }
        }
      ]
    }
    ```

3.  **Start the Mock Server:**
    ```bash
    apify mock-server --port 8080
    ```
    (In a separate terminal)

4.  **Create an API Test for `GET /api/users/1`:**
    Use `apify create request --file users.getUser1` and define it in `.apify/apis/users/getUser1.json`:
    ```json
    {
      "Name": "Get User 1",
      "Uri": "{{baseUrl}}/users/1", // baseUrl will be http://localhost:8080/api
      "Method": "GET",
      "Tests": [
        { "Name": "Status 200", "AssertType": "StatusCode", "ExpectedValue": "200" },
        { "Name": "ID is 1", "AssertType": "Equal", "Property": "id", "ExpectedValue": 1 },
        { "Name": "Name is Mocked Alice", "AssertType": "Equal", "Property": "name", "ExpectedValue": "Mocked Alice" }
      ]
    }
    ```

5.  **Run the Test:**
    ```bash
    apify run users.getUser1 --verbose
    # This will hit your running mock server.
    ```

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details. (Ensure you have a LICENSE file).