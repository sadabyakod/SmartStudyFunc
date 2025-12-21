# Add ClassLevel column to ExamQuestions table

$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$username = "schooladmin"
$password = "India@12345"

$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=False;"

Write-Host "`n=== Adding ClassLevel Column ===" -ForegroundColor Cyan

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    Write-Host "Connected to database" -ForegroundColor Green
    
    # Step 1: Add column
    Write-Host "`nStep 1: Adding ClassLevel column..." -ForegroundColor Yellow
    $addColumnSql = "ALTER TABLE ExamQuestions ADD ClassLevel INT NULL;"
    $command = New-Object System.Data.SqlClient.SqlCommand($addColumnSql, $connection)
    $command.CommandTimeout = 60
    $command.ExecuteNonQuery() | Out-Null
    Write-Host "[OK] Column added" -ForegroundColor Green
    
    # Step 2: Update values
    Write-Host "`nStep 2: Populating ClassLevel values..." -ForegroundColor Yellow
    $updateSql = @"
UPDATE ExamQuestions
SET ClassLevel = CASE
    WHEN ClassName LIKE '%1st%' OR ClassName LIKE '%Class 1%' THEN 1
    WHEN ClassName LIKE '%2nd%' OR ClassName LIKE '%Class 2%' THEN 2
    WHEN ClassName LIKE '%3rd%' OR ClassName LIKE '%Class 3%' THEN 3
    WHEN ClassName LIKE '%4th%' OR ClassName LIKE '%Class 4%' THEN 4
    WHEN ClassName LIKE '%5th%' OR ClassName LIKE '%Class 5%' THEN 5
    WHEN ClassName LIKE '%6th%' OR ClassName LIKE '%Class 6%' THEN 6
    WHEN ClassName LIKE '%7th%' OR ClassName LIKE '%Class 7%' THEN 7
    WHEN ClassName LIKE '%8th%' OR ClassName LIKE '%Class 8%' THEN 8
    WHEN ClassName LIKE '%9th%' OR ClassName LIKE '%Class 9%' THEN 9
    WHEN ClassName LIKE '%10th%' OR ClassName LIKE '%Class 10%' THEN 10
    WHEN ClassName LIKE '%11th%' OR ClassName LIKE '%1st PUC%' OR ClassName LIKE '%Class 11%' THEN 11
    WHEN ClassName LIKE '%12th%' OR ClassName LIKE '%2nd PUC%' OR ClassName LIKE '%Class 12%' THEN 12
    ELSE 10
END
WHERE ClassLevel IS NULL;
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($updateSql, $connection)
    $command.CommandTimeout = 60
    $rowsAffected = $command.ExecuteNonQuery()
    
    Write-Host "`n[SUCCESS] Migration completed" -ForegroundColor Green
    Write-Host "Rows updated: $rowsAffected" -ForegroundColor Cyan
    
    # Verify the column exists
    $verifyQuery = @"
SELECT TOP 5
    Id,
    ClassName,
    ClassLevel,
    Subject,
    QuestionText
FROM ExamQuestions
ORDER BY CreatedAt DESC
"@
    
    $verifyCommand = New-Object System.Data.SqlClient.SqlCommand($verifyQuery, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($verifyCommand)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    Write-Host "`nVerification - Recent questions with ClassLevel:" -ForegroundColor Yellow
    $dataset.Tables[0] | Format-Table -Property ClassName, ClassLevel, Subject -AutoSize
    
    $connection.Close()
    Write-Host "`n[COMPLETE] ClassLevel column added successfully" -ForegroundColor Green
    
} catch {
    Write-Host "`n[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
}
