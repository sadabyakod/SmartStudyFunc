# Check audit log entries
$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$username = "schooladmin"
$password = "India@12345"

$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=False;"

Write-Host "`n=== Audit Log Check ===" -ForegroundColor Cyan

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$connection.Open()

$sql = "SELECT TOP 5 EvaluationId, QuestionId, EngineName, MarksAwarded, ConfidenceScore, EvaluatedAt FROM EvaluationAuditLog ORDER BY EvaluatedAt DESC"
$command = New-Object System.Data.SqlClient.SqlCommand($sql, $connection)
$adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
$dataset = New-Object System.Data.DataSet
$adapter.Fill($dataset) | Out-Null

Write-Host "`nRecent Audit Entries:" -ForegroundColor Yellow
if ($dataset.Tables[0].Rows.Count -eq 0) {
    Write-Host "[NONE] No audit entries found" -ForegroundColor Red
} else {
    $dataset.Tables[0] | Format-Table -AutoSize
}

$connection.Close()
