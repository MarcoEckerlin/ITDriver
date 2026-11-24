namespace ITDriver.Core;

/// <summary>Official download/support information for a hardware vendor. ITDriver never re-hosts driver files;
/// it links to the vendor's own pages and tools.</summary>
public sealed record VendorInfo(string Name, string DownloadUrl, string? Tool = null, bool IsMicrosoft = false, string? Hint = null);

/// <summary>Curated list of the most common PC hardware vendors (PCI and USB vendor IDs).</summary>
public static class VendorCatalog
{
    private static readonly VendorInfo Nvidia = new("NVIDIA", "https://www.nvidia.com/de-de/drivers/", "NVIDIA App",
        Hint: "Grafiktreiber direkt von NVIDIA sind meist aktueller als Windows Update.");
    private static readonly VendorInfo Amd = new("AMD", "https://www.amd.com/de/support/download/drivers.html", "AMD Software: Adrenalin Edition (Auto-Detect)",
        Hint: "Grafik- und Chipsatztreiber direkt von AMD sind meist aktueller als Windows Update.");
    private static readonly VendorInfo Intel = new("Intel", "https://www.intel.de/content/www/de/de/support/detect.html", "Intel Driver & Support Assistant",
        Hint: "Der Intel Driver & Support Assistant erkennt Grafik-, WLAN-, Bluetooth- und Chipsatztreiber.");
    private static readonly VendorInfo Realtek = new("Realtek", "https://www.realtek.com/Download/Index", null,
        Hint: "Für Audio/LAN von Realtek ist oft der Treiber auf der Support-Seite des Mainboard- bzw. PC-Herstellers die bessere Wahl.");
    private static readonly VendorInfo Microsoft = new("Microsoft", "https://support.microsoft.com/de-de/windows", null, IsMicrosoft: true);

