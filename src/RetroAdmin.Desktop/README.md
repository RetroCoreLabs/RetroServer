# RetroServeUI - RetroFS Server Management UI

RetroServeUI is an AvaloniaUI-based graphical user interface for managing the RetroFS server. It provides a comprehensive dashboard for monitoring, configuring, and administering all aspects of the RetroFS server.

## Features

### Server Control
- **Start/Stop Server**: Control the RetroFS server directly from the UI
- **Real-time Status**: Live server status indicator showing running/stopped state
- **Automatic Initialization**: Automatically creates default admin user and configuration

### Dashboard
- Server information overview
- Quick access to all management functions
- System status at a glance

### TNFS Monitor
- **Active Connection Tracking**: Real-time display of active TNFS connections
- **Request Statistics**: Monitor total requests and data transfer
- **Performance Graphs**: Visual charts showing:
  - Requests over time (12h, 1w, 1m timeframes)
  - Data transfer in MB/GB
  - Files sent and received
- **Time Range Selection**: View statistics for last 12 hours, 1 week, or 1 month

### Volume Management
- List mounted volumes
- View volume details (type, path, mount status)
- Manage disk image mounts

### User & Group Management (RetroIDP)
- **User Administration**:
  - Create new users with username/password
  - View existing users and their group memberships
  - Manage user permissions
- **Group Management**:
  - Create and manage user groups
  - View group members
  - Assign group permissions

### Log Viewer
- **Real-time Logs**: Live streaming of server logs
- **Filtering**: Filter logs by level (Info, Warning, Error)
- **Search**: Search through log messages
- **Clear History**: Clear old log entries (keeps last 1000)

### Firewall Configuration
- Enable/disable firewall
- View and manage firewall rules
- IP allow/block lists
- Network CIDR rules

### Server Settings
- **TNFS Configuration**:
  - Root directory path
  - UDP/TCP enable/disable toggles
  - Port configuration (UDP and TCP)
- **Backend Selection**: Local or Azure Blob Storage

## Color Scheme

The UI uses the official RetroFS color scheme based on the project logo:

- **Primary Green** (#00FF00): Accent color, success states, active elements
- **Orange** (#FFA500): Secondary accent, warnings, highlights
- **Dark Blue** (#1A1F2E): Main background
- **Navy Blue** (#0F1419): Alternate background, sidebar
- **White/Light Gray**: Text and borders

## Architecture

### Technology Stack
- **Framework**: .NET 9.0
- **UI Framework**: AvaloniaUI 11.3.6
- **MVVM**: CommunityToolkit.Mvvm 8.2.1
- **Charts**: LiveChartsCore.SkiaSharpView.Avalonia 2.0.0-rc4.5
- **Hosting**: Microsoft.Extensions.Hosting 9.0.0

### Project Structure
```
RetroServeUI/
├── Services/
│   └── RetroServerHost.cs          # Server hosting and lifecycle management
├── ViewModels/
│   ├── MainWindowViewModel.cs      # Main window and navigation
│   ├── DashboardViewModel.cs       # Dashboard view
│   ├── TnfsMonitorViewModel.cs     # TNFS monitoring with charts
│   ├── VolumesViewModel.cs         # Volume management
│   ├── UsersViewModel.cs           # User/group administration
│   ├── LogsViewModel.cs            # Log viewer
│   ├── FirewallViewModel.cs        # Firewall configuration
│   └── SettingsViewModel.cs        # Server settings
├── Views/
│   ├── MainWindow.axaml            # Main window with navigation
│   ├── DashboardView.axaml
│   ├── TnfsMonitorView.axaml       # Charts and statistics
│   ├── VolumesView.axaml
│   ├── UsersView.axaml
│   ├── LogsView.axaml
│   ├── FirewallView.axaml
│   └── SettingsView.axaml
└── Styles/
    └── ColorScheme.axaml           # RetroFS color palette
```

### Server Integration

RetroServeUI embeds the RetroFS server using the same configuration and services as the console-based `RetroServer` project:

- **RetroIDP**: User authentication and authorization
- **Firewall**: IP filtering and rate limiting
- **Security**: Audit logging and security events
- **Rate Limiting**: Per-user, per-IP, and server-wide limits
- **TNFS Protocol**: Multi-transport (UDP/TCP) support
- **Backend Support**: Local filesystem or Azure Blob Storage

### Data Flow

1. **Server Startup**: `RetroServerHost` initializes all services
2. **Event Handling**: Server events are routed to ViewModels
3. **Real-time Updates**: UI updates automatically via data binding
4. **Statistics**: Chart data is updated based on server metrics
5. **Logging**: Server logs are captured and displayed in real-time

## Running the Application

### From Visual Studio / Rider
1. Set `RetroServeUI` as startup project
2. Press F5 to run

### From Command Line
```powershell
cd E:\Dev\Ronny\RetroFS\src\RetroServeUI
dotnet run
```

### Building
```powershell
cd E:\Dev\Ronny\RetroFS\src\RetroServeUI
dotnet build
```

## Default Configuration

On first run, RetroServeUI will:

1. Create RetroFS directory in `Documents/RetroFS`
2. Initialize TNFS configuration (ports 16384 UDP/TCP)
3. Create default admin user:
   - Username: `admin`
   - Password: `retroadmin`
   - **⚠️ Change this password immediately!**
4. Set up firewall with default rules
5. Create audit log directory

## Future Enhancements

- Real-time connection list with user details
- Volume mount/unmount operations
- User permission editor with RBAC controls
- Advanced firewall rule builder
- Export logs and statistics
- Configuration import/export
- Server performance metrics
- Disk usage visualization
- Session management
- Remote server management

## Full Path

This file is located at: `E:\Dev\Ronny\RetroFS\src\RetroServeUI\README.md`
