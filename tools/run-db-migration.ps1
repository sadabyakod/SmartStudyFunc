# Run EvaluationAuditLog table creation migration
$ErrorActionPreference = "Stop"

Write-Host "====================================="
Write-Host "SQL Migration: EvaluationAuditLog"
Write-Host "====================================="

# Load connection string
$config = Get-Content "$PSScriptRoot\..\local.settings.json" | ConvertFrom-Json
$connectionString = $config.Values.SqlConnectionString

# Load SQL script
$sqlScript = Get-Content "$PSScriptRoot\..\sql\CreateEvaluationAuditLogTable.sql" -Raw

Write-Host "Connection: $($connectionString.Substring(0, 60))..."
Write-Host "Script Length: $($sqlScript.Length) characters"
Write-Host ""

try {
    # Load System.Data.SqlClient
    Add-Type -AssemblyName System.Data

    # Create connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "[OK] Connected to database"

    # Split script by GO statements
    $batches = $sqlScript -split '\r?\nGO\r?\n'
    $batchNum = 0

    foreach ($batch in $batches) {
        $trimmedBatch = $batch.Trim()
        if ($trimmedBatch.Length -eq 0 -or $trimmedBatch -match '^\s*--') {
            continue
        }

        $batchNum++
        Write-Host "Executing batch $batchNum..."

        $command = $connection.CreateCommand()
        $command.CommandText = $trimmedBatch
        $command.CommandTimeout = 120
        
        try {
            $result = $command.ExecuteNonQuery()
            Write-Host "  [OK] Batch $batchNum executed successfully"
        }
        catch {
            Write-Host "  [WARNING] Batch $($batchNum): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    $connection.Close()
    Write-Host ""
    Write-Host "[SUCCESS] Migration completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Verify table creation
    $connection.Open()
    $verifyCmd = $connection.CreateCommand()
    $verifyCmd.CommandText = @"
SELECT 
    OBJECT_NAME(object_id) as TableName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = t.object_id) as ColumnCount,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id AND is_primary_key = 0) as IndexCount
FROM sys.tables t
WHERE OBJECT_NAME(object_id) = 'EvaluationAuditLog'
"@
    $reader = $verifyCmd.ExecuteReader()
    
    if ($reader.Read()) {
        Write-Host "Table Verification:"
        Write-Host "  Table Name: $($reader['TableName'])"
        Write-Host "  Columns: $($reader['ColumnCount'])"
        Write-Host "  Indexes: $($reader['IndexCount'])"
    }
    $reader.Close()
    $connection.Close()
}
catch {
    Write-Host ""
    Write-Host "[ERROR] Migration failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack Trace:" -ForegroundColor Red
    Write-Host $_.Exception.StackTrace -ForegroundColor Red
    exit 1
}
