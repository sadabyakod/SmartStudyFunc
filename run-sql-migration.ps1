# Run SQL Migration Scripts

$connectionString = "Server=tcp:smartstudysqlsrv.database.windows.net,1433;Initial Catalog=smartstudydb;Persist Security Info=False;User ID=schooladmin;Password=India@12345;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "RUNNING DATABASE MIGRATIONS" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# SQL scripts to run in order
$scripts = @(
    "C:\SmartStudyFunc\sql\03_CreateWrittenSubmissionsTables.sql",
    "C:\SmartStudyFunc\sql\06_FixWrittenSubmissionsSchema.sql"
)

foreach ($scriptPath in $scripts) {
    if (Test-Path $scriptPath) {
        Write-Host "`nRunning: $(Split-Path $scriptPath -Leaf)" -ForegroundColor Cyan
        
        try {
            $sqlContent = Get-Content $scriptPath -Raw
            
            # Remove GO statements and split into batches
            $batches = $sqlContent -split '\r?\nGO\r?\n' | Where-Object { $_.Trim() -ne '' }
            
            $connection = New-Object System.Data.SqlClient.SqlConnection
            $connection.ConnectionString = $connectionString
            $connection.Open()
            
            $totalRows = 0
            foreach ($batch in $batches) {
                if ($batch.Trim() -ne '') {
                    $command = $connection.CreateCommand()
                    $command.CommandText = $batch
                    $command.CommandTimeout = 120
                    
                    try {
                        $result = $command.ExecuteNonQuery()
                        if ($result -gt 0) {
                            $totalRows += $result
                        }
                    } catch {
                        # Ignore PRINT statements and non-query commands
                        if ($_.Exception.Message -notlike "*not return*") {
                            Write-Host "  Warning: $($_.Exception.Message)" -ForegroundColor Yellow
                        }
                    }
                }
            }
            
            $connection.Close()
            
            Write-Host "Success! ($totalRows rows affected)" -ForegroundColor Green
            
        } catch {
            Write-Host "Error: $_" -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor Red
        }
    } else {
        Write-Host "File not found: $scriptPath" -ForegroundColor Red
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "MIGRATION COMPLETE!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
