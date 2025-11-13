# PowerShell script to test textbook upload
# Usage: .\test-upload.ps1 -FilePath "path\to\your\file.pdf"

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [string]$ClassName = "Grade-10",
    [string]$Subject = "Mathematics",
    [string]$Chapter = "Chapter-1",
    [string]$Url = "http://localhost:7071/api/upload/textbook"
)

# Check if file exists
if (-not (Test-Path $FilePath)) {
    Write-Host "Error: File not found at $FilePath" -ForegroundColor Red
    exit 1
}

# Check if file is PDF
if ([System.IO.Path]::GetExtension($FilePath) -ne ".pdf") {
    Write-Host "Error: File must be a PDF" -ForegroundColor Red
    exit 1
}

Write-Host "Uploading textbook..." -ForegroundColor Cyan
Write-Host "  File: $FilePath" -ForegroundColor Gray
Write-Host "  Class: $ClassName" -ForegroundColor Gray
Write-Host "  Subject: $Subject" -ForegroundColor Gray
Write-Host "  Chapter: $Chapter" -ForegroundColor Gray
Write-Host ""

try {
    # Create multipart form data
    $boundary = [System.Guid]::NewGuid().ToString()
    $LF = "`r`n"
    
    # Read file content
    $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
    $fileName = [System.IO.Path]::GetFileName($FilePath)
    
    # Build multipart form data body
    $bodyLines = @(
        "--$boundary",
        "Content-Disposition: form-data; name=`"className`"$LF",
        $ClassName,
        "--$boundary",
        "Content-Disposition: form-data; name=`"subject`"$LF",
        $Subject,
        "--$boundary",
        "Content-Disposition: form-data; name=`"chapter`"$LF",
        $Chapter,
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
        "Content-Type: application/pdf$LF"
    )
    
    $bodyString = $bodyLines -join $LF
    
    # Combine text and binary data
    $encoding = [System.Text.Encoding]::UTF8
    $header = $encoding.GetBytes($bodyString)
    $footer = $encoding.GetBytes("$LF--$boundary--$LF")
    
    # Create final body
    $body = $header + $fileBytes + $footer
    
    # Make request
    $response = Invoke-RestMethod -Uri $Url -Method Post -Body $body -ContentType "multipart/form-data; boundary=$boundary"
    
    # Display result
    Write-Host "✓ Upload successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Yellow
    $response | ConvertTo-Json -Depth 10 | Write-Host
    
} catch {
    Write-Host "✗ Upload failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response: $responseBody" -ForegroundColor Red
    }
    exit 1
}
