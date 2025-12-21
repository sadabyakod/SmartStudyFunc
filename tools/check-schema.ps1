# Check ExamQuestions table schema

$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$username = "schooladmin"
$password = "India@12345"

$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=False;"

Write-Host "`n=== ExamQuestions Schema Check ===" -ForegroundColor Cyan

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    # Get column information
    $query = @"
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'ExamQuestions'
ORDER BY ORDINAL_POSITION
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    Write-Host "`nCurrent Columns:" -ForegroundColor Yellow
    $dataset.Tables[0] | Format-Table -AutoSize
    
    # Check if ClassLevel exists
    $hasClassLevel = $dataset.Tables[0].Rows | Where-Object { $_.COLUMN_NAME -eq "ClassLevel" }
    
    if ($hasClassLevel) {
        Write-Host "`n[OK] ClassLevel column exists" -ForegroundColor Green
    } else {
        Write-Host "`n[MISSING] ClassLevel column NOT found" -ForegroundColor Red
        Write-Host "Need to add: ClassLevel INT NULL" -ForegroundColor Yellow
    }
    
    $connection.Close()
    
} catch {
    Write-Host "`n[ERROR] $($_.Exception.Message)" -ForegroundColor Red
}
