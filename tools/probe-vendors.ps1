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

Probe 'catalog-rx7900' 'https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_1002%26DEV_744C' @('page \d+ of \d+', 'nextPage[^>]{0,200}', '__VIEWSTATE[^>]{0,80}', '__EVENTVALIDATION[^>]{0,60}', 'id="ctl00_catalogBody_searchDuration"[^<]*<', '_C1_R\d+') 4
Probe 'catalog-amd-gpio' 'https://www.catalog.update.microsoft.com/Search.aspx?q=AMDI0030' @('goToDetails[^>]*>[^<]*<', '_C5_R\d+"[^>]*>[^<]*<') 8
Probe 'catalog-amd-smbus' 'https://www.catalog.update.microsoft.com/Search.aspx?q=VEN_1022%26DEV_790B' @('goToDetails[^>]*>[^<]*<') 8
Probe 'catalog-amd-chipset-text' 'https://www.catalog.update.microsoft.com/Search.aspx?q=%22Advanced%20Micro%20Devices%22%20System' @('goToDetails[^>]*>[^<]*<') 8

Probe 'msi-api' 'https://www.msi.com/api/v1/product/support/panel?product=MAG-X870-TOMAHAWK-WIFI&type=driver' @('"title"\s*:\s*"[^"]*"', '"download_version"\s*:\s*"[^"]*"', '"download_url"\s*:\s*"[^"]*"', '"os"[^\]]{0,80}') 6
Probe 'asus-api-old' 'https://www.asus.com/support/api/product.asmx/GetPDDrivers?website=global&model=ROG%20STRIX%20X870E-E%20GAMING%20WIFI&pdhashedid=&osid=52' @('"Title"\s*:\s*"[^"]*"', '"Version"\s*:\s*"[^"]*"', '"Global"\s*:\s*"[^"]*"') 6
Probe 'asus-webapi' 'https://www.asus.com/support/webapi/product/GetPDDrivers?website=global&model=ROG-STRIX-X870E-E-GAMING-WIFI&osid=52&systemCode=rog' @('"Title"\s*:\s*"[^"]*"', '"Version"\s*:\s*"[^"]*"', '"Global"\s*:\s*"[^"]*"') 6
Probe 'asus-search' 'https://www.asus.com/support/webapi/product/GetPDLevel?website=global&type=1&typeid=0&productflag=1&keyword=X870E-E' @('"[A-Za-z]*[Hh]ashedid"\s*:\s*"[^"]*"', '"PDName"\s*:\s*"[^"]*"', '"Name"\s*:\s*"[^"]*"') 6
Probe 'gigabyte-page' 'https://www.gigabyte.com/Motherboard/X870-AORUS-ELITE-WIFI7-ICE-rev-10/support' @('download\.gigabyte\.com[^"''\s]*', 'Ajax/[A-Za-z/]+', 'data-[a-z\-]*id="?\d+', 'pid\s*[=:]\s*"?\d+', 'Realtek[^<]{0,80}', 'Chipset[^<]{0,80}', 'Version[^<]{0,40}<[^<]{0,80}') 8
Probe 'asrock-page' 'https://www.asrock.com/mb/AMD/X870%20Steel%20Legend%20WiFi/index.asp' @('download\.asrock\.com[^"''\s]*', 'Download\.asp[^"''\s]*', '[Dd]river[^<]{0,60}') 6
Probe 'asrock-dl' 'https://www.asrock.com/mb/AMD/X870%20Steel%20Legend%20WiFi/Download.asp?cat=Drivers&os=Win1164' @('download\.asrock\.com[^"''\s]*', '<td[^>]*>[^<]*\d+\.\d+[^<]*</td>') 8
Probe 'amd-chipset' 'https://www.amd.com/en/support/downloads/drivers.html/chipsets/am5/x870.html' @('drivers\.amd\.com[^"''\s]*', 'Revision Number[^<]{0,20}(<[^>]*>){0,6}[^<]{0,40}', '\d+\.\d+\.\d+\.\d+') 6
Probe 'amd-gpu' 'https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-7000-series/amd-radeon-rx-7900-xt.html' @('drivers\.amd\.com[^"''\s]*', 'Adrenalin[^<]{0,60}', '\d+\.\d+\.\d+') 6
Probe 'intel-gfx' 'https://www.intel.com/content/www/us/en/download/785597/intel-arc-iris-xe-graphics-windows.html' @('downloadmirror\.intel\.com[^"''\s]*', '"version"[^,]{0,40}', 'Version[^<]{0,20}(<[^>]*>){0,4}[^<]{0,40}') 6
Probe 'intel-chipset' 'https://www.intel.com/content/www/us/en/download/19347/chipset-inf-utility.html' @('downloadmirror\.intel\.com[^"''\s]*', 'Version[^<]{0,20}(<[^>]*>){0,4}[^<]{0,40}') 6
Probe 'realtek-audio' 'https://www.realtek.com/Download/List?cate_id=593' @('[0-9]\.[0-9]\.[0-9]{4}\.[0-9]', 'Windows[^<]{0,60}') 6
