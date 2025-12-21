$ErrorActionPreference = "Stop"

Write-Host "======================================="
Write-Host " SQL Migration: EvaluationAuditLog"
Write-Host "======================================="

$connectionString = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;Connection Timeout=30;"
$sqlScript = Get-Content "$PSScriptRoot\..\sql\CreateEvaluationAuditLogTable.sql" -Raw

Write-Host "Script length: $($sqlScript.Length) characters"

try {
    Add-Type -AssemblyName System.Data
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "[OK] Connected to Azure SQL Database"
    
    # Split by GO statements
    $batches = $sqlScript -split '(?m)^\s*GO\s*$'
    $batchCount = 0
    
    foreach ($batch in $batches) {
        $trimmed = $batch.Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }
        
        # Skip comment-only batches
        $lines = $trimmed -split "`n" | Where-Object { $_ -notmatch '^\s*(--|$)' }
        if ($lines.Count -eq 0) {
            continue
        }
        
        $batchCount++
        Write-Host "Executing batch $batchCount..."
        
        $command = $connection.CreateCommand()
        $command.CommandText = $trimmed
        $command.CommandTimeout = 120
        
        try {
            $rowsAffected = $command.ExecuteNonQuery()
            Write-Host "  [OK] Batch $batchCount completed ($rowsAffected rows affected)"
        }
        catch {
            Write-Host "  [WARN] Batch $batchCount : $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    
    $connection.Close()
    Write-Host ""
    Write-Host "[SUCCESS] Migration completed!" -ForegroundColor Green
    Write-Host ""
    
    # Verify
    $connection.Open()
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = @"
SELECT 
    t.name as TableName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = t.object_id) as Columns,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id) as Indexes
FROM sys.tables t
WHERE t.name = 'EvaluationAuditLog'
"@
    $reader = $cmd.ExecuteReader()
    
    if ($reader.Read()) {
        Write-Host "Verification:"
        Write-Host "  Table: $($reader['TableName'])"
        Write-Host "  Columns: $($reader['Columns'])"
        Write-Host "  Indexes: $($reader['Indexes'])"
    }
    else {
        Write-Host "[ERROR] Table not found after migration!" -ForegroundColor Red
    }
    
    $reader.Close()
    $connection.Close()
}
catch {
    Write-Host ""
    Write-Host "[ERROR] Migration failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
