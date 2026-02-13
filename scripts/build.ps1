$ErrorActionPreference = 'Stop'

dotnet restore ..\DfsMonitor.sln
dotnet build ..\DfsMonitor.sln -c Release
dotnet test ..\DfsMonitor.sln -c Release
