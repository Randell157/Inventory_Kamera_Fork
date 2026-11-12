# Recreate the compact, unmodified-value source snapshot from local downloaded data.
param([string]$Source = "$PSScriptRoot/../../.tools/game-data")
$ErrorActionPreference = 'Stop'
$fields = @{
  AvatarExcelConfigData = @('id','nameTextMapHash','iconName','weaponType','skillDepotId','candSkillDepotIds','useType')
  AvatarSkillDepotExcelConfigData = @('id','skills','energySkill','talents','talentStarName','skillDepotAbilityGroup')
  AvatarSkillExcelConfigData = @('id','nameTextMapHash','descTextMapHash','skillIcon','costElemType','proudSkillGroupId')
  AvatarTalentExcelConfigData = @('talentId','nameTextMapHash','descTextMapHash','icon','openConfig')
  FetterInfoExcelConfigData = @('avatarId','avatarConstellationBeforTextMapHash','avatarConstellationAfterTextMapHash','avatarVisionBeforTextMapHash','avatarVisionAfterTextMapHash')
}
$hashes = [System.Collections.Generic.HashSet[string]]::new()
foreach ($name in $fields.Keys) {
  $raw = Get-Content -Raw -LiteralPath "$Source/$name.json" | ConvertFrom-Json -AsHashtable
  $compact = @(foreach ($row in $raw) {
    $item = [ordered]@{}
    foreach ($field in $fields[$name]) {
      if ($row.ContainsKey($field)) {
        $item[$field] = $row[$field]
        if ($field.EndsWith('TextMapHash')) { [void]$hashes.Add([string]$row[$field]) }
      }
    }
    $item
  })
  ConvertTo-Json -InputObject $compact -Depth 15 -Compress | Set-Content -LiteralPath "$PSScriptRoot/$name.json" -Encoding utf8NoBOM
}
foreach ($mapName in @('TextMapEN','TextMap_MediumEN')) {
  $allText = Get-Content -Raw -LiteralPath "$Source/$mapName.json" | ConvertFrom-Json -AsHashtable
  $text = [ordered]@{}
  foreach ($hash in ($hashes | Sort-Object)) {
    if ($allText.ContainsKey($hash)) { $text[$hash] = $allText[$hash] }
  }
  ConvertTo-Json -InputObject $text -Depth 5 -Compress | Set-Content -LiteralPath "$PSScriptRoot/$mapName.json" -Encoding utf8NoBOM
}
