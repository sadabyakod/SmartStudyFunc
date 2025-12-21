# Diagnose V2 Evaluation Error
# Shows exactly what's failing

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$functionKey = "YOUR_FUNCTION_KEY_HERE"

Write-Host "`n=== V2 Error Diagnosis ===" -ForegroundColor Cyan

# Test with minimal payload
$payload = @{
    examId = "TEST-001"
    questionId = "04d3b720-74a8-4b82-9500-fd56feca8d87"
    studentAnswerText = "x = 5"
} | ConvertTo-Json

Write-Host "`nSending request..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod `
        -Uri "$baseUrl/api/answers/evaluate/v2" `
        -Method Post `
        -Body $payload `
        -ContentType "application/json" `
        -Headers @{"x-functions-key"=$functionKey} `
        -TimeoutSec 30 `
        -ErrorVariable restError

    Write-Host "`n[SUCCESS] Response:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 10 | Write-Host

} catch {
    Write-Host "`n[FAILED] Error Details:" -ForegroundColor Red
    
    Write-Host "`nStatus Code:" -ForegroundColor Yellow
    Write-Host $_.Exception.Response.StatusCode.value__
    
    Write-Host "`nException Message:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message
    
    # Try to get response body
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd()
        
        Write-Host "`nResponse Body:" -ForegroundColor Yellow
        Write-Host $responseBody
        
        # Try to parse as JSON
        try {
            $errorJson = $responseBody | ConvertFrom-Json
            Write-Host "`nParsed Error:" -ForegroundColor Magenta
            $errorJson | ConvertTo-Json -Depth 5 | Write-Host
        } catch {
            Write-Host "Could not parse as JSON" -ForegroundColor Gray
        }
    }
    
    Write-Host "`nFull Exception:" -ForegroundColor Yellow
    Write-Host $_.Exception | Format-List -Force | Out-String
}

Write-Host "`n=== Analysis Complete ===" -ForegroundColor Cyan
Write-Host "Check the output above for the exact error message from Azure"
