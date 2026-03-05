$dir = "samples\valley\autonomes"
Get-ChildItem "$dir\*.json" | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $newBytes = $bytes[3..($bytes.Length - 1)]
        [System.IO.File]::WriteAllBytes($_.FullName, [byte[]]$newBytes)
        Write-Host "Stripped BOM: $($_.Name)"
    } else {
        Write-Host "No BOM: $($_.Name)"
    }
}
Write-Host "Done."
