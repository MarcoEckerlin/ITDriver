<#
.SYNOPSIS
    Exportiert ein Hardware- und Treiber-Inventar dieses PCs als JSON (für ITDriver-Entwicklung/Support).

.DESCRIPTION
    Liest nur Informationen aus (keine Änderungen am System):
      - Betriebssystem, Mainboard, BIOS, CPU, GPU
      - alle PnP-Geräte mit Hardware-IDs, Treiberversion, Treiberdatum, Hersteller, INF
      - Geräte mit Problemen (fehlende/defekte Treiber)
      - Treiberpakete im Driver Store (pnputil /enum-drivers)
      - ausstehende Treiber-Updates laut Windows Update (optional, -SkipWindowsUpdate zum Überspringen)

    Es werden KEINE Benutzernamen, Seriennummern oder Netzwerkadressen exportiert.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Get-ITDriverInventory.ps1
    -> schreibt ITDriver-Inventar-<Datum>.json auf den Desktop
#>
[CmdletBinding()]
param(
    [string]$OutFile = (Join-Path ([Environment]::GetFolderPath('Desktop')) ("ITDriver-Inventar-{0:yyyyMMdd-HHmm}.json" -f (Get-Date))),
    [switch]$SkipWindowsUpdate
)

$ErrorActionPreference = 'Continue'
Write-Host "ITDriver Inventar wird erstellt ..." -ForegroundColor Cyan

function Convert-CimDate($d) { if ($d) { try { return ([datetime]$d).ToString('yyyy-MM-dd') } catch { return "$d" } } $null }

$os    = Get-CimInstance Win32_OperatingSystem
$cs    = Get-CimInstance Win32_ComputerSystem
$board = Get-CimInstance Win32_BaseBoard
$bios  = Get-CimInstance Win32_BIOS
$cpu   = Get-CimInstance Win32_Processor | Select-Object -First 1
$gpus  = Get-CimInstance Win32_VideoController

Write-Host " - Geräte und Treiber lesen"
$signed = @{}
Get-CimInstance Win32_PnPSignedDriver | ForEach-Object { if ($_.DeviceID) { $signed[$_.DeviceID] = $_ } }

$devices = Get-CimInstance Win32_PnPEntity | ForEach-Object {
    $d = $signed[$_.PNPDeviceID]
    [ordered]@{
        Name             = $_.Name
        Class            = $_.PNPClass
        Manufacturer     = $_.Manufacturer
        InstanceId       = $_.PNPDeviceID
        HardwareIds      = @($_.HardwareID)
        CompatibleIds    = @($_.CompatibleID)
        Status           = $_.Status
        ErrorCode        = $_.ConfigManagerErrorCode
        Present          = $_.Present
        DriverVersion    = $d.DriverVersion
        DriverDate       = Convert-CimDate $d.DriverDate
        DriverProvider   = $d.DriverProviderName
        InfName          = $d.InfName
        IsSigned         = $d.IsSigned
    }
}

Write-Host " - Driver Store lesen (pnputil)"
$store = @()
try {
    $raw = & pnputil.exe /enum-drivers 2>$null
    $cur = $null
    foreach ($line in $raw) {
        if ($line -match '^\s*$') { if ($cur) { $store += [pscustomobject]$cur; $cur = $null }; continue }
        if ($line -match '^\s*([^:]+):\s*(.*)$') {
            if (-not $cur) { $cur = [ordered]@{} }
            $cur[$matches[1].Trim()] = $matches[2].Trim()
        }
    }
    if ($cur) { $store += [pscustomobject]$cur }
} catch { Write-Warning "pnputil fehlgeschlagen: $_" }

$wu = @()
if (-not $SkipWindowsUpdate) {
    Write-Host " - Windows Update nach Treibern fragen (kann 1-3 Minuten dauern)"
    try {
        $session  = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result   = $searcher.Search("IsInstalled=0 and Type='Driver'")
        foreach ($u in $result.Updates) {
            $wu += [ordered]@{
                Title           = $u.Title
                DriverClass     = $u.DriverClass
                DriverModel     = $u.DriverModel
                DriverProvider  = $u.DriverProvider
                DriverVerDate   = Convert-CimDate $u.DriverVerDate
                DriverHardwareID= $u.DriverHardwareID
                SizeMB          = [math]::Round($u.MaxDownloadSize / 1MB, 1)
                UpdateId        = $u.Identity.UpdateID
            }
        }
    } catch { Write-Warning "Windows Update Abfrage fehlgeschlagen: $_" }
}

$inventory = [ordered]@{
    Tool        = 'ITDriver-Inventory'
    Version     = 1
    CreatedUtc  = (Get-Date).ToUniversalTime().ToString('o')
    System      = [ordered]@{
        OS            = $os.Caption
        OSVersion     = $os.Version
        OSBuild       = $os.BuildNumber
        Architecture  = $os.OSArchitecture
        Manufacturer  = $cs.Manufacturer
        Model         = $cs.Model
        Board         = "$($board.Manufacturer) $($board.Product)"
        BiosVersion   = $bios.SMBIOSBIOSVersion
        BiosDate      = Convert-CimDate $bios.ReleaseDate
        Cpu           = $cpu.Name
        RamGB         = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
        Gpus          = @($gpus | ForEach-Object { [ordered]@{ Name = $_.Name; DriverVersion = $_.DriverVersion; DriverDate = Convert-CimDate $_.DriverDate; Pnp = $_.PNPDeviceID } })
    }
    ProblemDevices        = @($devices | Where-Object { $_.ErrorCode -and $_.ErrorCode -ne 0 })
    Devices               = @($devices)
    DriverStore           = @($store)
    WindowsUpdateDrivers  = @($wu)
}

$inventory | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutFile -Encoding utf8
Write-Host ""
Write-Host ("Fertig: {0} Geräte, {1} mit Problemen, {2} Treiber-Updates bei Windows Update." -f $devices.Count, $inventory.ProblemDevices.Count, $wu.Count) -ForegroundColor Green
Write-Host "Datei: $OutFile"
