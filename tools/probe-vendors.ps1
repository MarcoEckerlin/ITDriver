# Diagnostic only: checks which official vendor support endpoints answer from a plain client. Not shipped.
$ErrorActionPreference = 'Continue'
$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 ITDriver'
function Probe($name, $url, $patterns, $max = 6) {
  Write-Host "===== $name"
  Write-Host "URL $url"
  try {
    $r = Invoke-WebRequest -Uri $url -UserAgent $ua -Headers @{ 'Accept-Language' = 'en-US' } -TimeoutSec 40 -MaximumRedirection 5
    $c = [string]$r.Content
    Write-Host "STATUS $($r.StatusCode) LEN $($c.Length) TYPE $($r.Headers['Content-Type'])"
    Write-Host ("HEAD " + ($c.Substring(0, [Math]::Min(400, $c.Length)) -replace '\s+', ' '))
    foreach ($p in $patterns) {
      $ms = [regex]::Matches($c, $p, 'IgnoreCase,Singleline')
      Write-Host "PATTERN [$p] -> $($ms.Count) matches"
      $ms | Select-Object -First $max | ForEach-Object { Write-Host ("   " + ($_.Value -replace '\s+', ' ').Substring(0, [Math]::Min(300, ($_.Value -replace '\s+', ' ').Length))) }
    }
  } catch { Write-Host "ERROR $($_.Exception.Message)" }
}



$base = 'https://www.catalog.update.microsoft.com/Search.aspx?q='
function TryPost($label, $target, $extra) {
  $s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  $u = $base + 'VEN_1002%26DEV_744C'
  $r = Invoke-WebRequest -Uri $u -WebSession $s -UserAgent $ua
  $c = $r.Content
  $form = [ordered]@{}
  foreach ($m in [regex]::Matches($c, '<input[^>]*>', 'IgnoreCase')) {
    $t = [regex]::Match($m.Value, 'type="([^"]*)"').Groups[1].Value
    if ($t -notin @('hidden','text')) { continue }
    $n = [regex]::Match($m.Value, 'name="([^"]*)"').Groups[1].Value
    $v = [regex]::Match($m.Value, 'value="([^"]*)"').Groups[1].Value
    if ($n) { $form[$n] = [System.Net.WebUtility]::HtmlDecode($v) }
  }
  $form['__EVENTTARGET'] = $target
  $form['__EVENTARGUMENT'] = ''
  foreach ($k in $extra.Keys) { $form[$k] = $extra[$k] }
  try {
    $r2 = Invoke-WebRequest -Uri $u -Method Post -Body $form -WebSession $s -UserAgent $ua -ContentType 'application/x-www-form-urlencoded' -Headers @{ Referer = $u; Origin = 'https://www.catalog.update.microsoft.com' }
    $c2 = $r2.Content
    $err = [regex]::Match($c2, 'Error number: [0-9A-F]+').Value
    Write-Host "$label LEN $($c2.Length) DUR " ([regex]::Match($c2, 'searchDuration">[^<]*').Value) " ERR $err"
    [regex]::Matches($c2, 'goToDetails\("[^>]*>[^<]*<') | Select-Object -First 3 | ForEach-Object { Write-Host "   $($_.Value -replace '\s+',' ')" }
    [regex]::Matches($c2, '_C4_R\d+"[^>]*>[^<]*<') | Select-Object -First 3 | ForEach-Object { Write-Host "   $($_.Value -replace '\s+',' ')" }
  } catch { Write-Host "$label ERROR $($_.Exception.Message)" }
}
TryPost 'next-with-text' 'ctl00$catalogBody$nextPageLinkText' @{}
TryPost 'next-imagebutton' '' @{ 'ctl00$catalogBody$nextPageLinkButton.x' = '5'; 'ctl00$catalogBody$nextPageLinkButton.y' = '5' }
TryPost 'sort-date' 'ctl00$catalogBody$updateMatches$ctl02$dateComputedHeaderLink' @{}
TryPost 'sort-version' 'ctl00$catalogBody$updateMatches$ctl02$driverVerVersionHeaderLink' @{}
