#Requires -Version 5.1
<#
.SYNOPSIS
    OpenTelemetry Demo - Order Service Load Generator
    
.DESCRIPTION
    Generates configurable load against the OrderService endpoint to populate traces,
    metrics, logs, and dashboard in the OTEL PoC. Supports two modes: 'happy' for
    basic demo flow and 'latency' for pressure testing against order service latency alert.
    
.PARAMETER BaseUrl
    Base URL of the OrderService (default: http://localhost:8080)
    
.PARAMETER Count
    Total number of orders to generate (required)
    
.PARAMETER Mode
    Load generation mode: 'happy' (sequential) or 'latency' (concurrent)
    Default: 'happy'
    
.PARAMETER Concurrency
    Number of concurrent requests in 'latency' mode.
    Default: 1 (ignored in 'happy' mode)
    
.PARAMETER TimeoutSeconds
    HTTP request timeout in seconds. Default: 10
    
.PARAMETER PauseMs
    Optional pause between requests or batches in milliseconds. Default: 0
    
.EXAMPLE
    # Generate 20 orders in happy mode
    .\generate-orders.ps1 -Count 20
    
.EXAMPLE
    # Generate 120 orders with 6 concurrent requests
    .\generate-orders.ps1 -Count 120 -Mode latency -Concurrency 6
    
.EXAMPLE
    # Generate 50 orders against custom OrderService endpoint
    .\generate-orders.ps1 -BaseUrl http://example.com:8080 -Count 50
#>

param(
    [string]$BaseUrl = "http://localhost:8080",
    [Parameter(Mandatory=$true)]
    [int]$Count,
    [ValidateSet("happy", "latency")]
    [string]$Mode = "happy",
    [int]$Concurrency = 1,
    [int]$TimeoutSeconds = 10,
    [int]$PauseMs = 0
)

# ============================================================================
# PARAMETER VALIDATION (T2)
# ============================================================================

if ($Count -le 0) {
    Write-Error "Count must be greater than zero"
    exit 1
}

if ($Concurrency -le 0) {
    Write-Error "Concurrency must be greater than zero"
    exit 1
}

if ($TimeoutSeconds -le 0) {
    Write-Error "TimeoutSeconds must be greater than zero"
    exit 1
}

if ($PauseMs -lt 0) {
    Write-Error "PauseMs cannot be negative"
    exit 1
}

# ============================================================================
# PAYLOAD BUILDER (T3)
# ============================================================================

function New-OrderRequestBody {
    param(
        [string]$Mode,
        [int]$Sequence
    )
    
    $guid = [guid]::NewGuid().ToString().Substring(0, 8)
    $description = "$Mode-$($Sequence)-$guid"
    
    $payload = @{
        description = $description
    } | ConvertTo-Json -Compress
    
    return $payload
}

# ============================================================================
# HTTP EXECUTOR (T4)
# ============================================================================

function Invoke-OrderRequest {
    param(
        [string]$TargetUrl,
        [string]$Payload,
        [int]$Sequence,
        [string]$Mode,
        [int]$TimeoutSeconds
    )
    
    $result = [PSCustomObject]@{
        sequence    = $Sequence
        mode        = $Mode
        description = ""
        succeeded   = $false
        statusCode  = 0
        durationMs  = 0
        orderId     = ""
        error       = ""
    }
    
    try {
        # Extract description from payload JSON
        $payloadObj = $Payload | ConvertFrom-Json
        $result.description = $payloadObj.description
        
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        
        $response = Invoke-WebRequest -Uri $TargetUrl `
            -Method POST `
            -ContentType "application/json" `
            -Body $Payload `
            -TimeoutSec $TimeoutSeconds `
            -UseBasicParsing `
            -ErrorAction Stop
        
        $sw.Stop()
        $result.durationMs = [int]$sw.Elapsed.TotalMilliseconds
        $result.statusCode = $response.StatusCode
        $result.succeeded = $true
        
        # Extract orderId if present in response
        if ($null -ne $response.Content) {
            try {
                $responseObj = $response.Content | ConvertFrom-Json
                if ($null -ne $responseObj.id) {
                    $result.orderId = $responseObj.id
                } elseif ($null -ne $responseObj.orderId) {
                    $result.orderId = $responseObj.orderId
                }
            } catch {
                # Silently ignore JSON parsing errors in response
            }
        }
    }
    catch [System.Net.WebException] {
        $sw.Stop()
        $result.durationMs = [int]$sw.Elapsed.TotalMilliseconds
        $result.error = "WebException: $($_.Exception.Message)"
        $result.succeeded = $false
        
        # Try to extract status code from exception
        if ($_.Exception -is [System.Net.WebException]) {
            if ($null -ne $_.Exception.Response) {
                $result.statusCode = [int]$_.Exception.Response.StatusCode
            }
        }
    }
    catch {
        $sw.Stop()
        $result.durationMs = [int]$sw.Elapsed.TotalMilliseconds
        $result.error = $_.Exception.Message
        $result.succeeded = $false
    }
    
    return $result
}

# ============================================================================
# HAPPY MODE (T5)
# ============================================================================

function Invoke-HappyPathLoad {
    param(
        [string]$TargetUrl,
        [int]$Count,
        [int]$TimeoutSeconds,
        [int]$PauseMs
    )
    
    $results = @()
    
    Write-Host "Starting load generation in 'happy' mode ($Count orders)..." -ForegroundColor Cyan
    
    for ($i = 1; $i -le $Count; $i++) {
        $payload = New-OrderRequestBody -Mode "happy" -Sequence $i
        $result = Invoke-OrderRequest -TargetUrl $TargetUrl `
            -Payload $payload `
            -Sequence $i `
            -Mode "happy" `
            -TimeoutSeconds $TimeoutSeconds
        
        $results += $result
        
        if ($result.succeeded) {
            Write-Host "[$i/$Count] OK - $($result.description) ($($result.durationMs)ms)" -ForegroundColor Green
        } else {
            Write-Host "[$i/$Count] FAIL - $($result.description) - $($result.error)" -ForegroundColor Red
        }
        
        if ($PauseMs -gt 0 -and $i -lt $Count) {
            Start-Sleep -Milliseconds $PauseMs
        }
    }
    
    return $results
}

# ============================================================================
# LATENCY MODE (T6)
# ============================================================================

function Invoke-LatencyLoad {
    param(
        [string]$TargetUrl,
        [int]$Count,
        [int]$Concurrency,
        [int]$TimeoutSeconds,
        [int]$PauseMs
    )
    
    $results = @()
    $processedCount = 0
    
    Write-Host "Starting load generation in 'latency' mode ($Count orders, concurrency=$Concurrency)..." -ForegroundColor Cyan
    
    # Create scriptblock that includes all necessary functions for the job
    $jobScriptBlock = {
        param($targetUrl, $count, $concurrency, $timeoutSeconds, $pauseMs, $batchStart, $batchEnd)
        
        # Define functions inside the job context
        function New-OrderRequestBody {
            param([string]$Mode, [int]$Sequence)
            $guid = [guid]::NewGuid().ToString().Substring(0, 8)
            $description = "$Mode-$($Sequence)-$guid"
            $payload = @{ description = $description } | ConvertTo-Json -Compress
            return $payload
        }
        
        function Invoke-OrderRequest {
            param([string]$TargetUrl, [string]$Payload, [int]$Sequence, [string]$Mode, [int]$TimeoutSeconds)
            
            $result = [PSCustomObject]@{
                sequence    = $Sequence
                mode        = $Mode
                description = ""
                succeeded   = $false
                statusCode  = 0
                durationMs  = 0
                orderId     = ""
                error       = ""
            }
            
            try {
                $payloadObj = $Payload | ConvertFrom-Json
                $result.description = $payloadObj.description
                
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                
                $response = Invoke-WebRequest -Uri $TargetUrl `
                    -Method POST `
                    -ContentType "application/json" `
                    -Body $Payload `
                    -TimeoutSec $TimeoutSeconds `
                    -UseBasicParsing `
                    -ErrorAction Stop
                
                $sw.Stop()
                $result.durationMs = [int]$sw.Elapsed.TotalMilliseconds
                $result.statusCode = $response.StatusCode
                $result.succeeded = $true
                
                if ($null -ne $response.Content) {
                    try {
                        $responseObj = $response.Content | ConvertFrom-Json
                        if ($null -ne $responseObj.id) {
                            $result.orderId = $responseObj.id
                        } elseif ($null -ne $responseObj.orderId) {
                            $result.orderId = $responseObj.orderId
                        }
                    } catch { }
                }
            }
            catch {
                $sw.Stop()
                $result.durationMs = [int]$sw.Elapsed.TotalMilliseconds
                $result.error = $_.Exception.Message
                $result.succeeded = $false
            }
            
            return $result
        }
        
        # Execute individual requests
        for ($i = $batchStart; $i -le $batchEnd; $i++) {
            $payload = New-OrderRequestBody -Mode "latency" -Sequence $i
            Invoke-OrderRequest -TargetUrl $targetUrl -Payload $payload -Sequence $i -Mode "latency" -TimeoutSeconds $TimeoutSeconds
        }
    }
    
    # Calculate number of batches
    $batchSize = [math]::Ceiling($Count / $Concurrency)
    
    for ($batch = 0; $batch -lt $Concurrency; $batch++) {
        $batchStart = $batch * $batchSize + 1
        $batchEnd = [math]::Min(($batch + 1) * $batchSize, $Count)
        
        if ($batchStart -le $Count) {
            $job = Start-Job -ScriptBlock $jobScriptBlock `
                -ArgumentList $TargetUrl, $Count, $Concurrency, $TimeoutSeconds, $PauseMs, $batchStart, $batchEnd
            
            $result = Receive-Job -Job $job -Wait
            Remove-Job -Job $job
            
            if ($null -ne $result) {
                $results += @($result)
                foreach ($r in @($result)) {
                    $processedCount++
                    if ($r.succeeded) {
                        Write-Host "[$processedCount/$Count] OK - $($r.description) ($($r.durationMs)ms)" -ForegroundColor Green
                    } else {
                        Write-Host "[$processedCount/$Count] FAIL - $($r.description) - $($r.error)" -ForegroundColor Red
                    }
                }
            }
        }
        
        if ($PauseMs -gt 0 -and ($batch + 1) -lt $Concurrency) {
            Start-Sleep -Milliseconds $PauseMs
        }
    }
    
    return $results
}

# ============================================================================
# SUMMARY REPORTER (T7)
# ============================================================================

function Write-LoadSummary {
    param(
        [object[]]$Results
    )
    
    $totalRequests = $Results.Count
    $successfulRequests = ($Results | Where-Object { $_.succeeded }).Count
    $failedRequests = $totalRequests - $successfulRequests
    
    $validResults = $Results | Where-Object { $_.durationMs -and $_.durationMs -gt 0 }
    
    if ($validResults) {
        $totalDuration = ($validResults | Measure-Object -Property durationMs -Sum).Sum
        $avgDuration = if ($validResults.Count -gt 0) { [int]($totalDuration / $validResults.Count) } else { 0 }
        $minDuration = ($validResults | Measure-Object -Property durationMs -Minimum).Minimum
        $maxDuration = ($validResults | Measure-Object -Property durationMs -Maximum).Maximum
        $totalSeconds = [math]::Round($totalDuration / 1000, 2)
    } else {
        $totalDuration = 0
        $avgDuration = 0
        $minDuration = 0
        $maxDuration = 0
        $totalSeconds = 0
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Load Generation Summary" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Total Requests:     $totalRequests"
    Write-Host "Successful:         $successfulRequests" -ForegroundColor Green
    Write-Host "Failed:             $failedRequests" $(if ($failedRequests -gt 0) { "-ForegroundColor Red" })
    Write-Host ""
    Write-Host "Duration:"
    Write-Host "  Total:            $($totalSeconds)s"
    Write-Host "  Average:          $($avgDuration)ms"
    Write-Host "  Min:              $($minDuration)ms"
    Write-Host "  Max:              $($maxDuration)ms"
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Show failure details if any
    $failures = $Results | Where-Object { -not $_.succeeded }
    if ($failures.Count -gt 0) {
        Write-Host "Failed Requests Details:" -ForegroundColor Yellow
        foreach ($failure in $failures) {
            Write-Host "  [$($failure.sequence)] $($failure.description) - Status: $($failure.statusCode) - Error: $($failure.error)"
        }
        Write-Host ""
    }
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

$targetUrl = "$BaseUrl/orders"

Write-Host "OpenTelemetry Demo - Order Service Load Generator" -ForegroundColor Cyan
Write-Host "Target:     $targetUrl"
Write-Host "Count:      $Count"
Write-Host "Mode:       $Mode"
if ($Mode -eq "latency") {
    Write-Host "Concurrency: $Concurrency"
}
Write-Host ""

$results = @()

try {
    if ($Mode -eq "happy") {
        $results = Invoke-HappyPathLoad -TargetUrl $targetUrl `
            -Count $Count `
            -TimeoutSeconds $TimeoutSeconds `
            -PauseMs $PauseMs
    }
    elseif ($Mode -eq "latency") {
        $results = Invoke-LatencyLoad -TargetUrl $targetUrl `
            -Count $Count `
            -Concurrency $Concurrency `
            -TimeoutSeconds $TimeoutSeconds `
            -PauseMs $PauseMs
    }
    
    # Display summary
    Write-LoadSummary -Results $results
    
    # Determine exit code
    $failureCount = ($results | Where-Object { -not $_.succeeded }).Count
    
    if ($failureCount -eq 0) {
        Write-Host "Load generation completed successfully!" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "Load generation completed with $failureCount failures!" -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Error "Fatal error: $($_.Exception.Message)"
    exit 2
}
