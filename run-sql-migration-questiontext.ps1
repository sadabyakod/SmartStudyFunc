# Run SQL Migration Script
# Adds QuestionText column to WrittenQuestionEvaluations table

$ErrorActionPreference = "Stop"

Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "DATABASE MIGRATION: Add QuestionText Column" -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan

# Read connection string from local.settings.json
$localSettings = Get-Content "local.settings.json" | ConvertFrom-Json
$connectionString = $localSettings.Values.SqlConnectionString

Write-Host "Connection string loaded" -ForegroundColor Green

# Read SQL script
$sqlScript = Get-Content "sql\05_AddQuestionTextColumn.sql" -Raw

Write-Host "SQL script loaded" -ForegroundColor Green

# Execute SQL script
try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "Connected to database" -ForegroundColor Green
    
    $command = $connection.CreateCommand()
    $command.CommandText = $sqlScript
    $command.CommandTimeout = 60
    
    Write-Host "Executing migration script..." -ForegroundColor Yellow
    
    $reader = $command.ExecuteReader()
    
    # Read all PRINT messages
    do {
        while ($reader.Read()) {
            for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                Write-Host $reader.GetValue($i)
            }
        }
    } while ($reader.NextResult())
    
    $reader.Close()
    $connection.Close()
    
    Write-Host ""
    Write-Host "===============================================================" -ForegroundColor Green
    Write-Host "MIGRATION COMPLETED SUCCESSFULLY" -ForegroundColor Green
    Write-Host "===============================================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "===============================================================" -ForegroundColor Red
    Write-Host "MIGRATION FAILED" -ForegroundColor Red
    Write-Host "===============================================================" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
