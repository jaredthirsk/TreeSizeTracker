# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TreeSizeTracker is a .NET 9 Blazor Server application that monitors disk space usage across configured folders. It scans folders on a schedule or on-demand, tracks size changes over time, and generates diff reports.

Key features:
- Configurable folder monitoring with scan depth control
- SQLite database for storing scan history
- Cron-style scheduling for automatic scans (using NCronTab)
- CSV and text report generation for folder size differences
- Cross-platform support with defaults for Windows and Linux
- Modern UI built with MudBlazor components

## Development Commands

### Building and Running
- `dotnet build` - Build the solution
- `dotnet run --project TreeSizeTracker` - Run the application (serves on https://localhost:7167 and http://localhost:5162)
- `dotnet watch --project TreeSizeTracker` - Run with hot reload for development

### Testing
- `dotnet test` - Run all tests (if test projects are added)

## Architecture

The application uses:
- **Blazor Server** with interactive server-side rendering
- **MudBlazor** for Material Design UI components
- **Entity Framework Core** with SQLite for data persistence
- **NCronTab** for cron expression parsing and scheduling
- **Background services** for scheduled scanning

### Project Structure

- **Models/**: Data models for folder configuration and scan results
- **Data/**: Entity Framework DbContext and database configuration
- **Services/**: 
  - `ConfigurationService`: Manages folder scan configuration (JSON file storage)
  - `FileScannerService`: Performs folder size scanning with configurable depth
  - `ReportingService`: Generates CSV and text diff reports
  - `ScheduledScanService`: Background service for automatic scans
- **Components/**: Blazor components
  - **Layout/**: Application layout with MudBlazor navigation
  - **Pages/**: 
    - `Dashboard.razor`: Overview of disk usage and recent changes
    - `FolderConfig.razor`: Configure folders to monitor
    - `Reports.razor`: View and download generated reports
    - `ScanHistory.razor`: Browse historical scan data

### Data Storage

- **SQLite database** (`treesize.db`): Stores scan history
- **JSON configuration** (`scan-config.json`): Stores folder configuration and schedule
- **Reports directory**: Contains generated CSV and text reports