    private static readonly Dictionary<string, VendorInfo> ById = new(StringComparer.OrdinalIgnoreCase)
    {
        // Graphics / chipset / CPU
        ["PCI:10DE"] = Nvidia,
        ["PCI:1002"] = Amd,
        ["PCI:1022"] = Amd,
        ["PCI:8086"] = Intel,
        ["USB:8087"] = Intel,
        ["ACPI:AMDI"] = Amd,
        ["ACPI:AMDP"] = Amd,
        ["ACPI:INTC"] = Intel,
        ["ACPI:ITE"] = new("ITE", "https://www.ite.com.tw/", Hint: "ITE-Controller (RGB, USB, Sensoren) werden vom Mainboard-Hersteller mit dessen Software geliefert."),
        ["PCI:1106"] = new("VIA", "https://www.viatech.com/en/support/drivers/"),
        // Network / wireless / bluetooth
        ["PCI:10EC"] = Realtek,
        ["USB:0BDA"] = Realtek,
        ["PCI:14E4"] = new("Broadcom", "https://www.broadcom.com/support/download-search"),
        ["USB:0A5C"] = new("Broadcom", "https://www.broadcom.com/support/download-search"),
        ["PCI:168C"] = new("Qualcomm Atheros", "https://www.qualcomm.com/support", Hint: "Killer-/Atheros-WLAN: Treiber vom PC-Hersteller oder Intel/Killer bevorzugen."),
        ["PCI:17CB"] = new("Qualcomm", "https://www.qualcomm.com/support"),
        ["USB:0CF3"] = new("Qualcomm Atheros", "https://www.qualcomm.com/support"),
        ["PCI:14C3"] = new("MediaTek", "https://www.mediatek.com/products/connectivity-and-networking", Hint: "MediaTek-WLAN-Treiber kommen meist über Windows Update oder den PC-Hersteller."),
        ["USB:0E8D"] = new("MediaTek", "https://www.mediatek.com/products/connectivity-and-networking"),
        ["USB:0489"] = new("Foxconn / Hon Hai (WLAN-/Bluetooth-Modul)", "https://www.catalog.update.microsoft.com/", Hint: "Bluetooth-/WLAN-Module von Foxconn nutzen Chips von MediaTek oder Qualcomm – Treiber kommen vom Mainboard-/PC-Hersteller oder Windows Update."),
        ["PCI:1D6A"] = new("Marvell (Aquantia)", "https://www.marvell.com/support.html"),
        ["PCI:11AB"] = new("Marvell", "https://www.marvell.com/support.html"),
        ["PCI:1B4B"] = new("Marvell", "https://www.marvell.com/support.html"),
        ["PCI:1969"] = new("Qualcomm Atheros (Killer/Attansic)", "https://www.qualcomm.com/support"),
        ["USB:2357"] = new("TP-Link", "https://www.tp-link.com/de/support/download/"),
        ["USB:0B95"] = new("ASIX", "https://www.asix.com.tw/en/support/download"),
        // Storage / USB controllers
        ["PCI:1B21"] = new("ASMedia", "https://www.asmedia.com.tw/", Hint: "ASMedia-USB/SATA-Treiber liefert normalerweise der Mainboard-Hersteller."),
        ["PCI:1912"] = new("Renesas", "https://www.renesas.com/"),
        ["PCI:1B73"] = new("Fresco Logic", "https://www.frescologic.com/"),
        ["PCI:197B"] = new("JMicron", "https://www.jmicron.com/"),
        ["PCI:144D"] = new("Samsung", "https://semiconductor.samsung.com/consumer-storage/support/tools/", "Samsung Magician"),
        ["PCI:15B7"] = new("Western Digital / SanDisk", "https://support-de.wd.com/app/products/downloads", "WD Dashboard"),
        ["PCI:2646"] = new("Kingston", "https://www.kingston.com/de/support/technical/downloads", "Kingston SSD Manager"),
        ["PCI:C0A9"] = new("Crucial / Micron", "https://www.crucial.de/support/ssd-support", "Crucial Storage Executive"),
        ["PCI:1344"] = new("Crucial / Micron", "https://www.crucial.de/support/ssd-support", "Crucial Storage Executive"),
        ["PCI:1C5C"] = new("SK hynix", "https://ssd.skhynix.com/download/"),
        ["PCI:1E0F"] = new("KIOXIA", "https://europe.kioxia.com/de-de/personal/support.html"),
        ["PCI:1987"] = new("Phison", "https://www.phison.com/"),
        ["PCI:126F"] = new("Silicon Motion", "https://www.siliconmotion.com/"),
        // Audio
        ["PCI:1102"] = new("Creative", "https://support.creative.com/"),
        ["USB:041E"] = new("Creative", "https://support.creative.com/"),
        ["USB:1235"] = new("Focusrite", "https://downloads.focusrite.com/"),
        ["USB:0D8C"] = new("C-Media", "https://www.cmedia.com.tw/"),
        // Input / peripherals
        ["USB:046D"] = new("Logitech", "https://www.logitech.com/de-de/software/logi-options-plus.html", "Logi Options+ / G HUB"),
        ["USB:1532"] = new("Razer", "https://www.razer.com/de-de/synapse-4", "Razer Synapse"),
        ["USB:1B1C"] = new("Corsair", "https://www.corsair.com/de/de/s/downloads", "Corsair iCUE"),
        ["USB:1038"] = new("SteelSeries", "https://steelseries.com/gg", "SteelSeries GG"),
        ["USB:056A"] = new("Wacom", "https://www.wacom.com/de-de/support/product-support/drivers"),
        ["USB:06CB"] = new("Synaptics", "https://www.synaptics.com/products/touchpad-family", Hint: "Touchpad-Treiber kommen vom Notebook-Hersteller."),
        ["USB:04F3"] = new("ELAN", "https://www.emc.com.tw/", Hint: "Touchpad-Treiber kommen vom Notebook-Hersteller."),
        ["USB:27C6"] = new("Goodix", "https://www.goodix.com/", Hint: "Fingerabdruck-Treiber kommen vom Notebook-Hersteller oder Windows Update."),
        ["USB:0FD9"] = new("Elgato", "https://www.elgato.com/de/de/s/downloads"),
        ["USB:054C"] = new("Sony", "https://www.playstation.com/de-de/support/"),
        ["USB:057E"] = new("Nintendo", "https://www.nintendo.com/de-de/Support/"),
        ["USB:045E"] = Microsoft,
        ["PCI:1414"] = Microsoft,
        // Printers / scanners
        ["USB:03F0"] = new("HP / HyperX", "https://support.hp.com/de-de/drivers", "HP Smart / HyperX NGENUITY"),
        ["USB:04A9"] = new("Canon", "https://www.canon.de/support/"),
        ["USB:04B8"] = new("Epson", "https://www.epson.de/de_DE/support"),
        ["USB:04F9"] = new("Brother", "https://support.brother.com/"),
        ["USB:04E8"] = new("Samsung", "https://www.samsung.com/de/support/"),
        // USB-serial adapters
        ["USB:0403"] = new("FTDI", "https://ftdichip.com/drivers/vcp-drivers/"),
        ["USB:067B"] = new("Prolific", "https://www.prolific.com.tw/US/ShowProduct.aspx?p_id=225&pcid=41"),
        ["USB:10C4"] = new("Silicon Labs", "https://www.silabs.com/developers/usb-to-uart-bridge-vcp-drivers"),
        ["USB:1A86"] = new("WCH (CH340/CH341)", "https://www.wch-ic.com/downloads/CH341SER_EXE.html"),
        // Virtualisation
        ["PCI:15AD"] = new("VMware", "https://www.vmware.com/", "VMware Tools"),
        ["PCI:80EE"] = new("Oracle VirtualBox", "https://www.virtualbox.org/wiki/Downloads", "VirtualBox Guest Additions"),
        ["PCI:1AF4"] = new("Red Hat VirtIO", "https://github.com/virtio-win/virtio-win-pkg-scripts"),
    };

