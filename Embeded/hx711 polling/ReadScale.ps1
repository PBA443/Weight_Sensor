$port = New-Object System.IO.Ports.SerialPort COM8,9600,None,8,1
$port.Open()

# Create a tiny 1-byte storage bucket
$buffer = New-Object byte[] 1

Write-Host "Reading raw bytes from scale (Press Ctrl+C to stop)..."

while($true) {
    if($port.BytesToRead -gt 0) {
        # Read exactly 1 raw byte into our bucket
        [void]$port.Read($buffer, 0, 1)
        
        # Format that byte as a clean 2-digit Hexadecimal number
        $hexValue = "0x{0:X2}" -f $buffer
        
        # If it matches our sync header, highlight it!
        if ($buffer -eq 0xAA) {
            Write-Host "$hexValue [Header]" -ForegroundColor Green
        } else {
            Write-Host $hexValue
        }
    }
}