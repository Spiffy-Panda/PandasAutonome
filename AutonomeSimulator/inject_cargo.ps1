$dir = "samples/valley/autonomes"
$count = 0

Get-ChildItem $dir -Filter "*.json" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw

    # Skip non-embodied (org) profiles
    if ($content -match '"embodied"\s*:\s*false') { return }

    # Skip if already has trade_goods
    if ($content -match 'trade_goods_food') { return }

    # Parse as JSON, add properties, write back
    $json = $content | ConvertFrom-Json

    # Add cargo properties
    $json.properties | Add-Member -NotePropertyName "trade_goods_food" -NotePropertyValue ([PSCustomObject]@{
        id = "trade_goods_food"; value = 0; min = 0; max = 5; decayRate = 0
    }) -Force

    $json.properties | Add-Member -NotePropertyName "trade_goods_ore" -NotePropertyValue ([PSCustomObject]@{
        id = "trade_goods_ore"; value = 0; min = 0; max = 5; decayRate = 0
    }) -Force

    $json.properties | Add-Member -NotePropertyName "trade_goods_tools" -NotePropertyValue ([PSCustomObject]@{
        id = "trade_goods_tools"; value = 0; min = 0; max = 5; decayRate = 0
    }) -Force

    # Write back with nice formatting
    $json | ConvertTo-Json -Depth 10 | Set-Content $_.FullName -Encoding UTF8
    $count++
    Write-Host "Updated: $($_.Name)"
}

Write-Host "`nInjected cargo properties into $count NPC profiles"
