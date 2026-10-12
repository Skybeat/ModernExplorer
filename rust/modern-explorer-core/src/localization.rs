#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum LanguageCode {
    De,
    En,
    Es,
    Fr,
    It,
    Nl,
    Pl,
    Pt,
    Ja,
    Zh,
}

impl LanguageCode {
    pub fn from_setting(code: &str) -> Self {
        let lower = code.trim().to_ascii_lowercase();
        let effective = if lower.is_empty() || lower == "system" {
            std::env::var("LANG")
                .or_else(|_| std::env::var("LC_ALL"))
                .unwrap_or_else(|_| "de".to_string())
                .to_ascii_lowercase()
        } else {
            lower
        };

        if effective.starts_with("de") {
            LanguageCode::De
        } else if effective.starts_with("es") {
            LanguageCode::Es
        } else if effective.starts_with("fr") {
            LanguageCode::Fr
        } else if effective.starts_with("it") {
            LanguageCode::It
        } else if effective.starts_with("nl") {
            LanguageCode::Nl
        } else if effective.starts_with("pl") {
            LanguageCode::Pl
        } else if effective.starts_with("pt") {
            LanguageCode::Pt
        } else if effective.starts_with("ja") {
            LanguageCode::Ja
        } else if effective.starts_with("zh") {
            LanguageCode::Zh
        } else if effective.starts_with("en") {
            LanguageCode::En
        } else {
            LanguageCode::De
        }
    }

    pub fn code_str(self) -> &'static str {
        match self {
            LanguageCode::De => "de",
            LanguageCode::En => "en",
            LanguageCode::Es => "es",
            LanguageCode::Fr => "fr",
            LanguageCode::It => "it",
            LanguageCode::Nl => "nl",
            LanguageCode::Pl => "pl",
            LanguageCode::Pt => "pt",
            LanguageCode::Ja => "ja",
            LanguageCode::Zh => "zh",
        }
    }

    pub fn all_options() -> &'static [(&'static str, &'static str)] {
        &[
            ("system", "System Default (Auto)"),
            ("de", "Deutsch (German)"),
            ("en", "English"),
            ("es", "Español (Spanish)"),
            ("fr", "Français (French)"),
            ("it", "Italiano (Italian)"),
            ("nl", "Nederlands (Dutch)"),
            ("pl", "Polski (Polish)"),
            ("pt", "Português (Portuguese)"),
            ("ja", "日本語 (Japanese)"),
            ("zh", "中文 (Chinese)"),
        ]
    }
}

pub struct LocalizationService;

