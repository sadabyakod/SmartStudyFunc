# Check Keywords field content
$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$username = "schooladmin"
$password = "India@12345"

$connectionString = "Server=$server;Database=$database;User Id=$username;Password=$password;Encrypt=True;TrustServerCertificate=False;"

$connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
$connection.Open()

$sql = "SELECT Id, Keywords, QuestionType FROM ExamQuestions WHERE Id = '04d3b720-74a8-4b82-9500-fd56feca8d87'"
$command = New-Object System.Data.SqlClient.SqlCommand($sql, $connection)
$reader = $command.ExecuteReader()

while ($reader.Read()) {
    Write-Host "Id:" $reader["Id"]
    Write-Host "Keywords:" $reader["Keywords"]
    Write-Host "QuestionType:" $reader["QuestionType"]
}

$connection.Close()
