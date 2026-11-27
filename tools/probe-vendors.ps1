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
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$u = $base + 'VEN_1002%26DEV_744C'
$r = Invoke-WebRequest -Uri $u -WebSession $s -UserAgent $ua
$c = $r.Content
Write-Host "FORM: " ([regex]::Match($c, '<form[^>]*>').Value)
foreach ($m in [regex]::Matches($c, 'handlePageChange[^{]*\{.{0,900}', 'Singleline')) { Write-Host "JS: $($m.Value -replace '\s+',' ')" }
foreach ($m in [regex]::Matches($c, '<input[^>]*>', 'IgnoreCase')) { $v = $m.Value; if ($v.Length -gt 160) { $v = $v.Substring(0,160) }; Write-Host "INPUT: $v" }
foreach ($m in [regex]::Matches($c, '<a[^>]*(dateColumnHeader|HeaderLink|columnHeader|sort)[^>]*>', 'IgnoreCase')) { Write-Host "HDR: $($m.Value)" }
$form = @{}
foreach ($m in [regex]::Matches($c, '<input[^>]*type="hidden"[^>]*>', 'IgnoreCase')) {
  $n = [regex]::Match($m.Value, 'name="([^"]*)"').Groups[1].Value
  $v = [regex]::Match($m.Value, 'value="([^"]*)"').Groups[1].Value
  if ($n) { $form[$n] = [System.Net.WebUtility]::HtmlDecode($v) }
}
$form['__EVENTTARGET'] = 'ctl00$catalogBody$nextPageLinkText'
$form['__EVENTARGUMENT'] = ''
$r2 = Invoke-WebRequest -Uri $u -Method Post -Body $form -WebSession $s -UserAgent $ua -ContentType 'application/x-www-form-urlencoded'
$c2 = $r2.Content
Write-Host "PAGE2 LEN $($c2.Length) TITLE " ([regex]::Match($c2, '<title>[^<]*').Value) " DUR " ([regex]::Match($c2, 'searchDuration">[^<]*').Value)
Write-Host ("PAGE2 BODY " + (([regex]::Replace($c2, '<script.*?</script>|<style.*?</style>|<[^>]+>', ' ', 'Singleline') -replace '\s+',' ').Substring(0, 700)))

foreach ($q in '%22VEN_10EC%26DEV_1220%26SUBSYS_1458A0C3%22', 'VEN_10EC%26DEV_1220%26SUBSYS_1458A0C3%20Windows%2011', 'HDAUDIO%5CFUNC_01%26VEN_10EC%26DEV_1220', '%22HDAUDIO%5CFUNC_01%26VEN_10EC%26DEV_1220%22', 'VEN_10EC%26DEV_8125', '%22PCI%5CVEN_10EC%26DEV_8125%22') {
  Probe "q $q" ($base + $q) @('searchDuration">[^<]*<', 'goToDetails\("[^>]*>[^<]*<') 3
}
