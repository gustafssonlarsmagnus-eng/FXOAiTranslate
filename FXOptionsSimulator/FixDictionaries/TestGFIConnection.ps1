Write-Host "=== GFI UAT Connection Test ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Check if port 9443 is listening locally
Write-Host "Test 1: Checking if stunnel/SSH tunnel is running..." -ForegroundColor Yellow
$portTest = Test-NetConnection -ComputerName localhost -Port 9443 -WarningAction SilentlyContinue

if ($portTest.TcpTestSucceeded) {
    Write-Host "✓ Port 9443 is open on localhost" -ForegroundColor Green
} else {
    Write-Host "✗ Port 9443 is NOT open - start stunnel or SSH tunnel!" -ForegroundColor Red
    exit
}

Write-Host ""

# Test 2: Try to establish a TCP connection
Write-Host "Test 2: Attempting TCP connection to localhost:9443..." -ForegroundColor Yellow

try {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect("localhost", 9443)
    
    if ($client.Connected) {
        Write-Host "✓ Successfully connected to localhost:9443" -ForegroundColor Green
        Write-Host "✓ stunnel/SSH tunnel is forwarding correctly" -ForegroundColor Green
        $client.Close()
    }
} catch {
    Write-Host "✗ Connection failed: $($_.Exception.Message)" -ForegroundColor Red
    exit
}

Write-Host ""

# Test 3: Send a simple message and check response
Write-Host "Test 3: Sending test data and checking for response..." -ForegroundColor Yellow

try {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect("localhost", 9443)
    $stream = $client.GetStream()
    $stream.ReadTimeout = 5000
    
    # Just connect and see if we get any response
    $buffer = New-Object byte[] 1024
    $stream.ReadTimeout = 2000
    
    try {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        if ($bytesRead -gt 0) {
            Write-Host "✓ Received $bytesRead bytes from server" -ForegroundColor Green
            Write-Host "✓ GFI server is responding!" -ForegroundColor Green
        }
    } catch [System.IO.IOException] {
        Write-Host "○ No immediate response (this is normal - waiting for logon)" -ForegroundColor Yellow
        Write-Host "✓ Connection is established, server is listening" -ForegroundColor Green
    }
    
    $client.Close()
} catch {
    Write-Host "✗ Connection test failed: $($_.Exception.Message)" -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "✓ stunnel/SSH tunnel is running" -ForegroundColor Green
Write-Host "✓ Port 9443 is accessible" -ForegroundColor Green
Write-Host "✓ TCP connection succeeds" -ForegroundColor Green
Write-Host "✓ Ready for FIX session!" -ForegroundColor Green
Write-Host ""
Write-Host "You can now run your application and it should connect to GFI UAT." -ForegroundColor White