impl LocalizationService {
    pub fn t(lang_setting: &str, key: &'static str) -> &'static str {
        let lang = LanguageCode::from_setting(lang_setting);
        match (lang, key) {
            (LanguageCode::De, "Col_Name") => "Name",
            (_, "Col_Name") => "Name",
            (LanguageCode::De, "Col_Size") => "Größe",
            (LanguageCode::Es, "Col_Size") => "Tamaño",
            (LanguageCode::Fr, "Col_Size") => "Taille",
            (_, "Col_Size") => "Size",
            (LanguageCode::De, "Col_Modified") => "Geändert am",
            (LanguageCode::Es, "Col_Modified") => "Modificado",
            (LanguageCode::Fr, "Col_Modified") => "Modifié le",
            (_, "Col_Modified") => "Modified",
            (LanguageCode::De, "Col_Ext") => "Typ",
            (_, "Col_Ext") => "Ext",

            (LanguageCode::De, "Btn_BatchRename") => "✏ Mehrfach-Umbenennen",
            (_, "Btn_BatchRename") => "✏ Batch Rename",
            (LanguageCode::De, "Btn_Diff") => "⚖ Diff",
            (_, "Btn_Diff") => "⚖ Diff",
            (LanguageCode::De, "Btn_Hashes") => "🔐 Hashes",
            (_, "Btn_Hashes") => "🔐 Hashes",
            (LanguageCode::De, "Btn_SplitShred") => "✂ Split/Shred",
            (_, "Btn_SplitShred") => "✂ Split/Shred",
            (LanguageCode::De, "Btn_Tags") => "🏷 Tags",
            (_, "Btn_Tags") => "🏷 Tags",
            (LanguageCode::De, "Btn_Workspaces") => "🗂 Workspaces",
            (_, "Btn_Workspaces") => "🗂 Workspaces",
            (LanguageCode::De, "Btn_Export") => "📋 Export",
            (_, "Btn_Export") => "📋 Export",
            (LanguageCode::De, "Btn_Basket") => "🧺 Sammelkorb",
            (_, "Btn_Basket") => "🧺 Basket",
            (LanguageCode::De, "Btn_AttrExif") => "🕒 Attribute/EXIF",
            (_, "Btn_AttrExif") => "🕒 Attr/EXIF",
            (LanguageCode::De, "Btn_Console") => ">_ Konsole",
            (_, "Btn_Console") => ">_ Console",
            (LanguageCode::De, "Btn_Git") => "⎇ Git",
            (_, "Btn_Git") => "⎇ Git",
            (LanguageCode::De, "Btn_Sync") => "⇄ Synchronisieren",
            (_, "Btn_Sync") => "⇄ Sync",
            (LanguageCode::De, "Btn_DeepSearch") => "🔍 Tiefensuche",
            (_, "Btn_DeepSearch") => "🔍 Deep Search",
            (LanguageCode::De, "Btn_Duplicates") => "🧹 Duplikate",
            (_, "Btn_Duplicates") => "🧹 Duplicates",
            (LanguageCode::De, "Btn_Treemap") => "🗺 Speicherplatz (Treemap)",
            (_, "Btn_Treemap") => "🗺 Treemap",
            (LanguageCode::De, "Btn_Pack") => "📦 Packen",
            (_, "Btn_Pack") => "📦 Pack",
            (LanguageCode::De, "Btn_Remote") => "🌐 FTP/SFTP",
            (_, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::De, "Btn_Preview") => "👁 Vorschau",
            (_, "Btn_Preview") => "👁 Preview",
            (LanguageCode::De, "Btn_Settings") => "⚙ Einstellungen",
            (_, "Btn_Settings") => "⚙ Settings",
            (LanguageCode::De, "Btn_About") => "ℹ Über / Update",
            (_, "Btn_About") => "ℹ About / Update",
            (LanguageCode::De, "Btn_ColorPicker") => "🎨 Farbpicker",
            (_, "Btn_ColorPicker") => "🎨 Color Picker",
            (LanguageCode::De, "Btn_ResetDefault") => "↺ Standardfarbe",
            (_, "Btn_ResetDefault") => "↺ Default Color",

            (LanguageCode::De, "Update_Available_Title") => "Update verfügbar",
            (_, "Update_Available_Title") => "Update Available",
            (LanguageCode::De, "Update_ReleaseNotes_Header") => "📋 Änderungen in diesem Release:",
            (_, "Update_ReleaseNotes_Header") => "📋 Changes in this Release:",
            (LanguageCode::De, "Update_ViewOnGitHub") => "🌐 Auf GitHub ansehen ↗",
            (_, "Update_ViewOnGitHub") => "🌐 View on GitHub ↗",

            (LanguageCode::De, "Treemap_Cat_VideoMedia") => "🎬 Video & Audio",
            (_, "Treemap_Cat_VideoMedia") => "🎬 Video & Audio",
            (LanguageCode::De, "Treemap_Cat_ArchiveImage") => "📦 Archive & Disk-Images",
            (_, "Treemap_Cat_ArchiveImage") => "📦 Archives & Disk Images",
            (LanguageCode::De, "Treemap_Cat_VirtualDiskSystem") => "💿 Virtuelle Disks, DB & System",
            (_, "Treemap_Cat_VirtualDiskSystem") => "💿 Virtual Disks, DB & System",
            (LanguageCode::De, "Treemap_Cat_CodeDocument") => "📝 Code, Text & Dokumente",
            (_, "Treemap_Cat_CodeDocument") => "📝 Code, Text & Documents",
            (LanguageCode::De, "Treemap_Cat_PictureGraphic") => "🖼 Bilder & Grafiken",
            (_, "Treemap_Cat_PictureGraphic") => "🖼 Pictures & Graphics",
            (LanguageCode::De, "Treemap_Cat_BinaryExecutable") => "⚙ Programme & Bibliotheken",
            (_, "Treemap_Cat_BinaryExecutable") => "⚙ Binaries & Libraries",
            (LanguageCode::De, "Treemap_Cat_OtherTemp") => "📄 Sonstige & Temporäre Dateien",
            (_, "Treemap_Cat_OtherTemp") => "📄 Other & Temporary Files",

            _ => key,
        }
    }
}

