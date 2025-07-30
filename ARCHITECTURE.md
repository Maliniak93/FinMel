# FinMel Architecture Overview

## Clean Architecture Layers

```
┌─────────────────────────────────────────┐
│              Web Layer                  │
│         (FinMel.Web)                   │  ← Controllers, Middleware
│                                        │
├─────────────────────────────────────────┤
│          Infrastructure Layer           │
│      (FinMel.Infrastructure)           │  ← Repositories, External Services
│                                        │
├─────────────────────────────────────────┤
│          Application Layer              │
│       (FinMel.Application)             │  ← Use Cases, Services
│                                        │
├─────────────────────────────────────────┤
│            Domain Layer                 │
│         (FinMel.Domain)                │  ← Business Logic, Entities
│                                        │
├─────────────────────────────────────────┤
│           Contracts Layer               │
│        (FinMel.Contracts)              │  ← DTOs, Interfaces
└─────────────────────────────────────────┘
```

## Key Principles

### ✅ Dependencies Flow Inward

- **Web** → **Infrastructure** → **Application** → **Domain** → **Contracts**
- Domain layer has NO dependencies on outer layers
- All dependencies point toward the center

### ✅ Separation of Concerns

- **Domain**: Business rules and logic
- **Application**: Use cases and workflows
- **Infrastructure**: Data access and external services
- **Web**: HTTP concerns and API endpoints
- **Contracts**: Data transfer objects

### ✅ Dependency Inversion

- High-level modules don't depend on low-level modules
- Both depend on abstractions (interfaces)
- Abstractions don't depend on details

## Project Structure

```
FinMel/
├── src/
│   ├── FinMel.Domain/           # Core business logic
│   ├── FinMel.Contracts/        # DTOs and interfaces
│   ├── FinMel.Application/      # Use cases and services
│   ├── FinMel.Infrastructure/   # Data access and external services
│   ├── FinMel.Web/             # Web API controllers
│   ├── FinMel.AppHost/         # .NET Aspire orchestration
│   └── FinMel.ServiceDefaults/ # Aspire shared configuration
├── tests/                      # Test projects mirror src structure
└── PROJECT-GUIDELINES.md       # Detailed development guide
```

## Technology Stack

- **.NET 9** - Core framework
- **ASP.NET Core** - Web API
- **.NET Aspire** - Orchestration and containerization
- **xUnit** - Testing framework
- **Clean Architecture** - Architectural pattern
- **Result Pattern** - Error handling without exceptions

## Quick Reference

For detailed implementation guidance, see:

- **[PROJECT-GUIDELINES.md](PROJECT-GUIDELINES.md)** - Complete development guide
- **[.copilot-instructions.md](.copilot-instructions.md)** - AI coding guidelines
