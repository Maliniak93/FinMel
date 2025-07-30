# FinMel - Personal Finance Management Application

## Description

FinMel is a comprehensive personal finance management application built using Clean Architecture and .NET 9. The system enables importing bank statements from various sources and formats (XML, CSV, API), processes them through adapter/importer layers, and maps data to a standardized domain model. This allows easy extension for additional banks and formats.

The application supports expense analysis, multi-currency handling, report generation, dashboards, and future expansion to other asset types (investments, real estate).

## Key Features

- **Universal Bank Statement Import**: Support for multiple banks and formats through adapter pattern
- **Multi-Currency Support**: Currency conversion with historical exchange rates
- **Comprehensive Reporting**: Dynamic and stored reports with automated updates
- **Extensible Design**: Easy addition of new banks, formats, and asset types
- **Rich Domain Model**: Generic entities supporting various financial instruments
- **Background Processing**: Automated report updates and currency rate fetching

## Architecture

The project uses Clean Architecture with the following layers:

- **Domain** (`FinMel.Domain`) - Business logic, entities, aggregates, value objects
  - Core entities: BankStatement, BankAccount, Transaction, User, Currency
  - Value objects: Money, Address, ImportMetadata
  - Domain events: BankStatementImported, ExchangeRateUpdated
- **Application** (`FinMel.Application`) - Use cases, application services, import adapters
  - Services: BankStatementService, ImportService, ReportService
  - Import system: Adapters for different bank formats
  - Repository abstractions
- **Infrastructure** (`FinMel.Infrastructure`) - Repository implementations, external services
  - Data persistence with Entity Framework Core
  - Import implementations: SantanderXmlImporter, GenericCsvImporter
  - External services: Currency APIs, file storage, background jobs
- **Web API** (`FinMel.Web`) - Controllers, middleware, configuration
  - API endpoints for bank statements, accounts, transactions, reports
  - File upload for statement import
  - Background services for automated processing
- **Contracts** (`FinMel.Contracts`) - DTOs, view models, API contracts
- **ServiceDefaults** (`FinMel.ServiceDefaults`) - .NET Aspire configuration

## Technologies

- .NET 9
- ASP.NET Core Web API
- Entity Framework Core (PostgreSQL for production)
- .NET Aspire (orchestration and containerization)
- Clean Architecture & Domain-Driven Design
- Quartz.NET (background jobs)
- Multi-currency support with exchange rate APIs
- xUnit (unit testing)
- Docker

## Running the Application

### Local run with .NET Aspire (Recommended)

```bash
dotnet run --project src/FinMel.AppHost
```

### Running with Docker

```bash
docker build -t finmel .
docker run -p 8080:8080 finmel
```

### Running Web API directly

```bash
dotnet run --project src/FinMel.Web
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests for specific project
dotnet test tests/FinMel.Domain.Tests
dotnet test tests/FinMel.Application.Tests
dotnet test tests/FinMel.Infrastructure.Tests
dotnet test tests/FinMel.Web.Tests

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Project Structure

```
FinMel/
├── src/
│   ├── FinMel.Domain/           # Domain layer
│   │   ├── Entities/            # Domain entities (BankStatement, Transaction, etc.)
│   │   ├── ValueObjects/        # Value objects (Money, Address, etc.)
│   │   ├── Events/              # Domain events
│   │   ├── Enums/               # Domain enumerations
│   │   └── Abstractions/        # Base classes and interfaces
│   ├── FinMel.Application/      # Application layer
│   │   ├── Services/            # Application services
│   │   ├── Import/              # Import adapters and strategies
│   │   ├── Repositories/        # Repository abstractions
│   │   └── Behaviors/           # Cross-cutting behaviors
│   ├── FinMel.Infrastructure/   # Infrastructure layer
│   │   ├── Persistence/         # EF Core, configurations, migrations
│   │   ├── Import/              # Concrete import implementations
│   │   ├── ExternalServices/    # Currency APIs, file storage
│   │   └── BackgroundJobs/      # Quartz.NET jobs
│   ├── FinMel.Web/             # Web API
│   │   ├── Controllers/         # API controllers
│   │   ├── Middleware/          # Request/response middleware
│   │   └── BackgroundServices/  # Hosted services
│   ├── FinMel.Contracts/       # Contracts/DTOs
│   │   ├── BankStatements/      # Bank statement DTOs
│   │   ├── Import/              # Import-related DTOs
│   │   ├── Reports/             # Report DTOs
│   │   └── Common/              # Shared DTOs
│   ├── FinMel.AppHost/         # .NET Aspire AppHost
│   └── FinMel.ServiceDefaults/ # Aspire Service Defaults
├── tests/
│   ├── FinMel.Domain.Tests/     # Domain layer tests
│   ├── FinMel.Application.Tests/# Application layer tests
│   ├── FinMel.Infrastructure.Tests/ # Infrastructure tests
│   └── FinMel.Web.Tests/       # Web API integration tests
├── Pomysly/                    # Design documents (Polish - for reference)
├── Dockerfile                  # Docker configuration
├── .copilot-instructions.md    # GitHub Copilot guidelines
├── PROJECT-GUIDELINES.md       # Comprehensive development guide
└── FinMel.sln                 # Solution file
```

## Development

### Prerequisites

- .NET 9 SDK
- Docker (optional)
- PostgreSQL
- Visual Studio Code or Visual Studio 2022

### Building the Solution

```bash
# Build the solution
dotnet build

# Restore packages
dotnet restore

# Clean solution
dotnet clean
```

## Import System

FinMel supports importing bank statements from various formats:

### Supported Formats

- **Santander XML**: Native XML format from Santander bank
- **Generic CSV**: Configurable CSV format with column mapping
- **Bank API**: Direct API integration (future)

### Import Process

1. Upload file through API endpoint
2. Format detection and adapter selection
3. File parsing and data extraction
4. Mapping to generic domain model
5. Business rule validation
6. Database persistence
7. Background report updates

### Code Quality

The project enforces:

- Nullable reference types
- Code analysis with warnings as errors
- Clean Architecture principles

### Testing Strategy

- **Unit Tests**: Business logic in Domain layer
- **Integration Tests**: API endpoints and external dependencies
- **Architecture Tests**: Ensure Clean Architecture compliance

## Future Extensions

The application is prepared for extension with:

- Database (Entity Framework Core)
- Authorization and authentication (JWT)
- Frontend (Angular - planned)
- Additional financial features
- Logging and monitoring
- Redis caching

## Contributing

1. Follow Clean Architecture principles
2. Write tests for new features
3. Use English for all code, comments, and documentation
4. Follow the guidelines in `.copilot-instructions.md`
5. Read the comprehensive development guide in `PROJECT-GUIDELINES.md`
