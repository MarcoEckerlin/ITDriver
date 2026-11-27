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


# Catalog paging via postback
$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$u = 'https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_1002%26DEV_744C'
$r = Invoke-WebRequest -Uri $u -WebSession $s -UserAgent $ua
$form = @{}
foreach ($m in [regex]::Matches($r.Content, '<input[^>]*type="hidden"[^>]*>', 'IgnoreCase')) {
  $n = [regex]::Match($m.Value, 'name="([^"]*)"').Groups[1].Value
  $v = [regex]::Match($m.Value, 'value="([^"]*)"').Groups[1].Value
  if ($n) { $form[$n] = [System.Net.WebUtility]::HtmlDecode($v) }
}
Write-Host "HIDDEN: $($form.Keys -join ', ')"
$form['__EVENTTARGET'] = 'ctl00$catalogBody$nextPageLinkText'
$form['__EVENTARGUMENT'] = ''
try {
  $r2 = Invoke-WebRequest -Uri $u -Method Post -Body $form -WebSession $s -UserAgent $ua
  Write-Host "PAGE2 STATUS $($r2.StatusCode) " ([regex]::Match($r2.Content, 'searchDuration">([^<]*)<').Groups[1].Value)
  [regex]::Matches($r2.Content, '_C5_R\d+"[^>]*>[^<]*<') | Select-Object -First 3 | ForEach-Object { Write-Host "   $($_.Value -replace '\s+',' ')" }
} catch { Write-Host "PAGE2 ERROR $($_.Exception.Message)" }

Probe 'catalog-swc' 'https://www.catalog.update.microsoft.com/Search.aspx?q=SWC%5CVEN_10EC%26AID_0001' @('searchDuration">[^<]*<', 'goToDetails\("[^>]*>[^<]*<') 4
Probe 'catalog-swc2' 'https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_10EC%26AID_0001' @('searchDuration">[^<]*<', 'goToDetails\("[^>]*>[^<]*<') 4
Probe 'catalog-hda' 'https://www.catalog.update.microsoft.com/Search.aspx?q=HDAUDIO%5CFUNC_01%26VEN_10EC%26DEV_1220%26SUBSYS_1458A0C3' @('searchDuration">[^<]*<', 'goToDetails\("[^>]*>[^<]*<') 4
Probe 'catalog-hda2' 'https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_10EC%26DEV_1220%26SUBSYS_1458A0C3' @('searchDuration">[^<]*<', 'goToDetails\("[^>]*>[^<]*<') 4
foreach ($c in 'am5/x870e','am5/b650','am5/b850','am4/b550','am4/x570') { Probe "amd-$c" "https://www.amd.com/en/support/downloads/drivers.html/chipsets/$c.html" @('amd_chipset_software_[0-9.]+\.exe') 2 }
Probe 'amd-9070' 'https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-9000-series/amd-radeon-rx-9070-xt.html' @('adrenalin-edition-[0-9.]+-win1[01][^"]*\.exe') 2
Probe 'amd-6800' 'https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-6000-series/amd-radeon-rx-6800-xt.html' @('adrenalin-edition-[0-9.]+-win1[01][^"]*\.exe') 2
Probe 'asus-os' 'https://www.asus.com/support/api/product.asmx/GetPDOS?website=global&model=TUF%20GAMING%20X870-PLUS%20WIFI' @('"Id"\s*:\s*"?\d+', '"Name"\s*:\s*"[^"]*"') 8
Probe 'asus-tuf' 'https://www.asus.com/support/api/product.asmx/GetPDDrivers?website=global&model=TUF%20GAMING%20X870-PLUS%20WIFI&pdhashedid=&osid=52' @('"Name"\s*:\s*"[^"]*","Count"', '"Title"\s*:\s*"[^"]*"') 20
Probe 'gigabyte-de' 'https://www.gigabyte.com/de/Motherboard/X870-AORUS-ELITE-WIFI7-ICE-rev-10/support' @('download\.gigabyte\.com[^"''\s]*') 4
Probe 'gigabyte-api' 'https://www.gigabyte.com/api/Support/GetDownloadFile?pid=8720&type=Driver' @('.{0,200}') 1
Probe 'msi-page' 'https://www.msi.com/Motherboard/MAG-X870-TOMAHAWK-WIFI/support' @('download\.msi\.com[^"''\s]*') 4
