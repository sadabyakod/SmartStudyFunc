#!/usr/bin/env pwsh
# ============================================================================
# Insert Sample Exam Questions for SAMPLE-EXAM-001
# ============================================================================
# This script inserts 3 exam questions into the database for testing
# the written answer evaluation pipeline.
# ============================================================================

Write-Host "Inserting exam questions for SAMPLE-EXAM-001..." -ForegroundColor Yellow

$server = "smartstudysqlsrv.database.windows.net"
$database = "smartstudydb"
$sqlFile = "sql/InsertSampleExamQuestions.sql"

# Method 1: Using Azure CLI (recommended)
Write-Host "`nMethod 1: Using Azure CLI..." -ForegroundColor Cyan
$result = az sql db execute `
    --server $server `
    --database $database `
    --file $sqlFile `
    --resource-group rg-smartstudy-dev `
    2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Questions inserted successfully using Azure CLI!" -ForegroundColor Green
    exit 0
}

# Method 2: Using sqlcmd (fallback)
Write-Host "`nMethod 1 failed. Trying Method 2: Using sqlcmd..." -ForegroundColor Yellow
Write-Host "Enter SQL Server admin password when prompted." -ForegroundColor Cyan

sqlcmd -S $server `
    -d $database `
    -U sqladminuser `
    -i $sqlFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✓ Questions inserted successfully using sqlcmd!" -ForegroundColor Green
} else {
    Write-Host "`n✗ Failed to insert questions. Please run manually:" -ForegroundColor Red
    Write-Host "  az sql db execute --server $server --database $database --file $sqlFile --resource-group rg-smartstudy-dev" -ForegroundColor Yellow
}
