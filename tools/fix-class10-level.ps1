# Fix ClassLevel for Class 10 questions
$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$username = "schooladmin"
$password = "India@12345"

$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=False;"

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$connection.Open()

$sql = "UPDATE ExamQuestions SET ClassLevel = 10 WHERE ClassName LIKE '%Class 10%' OR ClassName LIKE '%10th%'"
$command = New-Object System.Data.SqlClient.SqlCommand($sql, $connection)
$rows = $command.ExecuteNonQuery()

Write-Host "Updated $rows rows to ClassLevel=10" -ForegroundColor Green
$connection.Close()
