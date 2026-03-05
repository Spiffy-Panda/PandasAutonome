$dir = "samples\valley\autonomes"
$count = 0
Get-ChildItem "$dir\*.json" | ForEach-Object {
    $raw = [System.IO.File]::ReadAllText($_.FullName)
    $obj = $raw | ConvertFrom-Json

    if ($obj.properties.PSObject.Properties.Name -contains 'starvation') {
        $obj.properties.PSObject.Properties.Remove('starvation')
        $json = $obj | ConvertTo-Json -Depth 10
        # Write without BOM
        [System.IO.File]::WriteAllText($_.FullName, $json, (New-Object System.Text.UTF8Encoding $false))
        Write-Host "Removed starvation: $($_.Name)"
        $count++
    } else {
        Write-Host "No starvation: $($_.Name)"
    }
}
Write-Host "Done. Removed starvation from $count files."