    /// <summary>Support tools of the most common PC / mainboard manufacturers (matched against Win32_ComputerSystem.Manufacturer or board vendor).</summary>
    public static readonly IReadOnlyList<(string Match, VendorInfo Info)> OemSupport = new List<(string, VendorInfo)>
    {
        ("Dell", new("Dell", "https://www.dell.com/support/home/de-de", "Dell SupportAssist / Dell Command Update")),
        ("Lenovo", new("Lenovo", "https://pcsupport.lenovo.com/de/de", "Lenovo Vantage / System Update")),
        ("HP", new("HP", "https://support.hp.com/de-de/drivers", "HP Support Assistant")),
        ("Hewlett", new("HP", "https://support.hp.com/de-de/drivers", "HP Support Assistant")),
        ("ASUS", new("ASUS", "https://www.asus.com/de/support/download-center/", "MyASUS / Armoury Crate")),
        ("Micro-Star", new("MSI", "https://de.msi.com/support/download", "MSI Center")),
        ("MSI", new("MSI", "https://de.msi.com/support/download", "MSI Center")),
        ("Gigabyte", new("Gigabyte", "https://www.gigabyte.com/de/Support", "GIGABYTE Control Center")),
        ("ASRock", new("ASRock", "https://www.asrock.com/support/index.de.asp", "ASRock Live Update")),
        ("Acer", new("Acer", "https://www.acer.com/de-de/support/drivers-and-manuals", "Acer Care Center")),
        ("Microsoft", new("Microsoft Surface", "https://support.microsoft.com/de-de/surface/download-drivers-and-firmware-for-surface-09bb2e09-2a4b-cb69-0951-078a7739e120", IsMicrosoft: true)),
        ("Samsung", new("Samsung", "https://www.samsung.com/de/support/", "Samsung Update")),
        ("Fujitsu", new("Fujitsu", "https://support.ts.fujitsu.com/", "DeskUpdate")),
        ("Medion", new("Medion", "https://www.medion.com/de/service/start/")),
        ("Toshiba", new("Dynabook / Toshiba", "https://support.dynabook.com/")),
        ("Dynabook", new("Dynabook", "https://support.dynabook.com/")),
        ("Razer", new("Razer", "https://mysupport.razer.com/")),
        ("Huawei", new("Huawei", "https://consumer.huawei.com/de/support/", "PC Manager")),
        ("Xiaomi", new("Xiaomi", "https://www.mi.com/global/support")),
    };

    public static int Count => ById.Count;

    public static VendorInfo? Find(HardwareId id)
        => ById.TryGetValue($"{id.VendorNamespace}:{id.VendorId}", out var v) ? v : null;

    public static VendorInfo? Find(DeviceInfo device)
    {
        foreach (var raw in device.HardwareIds.Concat(device.CompatibleIds))
        {
            var id = HardwareId.TryParse(raw);
            if (id != null && Find(id) is { } v) return v;
        }
        return null;
    }

    public static VendorInfo? FindOem(string? manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer)) return null;
        foreach (var (match, info) in OemSupport)
        {
            // "HP" must match as a word, otherwise e.g. "Hewlett" handles it; avoid matching inside other names.
            if (match.Length <= 3)
            {
                var words = manufacturer.Split(new[] { ' ', ',', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Any(w => w.Equals(match, StringComparison.OrdinalIgnoreCase))) return info;
            }
            else if (manufacturer.Contains(match, StringComparison.OrdinalIgnoreCase)) return info;
        }
        return null;
    }
}
