# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

咏慕投影 (CanvasCast) is a WPF-based presentation application specialized for slide creation, editing, and projection. It combines image/media management with slide editing capabilities and Bible text features.

## Build and Development Commands

### Building the Application
```bash
# Build debug version
dotnet build ImageColorChanger.sln -c Debug

# Build release version (includes resource packing)
dotnet build ImageColorChanger.sln -c Release

# Build release with resource packing only
dotnet build ImageColorChanger.csproj -c Release -t:PackResources
```

### Resource Packing
The application uses a custom PAK (Package) file format for resources:
- Resources are automatically packed after each build
- Tool location: `BuildTools/PackResources.csproj`
- Output: `$(OutDir)Resources.pak`
- Command: `dotnet BuildTools\bin\PackResources.dll . "$(OutDir)Resources.pak"`

### Database Operations
```bash
# Database migrations (if needed)
dotnet ef database update

# Generate migration
dotnet ef migrations add MigrationName
```

### Testing
No specific test framework configured. Manual testing through the application UI.

## Architecture Overview

### Core Architecture
- **Framework**: WPF (.NET 8.0 Windows)
- **Database**: SQLite with Entity Framework Core 8.0
- **UI Framework**: Material Design Themes + custom controls
- **Image Processing**: SkiaSharp with GPU acceleration
- **Video Playback**: LibVLCSharp

### Key Architectural Patterns
1. **Database-First Architecture**: Uses Entity Framework Core with SQLite
2. **Manager Pattern**: Business logic separated into manager classes
3. **MVVM with Code-Behind**: Mix of MVVM patterns and traditional code-behind
4. **Service Pattern**: Dependency injection with service collection

### Main Application Structure
```
┌─────────────────────────────────────────────┐
│              UI层 (WPF)                     │
│  MainWindow.xaml + partial classes         │
├─────────────────────────────────────────────┤
│             业务逻辑层 (Managers)           │
│  TextProjectManager, ImportManager, etc.   │
├─────────────────────────────────────────────┤
│              数据模型层 (Models)            │
│  TextProject, Slide, TextElement, etc.     │
├─────────────────────────────────────────────┤
│           数据访问层 (Entity Framework)      │
│         CanvasDbContext (SQLite)           │
└─────────────────────────────────────────────┘
```

## Key Components and Files

### Main UI Structure
- **MainWindow.xaml**: Primary window with three work modes (File/Slide/Bible)
- **MainWindow.TextEditor.cs**: Text editing and button event logic (primary file for UI interactions)
- **MainWindow.*.cs**: Multiple partial classes for different functionality areas

### Core Data Models
- **TextProject**: Complete slide presentation project
- **Slide**: Individual slide page with background, text elements
- **TextElement**: Editable text boxes with rich formatting
- **MediaFile**: Media file management (images, videos)
- **BibleVerse**: Bible text data for religious content

### Key Managers
- **TextProjectManager**: Slide project CRUD operations
- **ImportManager**: File/folder import functionality
- **ImageSaveManager**: Image processing and saving
- **VideoPlayerManager**: Video playback control

### Specialized UI Controls
- **DraggableTextBox**: Draggable text elements on canvas
- **RichTextEditor**: Rich text editing with formatting
- **BiblePinyinHintControl**: Pinyin input for Bible text

### Database Context
- **CanvasDbContext**: Main EF Core context for slide data
- **BibleDbContext**: Separate context for Bible data

## Work Modes

The application supports three distinct work modes:

1. **File Mode**: Media file management and browsing
2. **Slide Mode**: Slide creation and editing (primary editing mode)
3. **Bible Mode**: Bible text display and editing

## Key Features

### Slide Editing
- Rich text editing with formatting options
- Background images and colors
- Split-screen mode (2x2 grid)
- Symmetric copying (horizontal/vertical)
- Drag-and-drop text positioning

### Projection System
- Dual-screen projection support
- Projection lock/unlock functionality
- Real-time sync with editing
- Keyframe-based animation system

### Resource Management
- PAK file system for bundled resources
- Image processing and optimization
- Thumbnail generation
- Resource cleanup on build

### Bible Integration
- Multiple Bible versions support
- Pinyin input system
- Bible search functionality
- Verse navigation

## Development Guidelines

### Button Event Pattern
Most button click events follow this pattern in `MainWindow.TextEditor.cs`:
```csharp
private async void Btn[Action]_Click(object sender, RoutedEventArgs e)
{
    // 1. State validation
    if (_currentTextProject == null) return;

    // 2. User confirmation for destructive operations
    if (isDestructive) {
        var result = WpfMessageBox.Show("确认操作?", "警告",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
    }

    // 3. Business logic execution
    await ExecuteBusinessLogic();

    // 4. Database persistence
    await _dbContext.SaveChangesAsync();

    // 5. UI state update
    UpdateUIState();
    LoadSlideList();
}
```

### Database Operations
- Always use `async/await` for database operations
- Call `SaveChangesAsync()` after modifications
- Use `LoadSlideList()` to refresh UI after data changes

### Resource Management
- Images are processed through `ImageProcessor`
- Use `PakManager` for resource access
- Resources are automatically packed on build

### Projection System
- Check `_isProjectionLocked` before updating projection
- Use `ProjectionManager` for projection updates
- Handle multi-screen scenarios with `WpfScreenHelper`

## File Organization

### Key Directories
- `Database/Models/`: Entity Framework models
- `Managers/`: Business logic managers
- `Services/`: Service implementations with interfaces
- `UI/Controls/`: Custom WPF controls
- `Core/`: Core utilities and configurations
- `Utils/`: Helper utilities
- `BuildTools/`: Build-time tools (PAK packer)

### Important Files
- `ImageColorChanger.csproj`: Main project file with build configuration
- `App.xaml.cs`: Application entry point and DI setup
- `ServiceCollectionExtensions.cs`: Dependency injection configuration
- `幻灯片架构与按钮逻辑文档.md`: Detailed Chinese documentation

## Common Development Tasks

### Adding New Button Functionality
1. Add button to `MainWindow.xaml`
2. Create event handler in `MainWindow.TextEditor.cs`
3. Follow the standard button event pattern
4. Update UI state and refresh lists as needed

### Database Schema Changes
1. Modify model classes in `Database/Models/`
2. Generate EF Core migration
3. Update database context if needed
4. Test with existing data

### Adding New Features
1. Consider if it fits in existing manager or needs new one
2. Create appropriate models if database storage needed
3. Add UI controls if user-facing
4. Update projection system if relevant

## Build Configuration

### Release Build Optimizations
- Unneeded dependencies are automatically removed
- Satellite assemblies are disabled
- Debug symbols are not generated
- Language resource packs are cleaned
- 32-bit libraries are removed from 64-bit builds

### Resource Packing
- Automatic on each build
- Excludes source files, only includes needed resources
- PAK file is included in output and publish

## Debugging and Troubleshooting

### Common Issues
- **Resource Loading**: Check PAK file generation
- **Database Issues**: Verify SQLite file permissions
- **Projection Problems**: Check multi-screen configuration
- **Performance**: Monitor image processing and thumbnail generation

### Log Locations
- Application logs in output directory
- Database logs via EF Core logging
- Build logs in Visual Studio output window