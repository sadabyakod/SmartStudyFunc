# PowerShell script to run SQL migration
# Usage: .\run-migration.ps1 -Server "your-server.database.windows.net" -Database "SmartStudy" -Username "your-username" -Password "your-password"

param(
    [Parameter(Mandatory=$true)]
    [string]$Server,
    
    [Parameter(Mandatory=$true)]
    [string]$Database,
    
    [Parameter(Mandatory=$true)]
    [string]$Username,
    
    [Parameter(Mandatory=$true)]
    [string]$Password
)

$sqlFile = "sql\add-metadata-columns.sql"

if (-not (Test-Path $sqlFile)) {
    Write-Host "Error: SQL file not found at $sqlFile" -ForegroundColor Red
    exit 1
}

Write-Host "Running migration on database: $Database" -ForegroundColor Cyan
Write-Host "Server: $Server" -ForegroundColor Gray
Write-Host ""

try {
    # Build connection string
    $connectionString = "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;Encrypt=True;TrustServerCertificate=False;"
    
    # Read SQL script
    $sqlScript = Get-Content $sqlFile -Raw
    
    # Split by GO statements
    $batches = $sqlScript -split '\r?\nGO\r?\n'
    
    # Load SQL Client
    Add-Type -Path "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\*\System.Data.SqlClient.dll" -ErrorAction SilentlyContinue
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    foreach ($batch in $batches) {
        $batch = $batch.Trim()
        if ($batch.Length -eq 0) { continue }
        
        Write-Host "Executing batch..." -ForegroundColor Yellow
        $command = $connection.CreateCommand()
        $command.CommandText = $batch
        $command.ExecuteNonQuery() | Out-Null
        Write-Host "✓ Batch executed successfully" -ForegroundColor Green
    }
    
    $connection.Close()
    
    Write-Host ""
    Write-Host "✓ Migration completed successfully!" -ForegroundColor Green
    
} catch {
    Write-Host ""
    Write-Host "✗ Migration failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
