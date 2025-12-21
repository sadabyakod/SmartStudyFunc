$cs = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;Connection Timeout=30;"
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()
Write-Host "[OK] Connected"

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = 'EvaluationAuditLog'"
$exists = $cmd.ExecuteScalar()
Write-Host "Table exists: $exists"

if ($exists -eq 1) {
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('EvaluationAuditLog')"
    $cols = $cmd2.ExecuteScalar()
    Write-Host "Columns: $cols"
    
    $cmd3 = $conn.CreateCommand()
    $cmd3.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('EvaluationAuditLog')"
    $idxs = $cmd3.ExecuteScalar()
    Write-Host "Indexes: $idxs"
}

$conn.Close()
Write-Host "[SUCCESS] Verification complete"
