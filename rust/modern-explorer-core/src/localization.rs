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
            (LanguageCode::Es, "Col_Name") => "Nombre",
            (LanguageCode::Fr, "Col_Name") => "Nom",
            (LanguageCode::It, "Col_Name") => "Nome",
            (LanguageCode::Nl, "Col_Name") => "Naam",
            (LanguageCode::Pl, "Col_Name") => "Nazwa",
            (LanguageCode::Pt, "Col_Name") => "Nome",
            (LanguageCode::Ja, "Col_Name") => "名前",
            (LanguageCode::Zh, "Col_Name") => "名称",
            (_, "Col_Name") => "Name",

            (LanguageCode::De, "Col_Size") => "Größe",
            (LanguageCode::Es, "Col_Size") => "Tamaño",
            (LanguageCode::Fr, "Col_Size") => "Taille",
            (LanguageCode::It, "Col_Size") => "Dimensione",
            (LanguageCode::Nl, "Col_Size") => "Grootte",
            (LanguageCode::Pl, "Col_Size") => "Rozmiar",
            (LanguageCode::Pt, "Col_Size") => "Tamanho",
            (LanguageCode::Ja, "Col_Size") => "サイズ",
            (LanguageCode::Zh, "Col_Size") => "大小",
            (_, "Col_Size") => "Size",

            (LanguageCode::De, "Col_Modified") => "Geändert am",
            (LanguageCode::Es, "Col_Modified") => "Modificado",
            (LanguageCode::Fr, "Col_Modified") => "Modifié le",
            (LanguageCode::It, "Col_Modified") => "Modificato",
            (LanguageCode::Nl, "Col_Modified") => "Gewijzigd",
            (LanguageCode::Pl, "Col_Modified") => "Zmodyfikowano",
            (LanguageCode::Pt, "Col_Modified") => "Modificado",
            (LanguageCode::Ja, "Col_Modified") => "更新日時",
            (LanguageCode::Zh, "Col_Modified") => "修改日期",
            (_, "Col_Modified") => "Modified",

            (LanguageCode::De, "Col_Ext") => "Typ",
            (LanguageCode::Es, "Col_Ext") => "Tipo",
            (LanguageCode::Fr, "Col_Ext") => "Type",
            (LanguageCode::It, "Col_Ext") => "Tipo",
            (LanguageCode::Nl, "Col_Ext") => "Type",
            (LanguageCode::Pl, "Col_Ext") => "Typ",
            (LanguageCode::Pt, "Col_Ext") => "Tipo",
            (LanguageCode::Ja, "Col_Ext") => "種類",
            (LanguageCode::Zh, "Col_Ext") => "类型",
            (_, "Col_Ext") => "Ext",

            (LanguageCode::De, "Btn_BatchRename") => "✏ Mehrfach-Umbenennen",
            (LanguageCode::Es, "Btn_BatchRename") => "✏ Cambio de nombre masivo",
            (LanguageCode::Fr, "Btn_BatchRename") => "✏ Renommage par lots",
            (LanguageCode::It, "Btn_BatchRename") => "✏ Rinomina in blocco",
            (LanguageCode::Nl, "Btn_BatchRename") => "✏ Meervoudig hernoemen",
            (LanguageCode::Pl, "Btn_BatchRename") => "✏ Masowa zmiana nazw",
            (LanguageCode::Pt, "Btn_BatchRename") => "✏ Renomear em lote",
            (LanguageCode::Ja, "Btn_BatchRename") => "✏ 一括リネーム",
            (LanguageCode::Zh, "Btn_BatchRename") => "✏ 批量重命名",
            (_, "Btn_BatchRename") => "✏ Batch Rename",

            (LanguageCode::De, "Btn_Diff") => "⚖ Diff",
            (LanguageCode::Es, "Btn_Diff") => "⚖ Comparar (Diff)",
            (LanguageCode::Fr, "Btn_Diff") => "⚖ Comparer (Diff)",
            (LanguageCode::It, "Btn_Diff") => "⚖ Confronta (Diff)",
            (LanguageCode::Nl, "Btn_Diff") => "⚖ Vergelijken (Diff)",
            (LanguageCode::Pl, "Btn_Diff") => "⚖ Porównaj (Diff)",
            (LanguageCode::Pt, "Btn_Diff") => "⚖ Comparar (Diff)",
            (LanguageCode::Ja, "Btn_Diff") => "⚖ 差分 (Diff)",
            (LanguageCode::Zh, "Btn_Diff") => "⚖ 文件比较 (Diff)",
            (_, "Btn_Diff") => "⚖ Diff",

            (LanguageCode::De, "Btn_Hashes") => "🔐 Hashes",
            (LanguageCode::Es, "Btn_Hashes") => "🔐 Hashes",
            (LanguageCode::Fr, "Btn_Hashes") => "🔐 Hachages",
            (LanguageCode::It, "Btn_Hashes") => "🔐 Hash",
            (LanguageCode::Nl, "Btn_Hashes") => "🔐 Hashes",
            (LanguageCode::Pl, "Btn_Hashes") => "🔐 Sumy kontrolne",
            (LanguageCode::Pt, "Btn_Hashes") => "🔐 Hashes",
            (LanguageCode::Ja, "Btn_Hashes") => "🔐 ハッシュ",
            (LanguageCode::Zh, "Btn_Hashes") => "🔐 哈希与校验",
            (_, "Btn_Hashes") => "🔐 Hashes",

            (LanguageCode::De, "Btn_SplitShred") => "✂ Split/Shred",
            (LanguageCode::Es, "Btn_SplitShred") => "✂ Dividir/Destruir",
            (LanguageCode::Fr, "Btn_SplitShred") => "✂ Diviser/Détruire",
            (LanguageCode::It, "Btn_SplitShred") => "✂ Dividi/Distruggi",
            (LanguageCode::Nl, "Btn_SplitShred") => "✂ Splitsen/Vernietigen",
            (LanguageCode::Pl, "Btn_SplitShred") => "✂ Dziel/Niszcz",
            (LanguageCode::Pt, "Btn_SplitShred") => "✂ Dividir/Triturar",
            (LanguageCode::Ja, "Btn_SplitShred") => "✂ 分割/完全消去",
            (LanguageCode::Zh, "Btn_SplitShred") => "✂ 分割/粉碎",
            (_, "Btn_SplitShred") => "✂ Split/Shred",

            (LanguageCode::De, "Btn_Tags") => "🏷 Tags",
            (LanguageCode::Es, "Btn_Tags") => "🏷 Etiquetas",
            (LanguageCode::Fr, "Btn_Tags") => "🏷 Étiquettes",
            (LanguageCode::It, "Btn_Tags") => "🏷 Etichette",
            (LanguageCode::Nl, "Btn_Tags") => "🏷 Tags",
            (LanguageCode::Pl, "Btn_Tags") => "🏷 Etykiety",
            (LanguageCode::Pt, "Btn_Tags") => "🏷 Tags",
            (LanguageCode::Ja, "Btn_Tags") => "🏷 タグ",
            (LanguageCode::Zh, "Btn_Tags") => "🏷 标签与备注",
            (_, "Btn_Tags") => "🏷 Tags",

            (LanguageCode::De, "Btn_Workspaces") => "🗂 Workspaces",
            (LanguageCode::Es, "Btn_Workspaces") => "🗂 Espacios de trabajo",
            (LanguageCode::Fr, "Btn_Workspaces") => "🗂 Espaces de travail",
            (LanguageCode::It, "Btn_Workspaces") => "🗂 Spazi di lavoro",
            (LanguageCode::Nl, "Btn_Workspaces") => "🗂 Werkruimten",
            (LanguageCode::Pl, "Btn_Workspaces") => "🗂 Obszary robocze",
            (LanguageCode::Pt, "Btn_Workspaces") => "🗂 Espaços de trabalho",
            (LanguageCode::Ja, "Btn_Workspaces") => "🗂 ワークスペース",
            (LanguageCode::Zh, "Btn_Workspaces") => "🗂 工作区",
            (_, "Btn_Workspaces") => "🗂 Workspaces",

            (LanguageCode::De, "Btn_Export") => "📋 Export",
            (LanguageCode::Es, "Btn_Export") => "📋 Exportar",
            (LanguageCode::Fr, "Btn_Export") => "📋 Exporter",
            (LanguageCode::It, "Btn_Export") => "📋 Esporta",
            (LanguageCode::Nl, "Btn_Export") => "📋 Exporteren",
            (LanguageCode::Pl, "Btn_Export") => "📋 Eksport",
            (LanguageCode::Pt, "Btn_Export") => "📋 Exportar",
            (LanguageCode::Ja, "Btn_Export") => "📋 エクスポート",
            (LanguageCode::Zh, "Btn_Export") => "📋 导出表格",
            (_, "Btn_Export") => "📋 Export",

            (LanguageCode::De, "Btn_Basket") => "🧺 Sammelkorb",
            (LanguageCode::Es, "Btn_Basket") => "🧺 Cesta de archivos",
            (LanguageCode::Fr, "Btn_Basket") => "🧺 Panier de fichiers",
            (LanguageCode::It, "Btn_Basket") => "🧺 Cestino raccolta",
            (LanguageCode::Nl, "Btn_Basket") => "🧺 Bestandsmand",
            (LanguageCode::Pl, "Btn_Basket") => "🧺 Koszyk plików",
            (LanguageCode::Pt, "Btn_Basket") => "🧺 Cesta de arquivos",
            (LanguageCode::Ja, "Btn_Basket") => "🧺 ファイルバスケット",
            (LanguageCode::Zh, "Btn_Basket") => "🧺 文件收集篮",
            (_, "Btn_Basket") => "🧺 Basket",

            (LanguageCode::De, "Btn_AttrExif") => "🕒 Attribute/EXIF",
            (LanguageCode::Es, "Btn_AttrExif") => "🕒 Atributos/EXIF",
            (LanguageCode::Fr, "Btn_AttrExif") => "🕒 Attributs/EXIF",
            (LanguageCode::It, "Btn_AttrExif") => "🕒 Attributi/EXIF",
            (LanguageCode::Nl, "Btn_AttrExif") => "🕒 Kenmerken/EXIF",
            (LanguageCode::Pl, "Btn_AttrExif") => "🕒 Atrybuty/EXIF",
            (LanguageCode::Pt, "Btn_AttrExif") => "🕒 Atributos/EXIF",
            (LanguageCode::Ja, "Btn_AttrExif") => "🕒 属性/EXIF",
            (LanguageCode::Zh, "Btn_AttrExif") => "🕒 属性/EXIF",
            (_, "Btn_AttrExif") => "🕒 Attr/EXIF",

            (LanguageCode::De, "Btn_Console") => ">_ Konsole",
            (LanguageCode::Es, "Btn_Console") => ">_ Consola",
            (LanguageCode::Fr, "Btn_Console") => ">_ Console",
            (LanguageCode::It, "Btn_Console") => ">_ Console",
            (LanguageCode::Nl, "Btn_Console") => ">_ Console",
            (LanguageCode::Pl, "Btn_Console") => ">_ Konsola",
            (LanguageCode::Pt, "Btn_Console") => ">_ Console",
            (LanguageCode::Ja, "Btn_Console") => ">_ コンソール",
            (LanguageCode::Zh, "Btn_Console") => ">_ 控制台",
            (_, "Btn_Console") => ">_ Console",

            (LanguageCode::De, "Btn_Git") => "⎇ Git",
            (LanguageCode::Es, "Btn_Git") => "⎇ Git",
            (LanguageCode::Fr, "Btn_Git") => "⎇ Git",
            (LanguageCode::It, "Btn_Git") => "⎇ Git",
            (LanguageCode::Nl, "Btn_Git") => "⎇ Git",
            (LanguageCode::Pl, "Btn_Git") => "⎇ Git",
            (LanguageCode::Pt, "Btn_Git") => "⎇ Git",
            (LanguageCode::Ja, "Btn_Git") => "⎇ Git",
            (LanguageCode::Zh, "Btn_Git") => "⎇ Git",
            (_, "Btn_Git") => "⎇ Git",

            (LanguageCode::De, "Btn_Sync") => "⇄ Synchronisieren",
            (LanguageCode::Es, "Btn_Sync") => "⇄ Sincronizar",
            (LanguageCode::Fr, "Btn_Sync") => "⇄ Synchroniser",
            (LanguageCode::It, "Btn_Sync") => "⇄ Sincronizza",
            (LanguageCode::Nl, "Btn_Sync") => "⇄ Synchroniseren",
            (LanguageCode::Pl, "Btn_Sync") => "⇄ Synchronizacja",
            (LanguageCode::Pt, "Btn_Sync") => "⇄ Sincronizar",
            (LanguageCode::Ja, "Btn_Sync") => "⇄ 同期",
            (LanguageCode::Zh, "Btn_Sync") => "⇄ 文件夹同步",
            (_, "Btn_Sync") => "⇄ Sync",

            (LanguageCode::De, "Btn_DeepSearch") => "🔍 Tiefensuche",
            (LanguageCode::Es, "Btn_DeepSearch") => "🔍 Búsqueda profunda",
            (LanguageCode::Fr, "Btn_DeepSearch") => "🔍 Recherche approfondie",
            (LanguageCode::It, "Btn_DeepSearch") => "🔍 Ricerca approfondita",
            (LanguageCode::Nl, "Btn_DeepSearch") => "🔍 Diep zoeken",
            (LanguageCode::Pl, "Btn_DeepSearch") => "🔍 Głębokie wyszukiwanie",
            (LanguageCode::Pt, "Btn_DeepSearch") => "🔍 Pesquisa profunda",
            (LanguageCode::Ja, "Btn_DeepSearch") => "🔍 詳細検索",
            (LanguageCode::Zh, "Btn_DeepSearch") => "🔍 深度搜索",
            (_, "Btn_DeepSearch") => "🔍 Deep Search",

            (LanguageCode::De, "Btn_Duplicates") => "🧹 Duplikate",
            (LanguageCode::Es, "Btn_Duplicates") => "🧹 Duplicados",
            (LanguageCode::Fr, "Btn_Duplicates") => "🧹 Doublons",
            (LanguageCode::It, "Btn_Duplicates") => "🧹 Duplicati",
            (LanguageCode::Nl, "Btn_Duplicates") => "🧹 Duplicaten",
            (LanguageCode::Pl, "Btn_Duplicates") => "🧹 Duplikaty",
            (LanguageCode::Pt, "Btn_Duplicates") => "🧹 Duplicatas",
            (LanguageCode::Ja, "Btn_Duplicates") => "🧹 重複ファイル",
            (LanguageCode::Zh, "Btn_Duplicates") => "🧹 重复文件清理",
            (_, "Btn_Duplicates") => "🧹 Duplicates",

            (LanguageCode::De, "Btn_Treemap") => "🗺 Speicherplatz (Treemap)",
            (LanguageCode::Es, "Btn_Treemap") => "🗺 Espacio en disco (Treemap)",
            (LanguageCode::Fr, "Btn_Treemap") => "🗺 Espace disque (Treemap)",
            (LanguageCode::It, "Btn_Treemap") => "🗺 Spazio su disco (Treemap)",
            (LanguageCode::Nl, "Btn_Treemap") => "🗺 Schijfruimte (Treemap)",
            (LanguageCode::Pl, "Btn_Treemap") => "🗺 Miejsce na dysku (Treemap)",
            (LanguageCode::Pt, "Btn_Treemap") => "🗺 Espaço em disco (Treemap)",
            (LanguageCode::Ja, "Btn_Treemap") => "🗺 ディスク容量ツリーマップ",
            (LanguageCode::Zh, "Btn_Treemap") => "🗺 磁盘空间树状图",
            (_, "Btn_Treemap") => "🗺 Treemap",

            (LanguageCode::De, "Btn_Pack") => "📦 Packen",
            (LanguageCode::Es, "Btn_Pack") => "📦 Comprimir",
            (LanguageCode::Fr, "Btn_Pack") => "📦 Compresser",
            (LanguageCode::It, "Btn_Pack") => "📦 Comprimi",
            (LanguageCode::Nl, "Btn_Pack") => "📦 Inpakken",
            (LanguageCode::Pl, "Btn_Pack") => "📦 Pakuj",
            (LanguageCode::Pt, "Btn_Pack") => "📦 Compactar",
            (LanguageCode::Ja, "Btn_Pack") => "📦 圧縮",
            (LanguageCode::Zh, "Btn_Pack") => "📦 压缩打包",
            (_, "Btn_Pack") => "📦 Pack",

            (LanguageCode::De, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Es, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Fr, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::It, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Nl, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Pl, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Pt, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Ja, "Btn_Remote") => "🌐 FTP/SFTP",
            (LanguageCode::Zh, "Btn_Remote") => "🌐 FTP/SFTP",
            (_, "Btn_Remote") => "🌐 FTP/SFTP",

            (LanguageCode::De, "Btn_Preview") => "👁 Vorschau",
            (LanguageCode::Es, "Btn_Preview") => "👁 Vista previa",
            (LanguageCode::Fr, "Btn_Preview") => "👁 Aperçu",
            (LanguageCode::It, "Btn_Preview") => "👁 Anteprima",
            (LanguageCode::Nl, "Btn_Preview") => "👁 Voorbeeld",
            (LanguageCode::Pl, "Btn_Preview") => "👁 Podgląd",
            (LanguageCode::Pt, "Btn_Preview") => "👁 Pré-visualização",
            (LanguageCode::Ja, "Btn_Preview") => "👁 プレビュー",
            (LanguageCode::Zh, "Btn_Preview") => "👁 快速预览",
            (_, "Btn_Preview") => "👁 Preview",

            (LanguageCode::De, "Btn_Settings") => "⚙ Einstellungen",
            (LanguageCode::Es, "Btn_Settings") => "⚙ Configuración",
            (LanguageCode::Fr, "Btn_Settings") => "⚙ Paramètres",
            (LanguageCode::It, "Btn_Settings") => "⚙ Impostazioni",
            (LanguageCode::Nl, "Btn_Settings") => "⚙ Instellingen",
            (LanguageCode::Pl, "Btn_Settings") => "⚙ Ustawienia",
            (LanguageCode::Pt, "Btn_Settings") => "⚙ Configurações",
            (LanguageCode::Ja, "Btn_Settings") => "⚙ 設定",
            (LanguageCode::Zh, "Btn_Settings") => "⚙ 设置",
            (_, "Btn_Settings") => "⚙ Settings",

            (LanguageCode::De, "Btn_About") => "ℹ Über / Update",
            (LanguageCode::Es, "Btn_About") => "ℹ Acerca de / Actualización",
            (LanguageCode::Fr, "Btn_About") => "ℹ À propos / Mise à jour",
            (LanguageCode::It, "Btn_About") => "ℹ Informazioni / Aggiorna",
            (LanguageCode::Nl, "Btn_About") => "ℹ Over / Update",
            (LanguageCode::Pl, "Btn_About") => "ℹ O programie / Aktualizacja",
            (LanguageCode::Pt, "Btn_About") => "ℹ Sobre / Atualização",
            (LanguageCode::Ja, "Btn_About") => "ℹ 情報 / アップデート",
            (LanguageCode::Zh, "Btn_About") => "ℹ 关于与更新",
            (_, "Btn_About") => "ℹ About / Update",

            (LanguageCode::De, "Btn_ColorPicker") => "🎨 Farbpicker",
            (LanguageCode::Es, "Btn_ColorPicker") => "🎨 Selector de color",
            (LanguageCode::Fr, "Btn_ColorPicker") => "🎨 Sélecteur de couleur",
            (LanguageCode::It, "Btn_ColorPicker") => "🎨 Selettore colore",
            (LanguageCode::Nl, "Btn_ColorPicker") => "🎨 Kleurkiezer",
            (LanguageCode::Pl, "Btn_ColorPicker") => "🎨 Selektor kolorów",
            (LanguageCode::Pt, "Btn_ColorPicker") => "🎨 Seletor de cores",
            (LanguageCode::Ja, "Btn_ColorPicker") => "🎨 カラーピッカー",
            (LanguageCode::Zh, "Btn_ColorPicker") => "🎨 颜色选择器",
            (_, "Btn_ColorPicker") => "🎨 Color Picker",

            (LanguageCode::De, "Btn_ResetDefault") => "↺ Standardfarbe",
            (LanguageCode::Es, "Btn_ResetDefault") => "↺ Color predeterminado",
            (LanguageCode::Fr, "Btn_ResetDefault") => "↺ Couleur par défaut",
            (LanguageCode::It, "Btn_ResetDefault") => "↺ Colore predefinito",
            (LanguageCode::Nl, "Btn_ResetDefault") => "↺ Standaardkleur",
            (LanguageCode::Pl, "Btn_ResetDefault") => "↺ Domyślny kolor",
            (LanguageCode::Pt, "Btn_ResetDefault") => "↺ Cor padrão",
            (LanguageCode::Ja, "Btn_ResetDefault") => "↺ デフォルト色",
            (LanguageCode::Zh, "Btn_ResetDefault") => "↺ 默认颜色",
            (_, "Btn_ResetDefault") => "↺ Default Color",

            (LanguageCode::De, "Update_Available_Title") => "Update verfügbar",
            (LanguageCode::Es, "Update_Available_Title") => "Actualización disponible",
            (LanguageCode::Fr, "Update_Available_Title") => "Mise à jour disponible",
            (LanguageCode::It, "Update_Available_Title") => "Aggiornamento disponibile",
            (LanguageCode::Nl, "Update_Available_Title") => "Update beschikbaar",
            (LanguageCode::Pl, "Update_Available_Title") => "Dostępna aktualizacja",
            (LanguageCode::Pt, "Update_Available_Title") => "Atualização disponível",
            (LanguageCode::Ja, "Update_Available_Title") => "アップデートが利用可能です",
            (LanguageCode::Zh, "Update_Available_Title") => "有新版本可用",
            (_, "Update_Available_Title") => "Update Available",

            (LanguageCode::De, "Update_ReleaseNotes_Header") => "📋 Änderungen in diesem Release:",
            (LanguageCode::Es, "Update_ReleaseNotes_Header") => "📋 Cambios en esta versión:",
            (LanguageCode::Fr, "Update_ReleaseNotes_Header") => "📋 Modifications de cette version :",
            (LanguageCode::It, "Update_ReleaseNotes_Header") => "📋 Modifiche in questa versione:",
            (LanguageCode::Nl, "Update_ReleaseNotes_Header") => "📋 Wijzigingen in deze release:",
            (LanguageCode::Pl, "Update_ReleaseNotes_Header") => "📋 Zmiany w tej wersji:",
            (LanguageCode::Pt, "Update_ReleaseNotes_Header") => "📋 Alterações nesta versão:",
            (LanguageCode::Ja, "Update_ReleaseNotes_Header") => "📋 このリリースの変更点:",
            (LanguageCode::Zh, "Update_ReleaseNotes_Header") => "📋 本次版本更新内容:",
            (_, "Update_ReleaseNotes_Header") => "📋 Changes in this Release:",

            (LanguageCode::De, "Update_ViewOnGitHub") => "🌐 Auf GitHub ansehen ↗",
            (LanguageCode::Es, "Update_ViewOnGitHub") => "🌐 Ver en GitHub ↗",
            (LanguageCode::Fr, "Update_ViewOnGitHub") => "🌐 Voir sur GitHub ↗",
            (LanguageCode::It, "Update_ViewOnGitHub") => "🌐 Visualizza su GitHub ↗",
            (LanguageCode::Nl, "Update_ViewOnGitHub") => "🌐 Bekijken op GitHub ↗",
            (LanguageCode::Pl, "Update_ViewOnGitHub") => "🌐 Zobacz na GitHubie ↗",
            (LanguageCode::Pt, "Update_ViewOnGitHub") => "🌐 Ver no GitHub ↗",
            (LanguageCode::Ja, "Update_ViewOnGitHub") => "🌐 GitHubで確認 ↗",
            (LanguageCode::Zh, "Update_ViewOnGitHub") => "🌐 在 GitHub 上查看 ↗",
            (_, "Update_ViewOnGitHub") => "🌐 View on GitHub ↗",

            (LanguageCode::De, "Treemap_Cat_VideoMedia") => "🎬 Video & Audio",
            (LanguageCode::Es, "Treemap_Cat_VideoMedia") => "🎬 Vídeo y Audio",
            (LanguageCode::Fr, "Treemap_Cat_VideoMedia") => "🎬 Vidéo & Audio",
            (LanguageCode::It, "Treemap_Cat_VideoMedia") => "🎬 Video e Audio",
            (LanguageCode::Nl, "Treemap_Cat_VideoMedia") => "🎬 Video & Audio",
            (LanguageCode::Pl, "Treemap_Cat_VideoMedia") => "🎬 Wideo i Dźwięk",
            (LanguageCode::Pt, "Treemap_Cat_VideoMedia") => "🎬 Vídeo e Áudio",
            (LanguageCode::Ja, "Treemap_Cat_VideoMedia") => "🎬 動画と音楽",
            (LanguageCode::Zh, "Treemap_Cat_VideoMedia") => "🎬 视频与音频",
            (_, "Treemap_Cat_VideoMedia") => "🎬 Video & Audio",

            (LanguageCode::De, "Treemap_Cat_ArchiveImage") => "📦 Archive & Disk-Images",
            (LanguageCode::Es, "Treemap_Cat_ArchiveImage") => "📦 Archivos e Imágenes de disco",
            (LanguageCode::Fr, "Treemap_Cat_ArchiveImage") => "📦 Archives & Images disque",
            (LanguageCode::It, "Treemap_Cat_ArchiveImage") => "📦 Archivi e Immagini disco",
            (LanguageCode::Nl, "Treemap_Cat_ArchiveImage") => "📦 Archieven & Schijfkopieën",
            (LanguageCode::Pl, "Treemap_Cat_ArchiveImage") => "📦 Archiwa i Obrazy płyt",
            (LanguageCode::Pt, "Treemap_Cat_ArchiveImage") => "📦 Arquivos e Imagens de disco",
            (LanguageCode::Ja, "Treemap_Cat_ArchiveImage") => "📦 アーカイブ & ディスクイメージ",
            (LanguageCode::Zh, "Treemap_Cat_ArchiveImage") => "📦 压缩包与磁盘映像",
            (_, "Treemap_Cat_ArchiveImage") => "📦 Archives & Disk Images",

            (LanguageCode::De, "Treemap_Cat_VirtualDiskSystem") => "💿 Virtuelle Disks, DB & System",
            (LanguageCode::Es, "Treemap_Cat_VirtualDiskSystem") => "💿 Discos virtuales, BD y Sistema",
            (LanguageCode::Fr, "Treemap_Cat_VirtualDiskSystem") => "💿 Disques virtuels, BD & Système",
            (LanguageCode::It, "Treemap_Cat_VirtualDiskSystem") => "💿 Dischi virtuali, DB e Sistema",
            (LanguageCode::Nl, "Treemap_Cat_VirtualDiskSystem") => "💿 Virtuele schijven, DB & Systeem",
            (LanguageCode::Pl, "Treemap_Cat_VirtualDiskSystem") => "💿 Wirtualne dyski, bazy danych i system",
            (LanguageCode::Pt, "Treemap_Cat_VirtualDiskSystem") => "💿 Discos virtuais, BD e Sistema",
            (LanguageCode::Ja, "Treemap_Cat_VirtualDiskSystem") => "💿 仮想ディスク、DB & システム",
            (LanguageCode::Zh, "Treemap_Cat_VirtualDiskSystem") => "💿 虚拟磁盘、数据库与系统",
            (_, "Treemap_Cat_VirtualDiskSystem") => "💿 Virtual Disks, DB & System",

            (LanguageCode::De, "Treemap_Cat_CodeDocument") => "📝 Code, Text & Dokumente",
            (LanguageCode::Es, "Treemap_Cat_CodeDocument") => "📝 Código, Texto y Documentos",
            (LanguageCode::Fr, "Treemap_Cat_CodeDocument") => "📝 Code, Texte & Documents",
            (LanguageCode::It, "Treemap_Cat_CodeDocument") => "📝 Codice, Testo e Documenti",
            (LanguageCode::Nl, "Treemap_Cat_CodeDocument") => "📝 Code, Tekst & Documenten",
            (LanguageCode::Pl, "Treemap_Cat_CodeDocument") => "📝 Kod, Tekst i Dokumenty",
            (LanguageCode::Pt, "Treemap_Cat_CodeDocument") => "📝 Código, Texto e Documentos",
            (LanguageCode::Ja, "Treemap_Cat_CodeDocument") => "📝 ソースコード、テキスト & 文書",
            (LanguageCode::Zh, "Treemap_Cat_CodeDocument") => "📝 源代码、文本与文档",
            (_, "Treemap_Cat_CodeDocument") => "📝 Code, Text & Documents",

            (LanguageCode::De, "Treemap_Cat_PictureGraphic") => "🖼 Bilder & Grafiken",
            (LanguageCode::Es, "Treemap_Cat_PictureGraphic") => "🖼 Imágenes y Gráficos",
            (LanguageCode::Fr, "Treemap_Cat_PictureGraphic") => "🖼 Images & Graphiques",
            (LanguageCode::It, "Treemap_Cat_PictureGraphic") => "🖼 Immagini e Grafica",
            (LanguageCode::Nl, "Treemap_Cat_PictureGraphic") => "🖼 Afbeeldingen & Grafieken",
            (LanguageCode::Pl, "Treemap_Cat_PictureGraphic") => "🖼 Obrazy i Grafika",
            (LanguageCode::Pt, "Treemap_Cat_PictureGraphic") => "🖼 Imagens e Gráficos",
            (LanguageCode::Ja, "Treemap_Cat_PictureGraphic") => "🖼 画像とイラスト",
            (LanguageCode::Zh, "Treemap_Cat_PictureGraphic") => "🖼 图片与图像",
            (_, "Treemap_Cat_PictureGraphic") => "🖼 Pictures & Graphics",

            (LanguageCode::De, "Treemap_Cat_BinaryExecutable") => "⚙ Programme & Bibliotheken",
            (LanguageCode::Es, "Treemap_Cat_BinaryExecutable") => "⚙ Programas y Bibliotecas",
            (LanguageCode::Fr, "Treemap_Cat_BinaryExecutable") => "⚙ Programmes & Bibliothèques",
            (LanguageCode::It, "Treemap_Cat_BinaryExecutable") => "⚙ Programmi e Librerie",
            (LanguageCode::Nl, "Treemap_Cat_BinaryExecutable") => "⚙ Programma's & Bibliotheken",
            (LanguageCode::Pl, "Treemap_Cat_BinaryExecutable") => "⚙ Programy i Biblioteki",
            (LanguageCode::Pt, "Treemap_Cat_BinaryExecutable") => "⚙ Programas e Bibliotecas",
            (LanguageCode::Ja, "Treemap_Cat_BinaryExecutable") => "⚙ プログラム & ライブラリ",
            (LanguageCode::Zh, "Treemap_Cat_BinaryExecutable") => "⚙ 应用程序与运行库",
            (_, "Treemap_Cat_BinaryExecutable") => "⚙ Binaries & Libraries",

            (LanguageCode::De, "Treemap_Cat_OtherTemp") => "📄 Sonstige & Temporäre Dateien",
            (LanguageCode::Es, "Treemap_Cat_OtherTemp") => "📄 Otros y Archivos temporales",
            (LanguageCode::Fr, "Treemap_Cat_OtherTemp") => "📄 Autres & Fichiers temporaires",
            (LanguageCode::It, "Treemap_Cat_OtherTemp") => "📄 Altro e File temporanei",
            (LanguageCode::Nl, "Treemap_Cat_OtherTemp") => "📄 Overige & Tijdelijke bestanden",
            (LanguageCode::Pl, "Treemap_Cat_OtherTemp") => "📄 Inne i Pliki tymczasowe",
            (LanguageCode::Pt, "Treemap_Cat_OtherTemp") => "📄 Outros e Arquivos temporários",
            (LanguageCode::Ja, "Treemap_Cat_OtherTemp") => "📄 その他 & 一時ファイル",
            (LanguageCode::Zh, "Treemap_Cat_OtherTemp") => "📄 其他与临时文件",
            (_, "Treemap_Cat_OtherTemp") => "📄 Other & Temporary Files",

            _ => key,
        }
    }
}

