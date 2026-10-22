#define MyAppName "Opsis - Desktop Customization"
#define MyAppVersion "2.1.2"
#define MyAppPublisher "Opsis Team"
#define MyAppExeName "Opsis.exe"
; Πού βρίσκεται ο installer του News Crawler (δημοσιεύεται ως GitHub release του αντίστοιχου repo).
; ΠΡΟΣΟΧΗ: το URL είναι η υπόθεση για το repo του crawler — άλλαξέ το αν ο installer ανέβει αλλού.
; Opsis Appearance (plugin): το installer του δημοσιεύεται ως GitHub release του αντίστοιχου repo (ίδιος μηχανισμός με τον crawler).
#ifndef AppearanceSetupUrl
  #define AppearanceSetupUrl "https://github.com/giorstergiopoulos-design/OpsisAppearance/releases/latest/download/OpsisAppearance-Setup.exe"
#endif
#ifndef CrawlerSetupUrl
  #define CrawlerSetupUrl "https://github.com/giorstergiopoulos-design/OpsisNewsCrawler/releases/latest/download/OpsisNewsCrawler-Setup.exe"
#endif

; Κοινή σελίδα εξαρτήσεων (.NET 10 Desktop Runtime / WebView2 Runtime) με τον installer του News
; Crawler — βλ. το header comment μέσα στο ίδιο το αρχείο για το πλήρες hook-up. Αντικαθιστά το
; παλιό σιωπηλό bundled-redist μηχανισμό παρακάτω: δείχνει στον χρήστη τι χρειάζεται/υπάρχει ήδη,
; κατεβάζει μόνο ό,τι λείπει (με επαλήθευση ψηφιακής υπογραφής Microsoft πριν την εκτέλεση).
#include "installer\dependencies.iss"

[Setup]
; Μοναδικό GUID για την εφαρμογή (μην το αλλάξεις σε μελλοντικές εκδόσεις)
AppId={{A9E8F765-1234-4567-89AB-CDEF01234567}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=OpsisSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
AppMutex=Opsis.SingleInstance
; Η εφαρμογή είναι win-x64 (framework-dependent, native WebView2Loader.dll x64) — ο installer
; πρέπει να τρέχει ΜΟΝΟ σε 64-bit-ικανά συστήματα, όχι σε καθαρό x86. Το "x64compatible" είναι
; το σύγχρονο identifier (το απλό "x64" είναι πλέον deprecated στο Inno Setup 6.7+).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=assets\Opsis.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableWelcomePage=no

[CustomMessages]
greek.UpdateDetected=Εντοπίστηκε ήδη εγκατεστημένη έκδοση %1 του {#MyAppName}.%n%nΘα ενημερωθεί στην έκδοση {#MyAppVersion}.
english.UpdateDetected=An existing installation of {#MyAppName} version %1 was detected.%n%nIt will be updated to version {#MyAppVersion}.
german.UpdateDetected=Eine vorhandene Installation von {#MyAppName} in Version %1 wurde erkannt.%n%nSie wird auf Version {#MyAppVersion} aktualisiert.
french.UpdateDetected=Une installation existante de {#MyAppName} version %1 a été détectée.%n%nElle sera mise à jour vers la version {#MyAppVersion}.
spanish.UpdateDetected=Se detectó una instalación existente de {#MyAppName} versión %1.%n%nSe actualizará a la versión {#MyAppVersion}.
korean.UpdateDetected={#MyAppName} 버전 %1이(가) 이미 설치되어 있습니다.%n%n버전 {#MyAppVersion}(으)로 업데이트됩니다.
chinese.UpdateDetected=检测到已安装 {#MyAppName} 版本 %1。%n%n将更新到版本 {#MyAppVersion}。
italian.UpdateDetected=È stata rilevata un'installazione esistente di {#MyAppName} versione %1.%n%nVerrà aggiornata alla versione {#MyAppVersion}.
russian.UpdateDetected=Обнаружена существующая установка {#MyAppName} версии %1.%n%nОна будет обновлена до версии {#MyAppVersion}.
japanese.UpdateDetected={#MyAppName} バージョン %1 の既存のインストールが検出されました。%n%nバージョン {#MyAppVersion} に更新されます。
portuguese.UpdateDetected=Foi detetada uma instalação existente do {#MyAppName} versão %1.%n%nSerá atualizada para a versão {#MyAppVersion}.
turkish.UpdateDetected={#MyAppName} uygulamasının %1 sürümü zaten yüklü.%n%nSürüm {#MyAppVersion} olarak güncellenecek.
arabic.UpdateDetected=تم اكتشاف تثبيت موجود من {#MyAppName} الإصدار %1.%n%nسيتم تحديثه إلى الإصدار {#MyAppVersion}.
hindi.UpdateDetected={#MyAppName} संस्करण %1 का मौजूदा इंस्टॉलेशन मिला।%n%nइसे संस्करण {#MyAppVersion} में अपडेट किया जाएगा।
greek.KeepDataPromptText=Να διαγραφούν επίσης οι ρυθμίσεις, τα προφίλ και τα αποθηκευμένα DeskZones/DeskContainers του {#MyAppName};
english.KeepDataPromptText=Do you also want to delete {#MyAppName}'s settings, profiles and saved DeskZones/DeskContainers?
german.KeepDataPromptText=Sollen auch die Einstellungen, Profile und gespeicherten DeskZones/DeskContainers von {#MyAppName} gelöscht werden?
french.KeepDataPromptText=Voulez-vous aussi supprimer les paramètres, profils et DeskZones/DeskContainers enregistrés de {#MyAppName} ?
spanish.KeepDataPromptText=¿Desea eliminar también la configuración, los perfiles y los DeskZones/DeskContainers guardados de {#MyAppName}?
korean.KeepDataPromptText={#MyAppName}의 설정, 프로필, 저장된 DeskZones/DeskContainers도 삭제하시겠습니까?
chinese.KeepDataPromptText=是否同时删除 {#MyAppName} 的设置、配置文件以及已保存的 DeskZones/DeskContainers？
italian.KeepDataPromptText=Vuoi eliminare anche le impostazioni, i profili e i DeskZones/DeskContainers salvati di {#MyAppName}?
russian.KeepDataPromptText=Удалить также настройки, профили и сохранённые DeskZones/DeskContainers приложения {#MyAppName}?
japanese.KeepDataPromptText={#MyAppName} の設定、プロファイル、保存済みの DeskZones/DeskContainers も削除しますか？
portuguese.KeepDataPromptText=Deseja eliminar também as definições, os perfis e os DeskZones/DeskContainers guardados do {#MyAppName}?
turkish.KeepDataPromptText={#MyAppName} uygulamasının ayarları, profilleri ve kayıtlı DeskZones/DeskContainers öğeleri de silinsin mi?
arabic.KeepDataPromptText=هل تريد أيضًا حذف إعدادات {#MyAppName} وملفاته الشخصية وDeskZones/DeskContainers المحفوظة؟
hindi.KeepDataPromptText=क्या आप {#MyAppName} की सेटिंग्स, प्रोफ़ाइल और सहेजे गए DeskZones/DeskContainers भी हटाना चाहते हैं?
greek.AutostartTask=Αυτόματη εκκίνηση με την είσοδο στα Windows (System Tray)
english.AutostartTask=Start automatically when signing in to Windows (System Tray)
german.AutostartTask=Automatisch beim Anmelden bei Windows starten (Infobereich)
french.AutostartTask=Démarrer automatiquement à l'ouverture de session Windows (zone de notification)
spanish.AutostartTask=Iniciar automáticamente al iniciar sesión en Windows (bandeja del sistema)
korean.AutostartTask=Windows 로그인 시 자동으로 시작(시스템 트레이)
chinese.AutostartTask=登录 Windows 时自动启动（系统托盘）
italian.AutostartTask=Avvia automaticamente all'accesso a Windows (area di notifica)
russian.AutostartTask=Запускать автоматически при входе в Windows (область уведомлений)
japanese.AutostartTask=Windows へのサインイン時に自動的に起動する (システム トレイ)
portuguese.AutostartTask=Iniciar automaticamente ao iniciar sessão no Windows (área de notificação)
turkish.AutostartTask=Windows'ta oturum açılırken otomatik başlat (Sistem Tepsisi)
arabic.AutostartTask=البدء تلقائيًا عند تسجيل الدخول إلى Windows (علبة النظام)
hindi.AutostartTask=Windows में साइन इन करते समय अपने आप शुरू करें (सिस्टम ट्रे)
greek.StartupGroup=Ρυθμίσεις Εκκίνησης:
english.StartupGroup=Startup settings:
german.StartupGroup=Starteinstellungen:
french.StartupGroup=Paramètres de démarrage :
spanish.StartupGroup=Configuración de inicio:
korean.StartupGroup=시작 설정:
chinese.StartupGroup=启动设置：
italian.StartupGroup=Impostazioni di avvio:
russian.StartupGroup=Параметры запуска:
japanese.StartupGroup=起動設定:
portuguese.StartupGroup=Definições de arranque:
turkish.StartupGroup=Başlangıç ayarları:
arabic.StartupGroup=إعدادات بدء التشغيل:
hindi.StartupGroup=स्टार्टअप सेटिंग्स:
greek.FfmpegTask=Εγκατάσταση FFmpeg (για αναπαραγωγή βίντεο .wmv ως κινούμενο wallpaper)
english.FfmpegTask=Install FFmpeg (to play .wmv videos as an animated wallpaper)
german.FfmpegTask=FFmpeg installieren (zum Abspielen von .wmv-Videos als animierter Hintergrund)
french.FfmpegTask=Installer FFmpeg (pour lire les vidéos .wmv comme fond d'écran animé)
spanish.FfmpegTask=Instalar FFmpeg (para reproducir vídeos .wmv como fondo animado)
korean.FfmpegTask=FFmpeg 설치(.wmv 동영상을 애니메이션 배경 화면으로 재생하기 위함)
chinese.FfmpegTask=安装 FFmpeg（用于将 .wmv 视频作为动态壁纸播放）
italian.FfmpegTask=Installa FFmpeg (per riprodurre i video .wmv come sfondo animato)
russian.FfmpegTask=Установить FFmpeg (для воспроизведения видео .wmv как живых обоев)
japanese.FfmpegTask=FFmpeg をインストール (.wmv 動画をアニメーション壁紙として再生するため)
portuguese.FfmpegTask=Instalar o FFmpeg (para reproduzir vídeos .wmv como fundo animado)
turkish.FfmpegTask=FFmpeg'i yükle (.wmv videolarını animasyonlu duvar kâğıdı olarak oynatmak için)
arabic.FfmpegTask=تثبيت FFmpeg (لتشغيل فيديوهات .wmv كخلفية متحركة)
hindi.FfmpegTask=FFmpeg इंस्टॉल करें (.wmv वीडियो को एनिमेटेड वॉलपेपर के रूप में चलाने के लिए)
greek.ExtraDepsGroup=Πρόσθετες εξαρτήσεις:
english.ExtraDepsGroup=Additional dependencies:
german.ExtraDepsGroup=Zusätzliche Abhängigkeiten:
french.ExtraDepsGroup=Dépendances supplémentaires :
spanish.ExtraDepsGroup=Dependencias adicionales:
korean.ExtraDepsGroup=추가 종속성:
chinese.ExtraDepsGroup=附加依赖项：
italian.ExtraDepsGroup=Dipendenze aggiuntive:
russian.ExtraDepsGroup=Дополнительные зависимости:
japanese.ExtraDepsGroup=追加の依存関係:
portuguese.ExtraDepsGroup=Dependências adicionais:
turkish.ExtraDepsGroup=Ek bağımlılıklar:
arabic.ExtraDepsGroup=اعتماديات إضافية:
hindi.ExtraDepsGroup=अतिरिक्त निर्भरताएँ:
greek.InstallingWebView2=Εγκατάσταση Microsoft Edge WebView2 Runtime...
english.InstallingWebView2=Installing Microsoft Edge WebView2 Runtime...
german.InstallingWebView2=Microsoft Edge WebView2 Runtime wird installiert...
french.InstallingWebView2=Installation de Microsoft Edge WebView2 Runtime...
spanish.InstallingWebView2=Instalando Microsoft Edge WebView2 Runtime...
korean.InstallingWebView2=Microsoft Edge WebView2 Runtime 설치 중...
chinese.InstallingWebView2=正在安装 Microsoft Edge WebView2 Runtime...
italian.InstallingWebView2=Installazione di Microsoft Edge WebView2 Runtime...
russian.InstallingWebView2=Установка Microsoft Edge WebView2 Runtime...
japanese.InstallingWebView2=Microsoft Edge WebView2 Runtime をインストールしています...
portuguese.InstallingWebView2=A instalar o Microsoft Edge WebView2 Runtime...
turkish.InstallingWebView2=Microsoft Edge WebView2 Runtime yükleniyor...
arabic.InstallingWebView2=جارٍ تثبيت Microsoft Edge WebView2 Runtime...
hindi.InstallingWebView2=Microsoft Edge WebView2 Runtime इंस्टॉल हो रहा है...
greek.InstallingDotNet=Εγκατάσταση .NET 10.0 Desktop Runtime...
english.InstallingDotNet=Installing .NET 10.0 Desktop Runtime...
german.InstallingDotNet=.NET 10.0 Desktop Runtime wird installiert...
french.InstallingDotNet=Installation de .NET 10.0 Desktop Runtime...
spanish.InstallingDotNet=Instalando .NET 10.0 Desktop Runtime...
korean.InstallingDotNet=.NET 10.0 Desktop Runtime 설치 중...
chinese.InstallingDotNet=正在安装 .NET 10.0 Desktop Runtime...
italian.InstallingDotNet=Installazione di .NET 10.0 Desktop Runtime...
russian.InstallingDotNet=Установка .NET 10.0 Desktop Runtime...
japanese.InstallingDotNet=.NET 10.0 Desktop Runtime をインストールしています...
portuguese.InstallingDotNet=A instalar o .NET 10.0 Desktop Runtime...
turkish.InstallingDotNet=.NET 10.0 Desktop Runtime yükleniyor...
arabic.InstallingDotNet=جارٍ تثبيت .NET 10.0 Desktop Runtime...
hindi.InstallingDotNet=.NET 10.0 Desktop Runtime इंस्टॉल हो रहा है...
greek.InstallingFfmpeg=Εγκατάσταση FFmpeg...
english.InstallingFfmpeg=Installing FFmpeg...
german.InstallingFfmpeg=FFmpeg wird installiert...
french.InstallingFfmpeg=Installation de FFmpeg...
spanish.InstallingFfmpeg=Instalando FFmpeg...
korean.InstallingFfmpeg=FFmpeg 설치 중...
chinese.InstallingFfmpeg=正在安装 FFmpeg...
italian.InstallingFfmpeg=Installazione di FFmpeg...
russian.InstallingFfmpeg=Установка FFmpeg...
japanese.InstallingFfmpeg=FFmpeg をインストールしています...
portuguese.InstallingFfmpeg=A instalar o FFmpeg...
turkish.InstallingFfmpeg=FFmpeg yükleniyor...
arabic.InstallingFfmpeg=جارٍ تثبيت FFmpeg...
hindi.InstallingFfmpeg=FFmpeg इंस्टॉल हो रहा है...

; Ρητό αίτημα χρήστη: "να έχει πληροφορίες και δομή και λειτουργίες όπως του gearwin" — ίδιο μοτίβο
; με τον installer του GearWin (../installer/OptimizerWpf.iss): σελίδα "Πληροφορίες" (InfoBeforeFile)
; πριν την επιλογή φακέλου, υποχρεωτική σελίδα άδειας (LicenseFile) ανά γλώσσα, μήνυμα ανίχνευσης
; ήδη-εγκατεστημένης έκδοσης, και ερώτηση επιβεβαίωσης στην απεγκατάσταση. Από την 1.7.8 η εφαρμογή (LocalizationManager, locales/)
; υποστηρίζει 14 γλώσσες, όπως το GearWin, και ο installer τις καλύπτει όλες. Chinese.isl/Hindi.isl είναι custom
; (αντιγραφή από το GearWin installer), τα υπόλοιπα .isl έρχονται με το Inno Setup.
[Languages]
Name: "greek"; MessagesFile: "installer\Greek.isl"; LicenseFile: "installer\license_el.txt"; InfoBeforeFile: "installer\app_description_el.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "installer\license_en.txt"; InfoBeforeFile: "installer\app_description_en.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "installer\license_de.txt"; InfoBeforeFile: "installer\app_description_de.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "installer\license_fr.txt"; InfoBeforeFile: "installer\app_description_fr.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "installer\license_es.txt"; InfoBeforeFile: "installer\app_description_es.txt"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"; LicenseFile: "installer\license_ko.txt"; InfoBeforeFile: "installer\app_description_ko.txt"
Name: "chinese"; MessagesFile: "installer\Chinese.isl"; LicenseFile: "installer\license_zh.txt"; InfoBeforeFile: "installer\app_description_zh.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; LicenseFile: "installer\license_it.txt"; InfoBeforeFile: "installer\app_description_it.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "installer\license_ru.txt"; InfoBeforeFile: "installer\app_description_ru.txt"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; LicenseFile: "installer\license_ja.txt"; InfoBeforeFile: "installer\app_description_ja.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; LicenseFile: "installer\license_pt.txt"; InfoBeforeFile: "installer\app_description_pt.txt"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"; LicenseFile: "installer\license_tr.txt"; InfoBeforeFile: "installer\app_description_tr.txt"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"; LicenseFile: "installer\license_ar.txt"; InfoBeforeFile: "installer\app_description_ar.txt"
Name: "hindi"; MessagesFile: "installer\Hindi.isl"; LicenseFile: "installer\license_hi.txt"; InfoBeforeFile: "installer\app_description_hi.txt"

; News Crawler (plugin): λήψη και εγκατάσταση ταυτόχρονα με το Opsis — ο crawler ΔΕΝ περιλαμβάνεται στο παρόν installer.
[CustomMessages]
greek.CrawlerTask=Λήψη και εγκατάσταση του News Crawler (μπάρα ειδήσεων και τιμών, plugin του Opsis)
english.CrawlerTask=Download and install News Crawler (news and price bar, a plugin of Opsis)
german.CrawlerTask=News Crawler herunterladen und installieren (Nachrichten- und Kursleiste, ein Plugin von Opsis)
french.CrawlerTask=Télécharger et installer News Crawler (barre d'actualités et de cours, plugin d'Opsis)
spanish.CrawlerTask=Descargar e instalar News Crawler (barra de noticias y precios, un plugin de Opsis)
korean.CrawlerTask=News Crawler 다운로드 및 설치(뉴스·시세 바, Opsis 플러그인)
chinese.CrawlerTask=下载并安装 News Crawler（新闻和行情栏，Opsis 插件）
italian.CrawlerTask=Scarica e installa News Crawler (barra di notizie e quotazioni, plugin di Opsis)
russian.CrawlerTask=Скачать и установить News Crawler (панель новостей и котировок, плагин Opsis)
japanese.CrawlerTask=News Crawler をダウンロードしてインストール (ニュースと相場のバー、Opsis のプラグイン)
portuguese.CrawlerTask=Transferir e instalar o News Crawler (barra de notícias e cotações, plugin do Opsis)
turkish.CrawlerTask=News Crawler'ı indir ve yükle (haber ve fiyat çubuğu, Opsis eklentisi)
arabic.CrawlerTask=تنزيل News Crawler وتثبيته (شريط الأخبار والأسعار، إضافة لـ Opsis)
hindi.CrawlerTask=News Crawler डाउनलोड और इंस्टॉल करें (समाचार और भाव पट्टी, Opsis का प्लगइन)
greek.CrawlerDownloadTitle=Λήψη του News Crawler
english.CrawlerDownloadTitle=Downloading News Crawler
german.CrawlerDownloadTitle=News Crawler wird heruntergeladen
french.CrawlerDownloadTitle=Téléchargement de News Crawler
spanish.CrawlerDownloadTitle=Descargando News Crawler
korean.CrawlerDownloadTitle=News Crawler 다운로드 중
chinese.CrawlerDownloadTitle=正在下载 News Crawler
italian.CrawlerDownloadTitle=Download di News Crawler
russian.CrawlerDownloadTitle=Загрузка News Crawler
japanese.CrawlerDownloadTitle=News Crawler をダウンロードしています
portuguese.CrawlerDownloadTitle=A transferir o News Crawler
turkish.CrawlerDownloadTitle=News Crawler indiriliyor
arabic.CrawlerDownloadTitle=جارٍ تنزيل News Crawler
hindi.CrawlerDownloadTitle=News Crawler डाउनलोड हो रहा है
greek.CrawlerDownloadDesc=Περιμένετε όσο κατεβαίνει ο installer του News Crawler.
english.CrawlerDownloadDesc=Please wait while the News Crawler installer is downloaded.
german.CrawlerDownloadDesc=Bitte warten Sie, während das News-Crawler-Installationsprogramm heruntergeladen wird.
french.CrawlerDownloadDesc=Veuillez patienter pendant le téléchargement du programme d'installation de News Crawler.
spanish.CrawlerDownloadDesc=Espere mientras se descarga el instalador de News Crawler.
korean.CrawlerDownloadDesc=News Crawler 설치 프로그램을 다운로드하는 동안 기다려 주세요.
chinese.CrawlerDownloadDesc=请稍候，正在下载 News Crawler 安装程序。
italian.CrawlerDownloadDesc=Attendere il download del programma di installazione di News Crawler.
russian.CrawlerDownloadDesc=Подождите, пока загружается установщик News Crawler.
japanese.CrawlerDownloadDesc=News Crawler のインストーラーをダウンロードしています。しばらくお待ちください。
portuguese.CrawlerDownloadDesc=Aguarde enquanto o instalador do News Crawler é transferido.
turkish.CrawlerDownloadDesc=News Crawler yükleyicisi indirilirken lütfen bekleyin.
arabic.CrawlerDownloadDesc=يرجى الانتظار أثناء تنزيل برنامج تثبيت News Crawler.
hindi.CrawlerDownloadDesc=News Crawler इंस्टॉलर डाउनलोड होने तक प्रतीक्षा करें।
greek.CrawlerInstalling=Εγκατάσταση του News Crawler...
english.CrawlerInstalling=Installing News Crawler...
german.CrawlerInstalling=News Crawler wird installiert...
french.CrawlerInstalling=Installation de News Crawler...
spanish.CrawlerInstalling=Instalando News Crawler...
korean.CrawlerInstalling=News Crawler 설치 중...
chinese.CrawlerInstalling=正在安装 News Crawler...
italian.CrawlerInstalling=Installazione di News Crawler...
russian.CrawlerInstalling=Установка News Crawler...
japanese.CrawlerInstalling=News Crawler をインストールしています...
portuguese.CrawlerInstalling=A instalar o News Crawler...
turkish.CrawlerInstalling=News Crawler yükleniyor...
arabic.CrawlerInstalling=جارٍ تثبيت News Crawler...
hindi.CrawlerInstalling=News Crawler इंस्टॉल हो रहा है...
greek.CrawlerFailed=Δεν ήταν δυνατή η λήψη του News Crawler (%1).%n%nΤο Opsis εγκαταστάθηκε κανονικά. Μπορείτε να εγκαταστήσετε το News Crawler αργότερα από:%n%2
english.CrawlerFailed=News Crawler could not be downloaded (%1).%n%nOpsis was installed normally. You can install News Crawler later from:%n%2
german.CrawlerFailed=News Crawler konnte nicht heruntergeladen werden (%1).%n%nOpsis wurde normal installiert. Sie können News Crawler später installieren von:%n%2
french.CrawlerFailed=News Crawler n'a pas pu être téléchargé (%1).%n%nOpsis a été installé normalement. Vous pourrez installer News Crawler plus tard depuis :%n%2
spanish.CrawlerFailed=No se pudo descargar News Crawler (%1).%n%nOpsis se instaló con normalidad. Puede instalar News Crawler más tarde desde:%n%2
korean.CrawlerFailed=News Crawler를 다운로드할 수 없습니다(%1).%n%nOpsis는 정상적으로 설치되었습니다. 나중에 다음 위치에서 News Crawler를 설치할 수 있습니다:%n%2
chinese.CrawlerFailed=无法下载 News Crawler（%1）。%n%nOpsis 已正常安装。您可以稍后从以下位置安装 News Crawler：%n%2
italian.CrawlerFailed=Impossibile scaricare News Crawler (%1).%n%nOpsis è stato installato normalmente. Puoi installare News Crawler più tardi da:%n%2
russian.CrawlerFailed=Не удалось загрузить News Crawler (%1).%n%nOpsis установлен в обычном порядке. Вы можете установить News Crawler позже отсюда:%n%2
japanese.CrawlerFailed=News Crawler をダウンロードできませんでした (%1)。%n%nOpsis は通常どおりインストールされました。News Crawler は後で次の場所からインストールできます:%n%2
portuguese.CrawlerFailed=Não foi possível transferir o News Crawler (%1).%n%nO Opsis foi instalado normalmente. Pode instalar o News Crawler mais tarde a partir de:%n%2
turkish.CrawlerFailed=News Crawler indirilemedi (%1).%n%nOpsis normal şekilde yüklendi. News Crawler'ı daha sonra şuradan yükleyebilirsiniz:%n%2
arabic.CrawlerFailed=تعذّر تنزيل News Crawler (%1).%n%nتم تثبيت Opsis بشكل طبيعي. يمكنك تثبيت News Crawler لاحقًا من:%n%2
hindi.CrawlerFailed=News Crawler डाउनलोड नहीं हो सका (%1)।%n%nOpsis सामान्य रूप से इंस्टॉल हो गया। आप News Crawler बाद में यहाँ से इंस्टॉल कर सकते हैं:%n%2

; Opsis Appearance (plugin): προαιρετική λήψη και εγκατάσταση ταυτόχρονα με το Opsis (όπως ο crawler)
greek.AppearanceTask=Λήψη και εγκατάσταση του Opsis Appearance (αλλαγή εμφάνισης Windows: light/dark, accent, εφέ παραθύρων, θέματα· plugin του Opsis)
english.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
german.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
french.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
spanish.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
korean.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
chinese.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
italian.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
russian.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
japanese.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
portuguese.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
turkish.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
arabic.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
hindi.AppearanceTask=Download and install Opsis Appearance (change the look of Windows: light/dark, accent, window effects, themes; a plugin of Opsis)
greek.AppearanceDownloadTitle=Λήψη του Opsis Appearance
english.AppearanceDownloadTitle=Downloading Opsis Appearance
german.AppearanceDownloadTitle=Downloading Opsis Appearance
french.AppearanceDownloadTitle=Downloading Opsis Appearance
spanish.AppearanceDownloadTitle=Downloading Opsis Appearance
korean.AppearanceDownloadTitle=Downloading Opsis Appearance
chinese.AppearanceDownloadTitle=Downloading Opsis Appearance
italian.AppearanceDownloadTitle=Downloading Opsis Appearance
russian.AppearanceDownloadTitle=Downloading Opsis Appearance
japanese.AppearanceDownloadTitle=Downloading Opsis Appearance
portuguese.AppearanceDownloadTitle=Downloading Opsis Appearance
turkish.AppearanceDownloadTitle=Downloading Opsis Appearance
arabic.AppearanceDownloadTitle=Downloading Opsis Appearance
hindi.AppearanceDownloadTitle=Downloading Opsis Appearance
greek.AppearanceDownloadDesc=Περιμένετε όσο κατεβαίνει ο installer του Opsis Appearance.
english.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
german.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
french.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
spanish.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
korean.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
chinese.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
italian.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
russian.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
japanese.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
portuguese.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
turkish.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
arabic.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
hindi.AppearanceDownloadDesc=Please wait while the Opsis Appearance installer is downloaded.
greek.AppearanceInstalling=Εγκατάσταση του Opsis Appearance...
english.AppearanceInstalling=Installing Opsis Appearance...
german.AppearanceInstalling=Installing Opsis Appearance...
french.AppearanceInstalling=Installing Opsis Appearance...
spanish.AppearanceInstalling=Installing Opsis Appearance...
korean.AppearanceInstalling=Installing Opsis Appearance...
chinese.AppearanceInstalling=Installing Opsis Appearance...
italian.AppearanceInstalling=Installing Opsis Appearance...
russian.AppearanceInstalling=Installing Opsis Appearance...
japanese.AppearanceInstalling=Installing Opsis Appearance...
portuguese.AppearanceInstalling=Installing Opsis Appearance...
turkish.AppearanceInstalling=Installing Opsis Appearance...
arabic.AppearanceInstalling=Installing Opsis Appearance...
hindi.AppearanceInstalling=Installing Opsis Appearance...
greek.AppearanceFailed=Δεν ήταν δυνατή η λήψη του Opsis Appearance (%1).%n%nΤο Opsis εγκαταστάθηκε κανονικά. Μπορείτε να εγκαταστήσετε το Opsis Appearance αργότερα από:%n%2
english.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
german.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
french.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
spanish.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
korean.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
chinese.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
italian.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
russian.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
japanese.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
portuguese.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
turkish.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
arabic.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2
hindi.AppearanceFailed=Opsis Appearance could not be downloaded (%1).%n%nOpsis was installed normally. You can install Opsis Appearance later from:%n%2

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"
; Προαιρετική εξάρτηση — ζητήθηκε ρητά "να εμφανιζεται η δυνατοτητα εγκαταστασης dependencies
; οπως το ffmpeg". Το FFmpeg χρειάζεται μόνο για αναπαραγωγή/μετατροπή .wmv βίντεο ως κινούμενο
; wallpaper (WmvConversionService) — δεν είναι απαραίτητο για την υπόλοιπη εφαρμογή, γι' αυτό
; είναι tickbox και όχι υποχρεωτικό όπως το WebView2/.NET Runtime. Εγκαθίσταται μέσω winget (ίδιο
; πακέτο, Gyan.FFmpeg, με αυτό που ήδη ψάχνει το WmvConversionService.FindFfmpeg()).
; Ζητήθηκε ρητά 15/10: προεπιλογή ενεργό (όχι πια "unchecked") — ο χρήστης βαρέθηκε να το ξεχνάει
; σε κάθε ενημέρωση του Opsis και το News Crawler έμενε παλιό για μήνες. "checkedonce" (ίδιο flag
; με το desktopicon παραπάνω) = ενεργό στην πρώτη εγκατάσταση, αλλά θυμάται αν ο χρήστης το
; απενεργοποιήσει αργότερα — δεν το ξαναενεργοποιεί σιωπηλά σε κάθε update.
Name: "installappearance"; Description: "{cm:AppearanceTask}"; GroupDescription: "{cm:ExtraDepsGroup}"; Flags: unchecked
Name: "installcrawler"; Description: "{cm:CrawlerTask}"; GroupDescription: "{cm:ExtraDepsGroup}"; Flags: checkedonce
Name: "installffmpeg"; Description: "{cm:FfmpegTask}"; GroupDescription: "{cm:ExtraDepsGroup}"; Flags: unchecked; Check: IsWingetAvailable and not IsFfmpegInstalled

[Files]
; Κύρια αρχεία εφαρμογής (δημιουργούνται από: dotnet publish -c Release -r win-x64)
Source: "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Αυτόματη εκκίνηση μέσω Registry (αν επιλεχθεί στο wizard)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; 1. Προαιρετική εγκατάσταση FFmpeg μέσω winget (μόνο αν ο χρήστης επέλεξε το tickbox παραπάνω).
; "& exit 0" αποτρέπει το Inno Setup από το να δείξει σφάλμα αν το winget γυρίσει μη-μηδενικό
; exit code (π.χ. "no applicable update found" όταν είναι ήδη εγκατεστημένο).
; (.NET 10 Desktop Runtime / WebView2 Runtime: πλέον μέσω της κοινής σελίδας dependencies.iss —
; βλ. InitializeWizard/NextButtonClick/CurStepChanged παρακάτω.)
Filename: "{cmd}"; Parameters: "/C winget install --id Gyan.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements & exit 0"; StatusMsg: "{cm:InstallingFfmpeg}"; Flags: runhidden; Tasks: installffmpeg

; 2. Εκκίνηση της εφαρμογής μετά το τέλος της εγκατάστασης
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Ελέγχει αν το winget (App Installer) υπάρχει στο σύστημα, ώστε το tickbox του FFmpeg να μην
// εμφανίζεται καν σε μηχανήματα χωρίς winget (θα απέτυχε σιωπηλά χωρίς αυτό τον έλεγχο).
function IsWingetAvailable: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C where winget >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Γρήγορος έλεγχος αν υπάρχει ήδη FFmpeg (PATH ή γνωστός φάκελος winget) — ίδια λογική με το
// WmvConversionService.FindFfmpeg() της ίδιας της εφαρμογής, ώστε να μην προτείνουμε εγκατάσταση
// αν υπάρχει ήδη.
function IsFfmpegInstalled: Boolean;
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{cmd}'), '/C where ffmpeg >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    Result := True;
    exit;
  end;
  Result := FileExists(ExpandConstant('{localappdata}\Microsoft\WinGet\Links\ffmpeg.exe'));
end;

// Ίδιο μοτίβο ανίχνευσης ενημέρωσης με το GearWin installer: διαβάζει το DisplayVersion από το
// registry uninstall key που γράφει το ίδιο το Inno Setup σε κάθε εγκατάσταση, ελέγχοντας ΚΑΙ τις
// δύο όψεις του registry (64-bit πρώτα, 32-bit fallback).
function GetInstalledVersion(): String;
var
  sVersion: String;
begin
  sVersion := '';
  if not RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\A9E8F765-1234-4567-89AB-CDEF01234567_is1', 'DisplayVersion', sVersion) then
    RegQueryStringValue(HKLM32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\A9E8F765-1234-4567-89AB-CDEF01234567_is1', 'DisplayVersion', sVersion);
  Result := sVersion;
end;

var
  CrawlerDownload: TDownloadWizardPage;
  CrawlerDownloaded: Boolean;
  AppearanceDownload: TDownloadWizardPage;
  AppearanceDownloaded: Boolean;

procedure InitializeWizard();
var
  sPrevVersion: String;
begin
  sPrevVersion := GetInstalledVersion();
  if sPrevVersion <> '' then
    WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('UpdateDetected'), [sPrevVersion]);
  CrawlerDownload := CreateDownloadPage(CustomMessage('CrawlerDownloadTitle'), CustomMessage('CrawlerDownloadDesc'), nil);
  AppearanceDownload := CreateDownloadPage(CustomMessage('AppearanceDownloadTitle'), CustomMessage('AppearanceDownloadDesc'), nil);
  // Σελίδα εξαρτήσεων (dependencies.iss) αμέσως μετά τη σελίδα επιλογής tasks.
  DepsInit(wpSelectTasks);
end;

function CrawlerChecksumMatches: Boolean;
var
  Lines: TArrayOfString;
  Expected, Actual: String;
begin
  Result := False;
  if not LoadStringsFromFile(ExpandConstant('{tmp}\OpsisNewsCrawler-Setup.exe.sha256'), Lines) then Exit;
  if GetArrayLength(Lines) = 0 then Exit;
  Expected := Lowercase(Copy(Trim(Lines[0]), 1, 64));
  Actual := Lowercase(GetSHA256OfFile(ExpandConstant('{tmp}\OpsisNewsCrawler-Setup.exe')));
  Result := (Length(Expected) = 64) and (Expected = Actual);
end;

function AppearanceChecksumMatches: Boolean;
var
  Lines: TArrayOfString;
  Expected, Actual: String;
begin
  Result := False;
  if not LoadStringsFromFile(ExpandConstant('{tmp}\OpsisAppearance-Setup.exe.sha256'), Lines) then Exit;
  if GetArrayLength(Lines) = 0 then Exit;
  Expected := Lowercase(Copy(Trim(Lines[0]), 1, 64));
  Actual := Lowercase(GetSHA256OfFile(ExpandConstant('{tmp}\OpsisAppearance-Setup.exe')));
  Result := (Length(Expected) = 64) and (Expected = Actual);
end;

// Λήψη του installer του News Crawler πριν ξεκινήσει η εγκατάσταση (αν επιλέχθηκε). Αν αποτύχει, το Opsis εγκαθίσταται κανονικά.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpReady then
    Result := DepsDownloadSelected();
  if (CurPageID = wpReady) and WizardIsTaskSelected('installappearance') then
  begin
    AppearanceDownloaded := False;
    AppearanceDownload.Clear;
    AppearanceDownload.Add('{#AppearanceSetupUrl}', 'OpsisAppearance-Setup.exe', '');
    AppearanceDownload.Add('{#AppearanceSetupUrl}.sha256', 'OpsisAppearance-Setup.exe.sha256', '');
    AppearanceDownload.Show;
    try
      try
        AppearanceDownload.Download;
        // Το release δημοσιεύει το SHA-256 δίπλα στο installer: απορρίπτεται αρχείο που δεν ταιριάζει (κατεστραμμένο ή αντικατεστημένο).
        if not AppearanceChecksumMatches then
          RaiseException('the checksum of the downloaded file does not match');
        AppearanceDownloaded := True;
      except
        if AppearanceDownload.AbortedByUser then
          Log('Opsis Appearance download aborted by the user')
        else
          SuppressibleMsgBox(FmtMessage(CustomMessage('AppearanceFailed'), [GetExceptionMessage, '{#AppearanceSetupUrl}']), mbError, MB_OK, IDOK);
      end;
    finally
      AppearanceDownload.Hide;
    end;
  end;
  if (CurPageID = wpReady) and WizardIsTaskSelected('installcrawler') then
  begin
    CrawlerDownloaded := False;
    CrawlerDownload.Clear;
    CrawlerDownload.Add('{#CrawlerSetupUrl}', 'OpsisNewsCrawler-Setup.exe', '');
    CrawlerDownload.Add('{#CrawlerSetupUrl}.sha256', 'OpsisNewsCrawler-Setup.exe.sha256', '');
    CrawlerDownload.Show;
    try
      try
        CrawlerDownload.Download;
        // The release publishes the SHA-256 of the setup next to it: refuse a file that does not match (damaged or replaced download).
        if not CrawlerChecksumMatches then
          RaiseException('the checksum of the downloaded file does not match');
        CrawlerDownloaded := True;
      except
        if CrawlerDownload.AbortedByUser then
          Log('News Crawler download aborted by the user')
        else
          SuppressibleMsgBox(FmtMessage(CustomMessage('CrawlerFailed'), [GetExceptionMessage, '{#CrawlerSetupUrl}']), mbError, MB_OK, IDOK);
      end;
    finally
      CrawlerDownload.Hide;
    end;
  end;
end;

// Μετά την εγκατάσταση του Opsis: σιωπηλή ενσωματωμένη εγκατάσταση του crawler (ως ο χρήστης που τρέχει το setup,
// γιατί ο installer του crawler είναι per-user και γράφει στο %AppData%\Opsis\plugins).
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  SetupFile: String;
begin
  if CurStep = ssInstall then DepsInstallSelected();
  if (CurStep = ssPostInstall) and AppearanceDownloaded then
  begin
    SetupFile := ExpandConstant('{tmp}\OpsisAppearance-Setup.exe');
    if FileExists(SetupFile) then
    begin
      WizardForm.StatusLabel.Caption := CustomMessage('AppearanceInstalling');
      if not ExecAsOriginalUser(SetupFile, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MODE=plugin', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
        Log('Could not start the Opsis Appearance installer');
    end;
  end;
  if (CurStep = ssPostInstall) and CrawlerDownloaded then
  begin
    SetupFile := ExpandConstant('{tmp}\OpsisNewsCrawler-Setup.exe');
    if FileExists(SetupFile) then
    begin
      WizardForm.StatusLabel.Caption := CustomMessage('CrawlerInstalling');
      if not ExecAsOriginalUser(SetupFile, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MODE=integrated', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
        Log('Could not start the News Crawler installer');
    end;
  end;
end;

// Καθαρισμός %AppData%\Opsis στην απεγκατάσταση (ρυθμίσεις, wallpaper.json,
// αποθηκευμένα profiles/DeskZones/DeskContainers) — ζητήθηκε ρητά, αλλά με ερώτηση επιβεβαίωσης
// πριν διαγραφούν πιθανά δεδομένα που ο χρήστης θέλει να κρατήσει. Το prompt χρησιμοποιεί πλέον
// CustomMessage (EL/EN) αντί για hardcoded ελληνικό κείμενο, ώστε να εμφανίζεται στη γλώσσα που
// επέλεξε ο χρήστης κατά την εγκατάσταση. Ελέγχει ΚΑΙ το παλιό %AppData%\MotionDeskStudio (από τη
// μετονομασία "MotionDesk Studio" -> "Opsis") — η εφαρμογή το μεταφέρει αυτόματα στο νέο όνομα στην
// πρώτη εκκίνηση, αλλά αν ο χρήστης κάνει uninstall ΠΡΙΝ ποτέ ξανατρέξει τη νέα έκδοση, ο παλιός
// φάκελος θα υπάρχει ακόμα με το παλιό όνομα.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath, LegacyAppDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataPath := ExpandConstant('{userappdata}\Opsis');
    LegacyAppDataPath := ExpandConstant('{userappdata}\MotionDeskStudio');
    if DirExists(AppDataPath) or DirExists(LegacyAppDataPath) then
    begin
      if MsgBox(CustomMessage('KeepDataPromptText') + #13#10 + #13#10 + AppDataPath, mbConfirmation, MB_YESNO) = IDYES then
      begin
        if DirExists(AppDataPath) then DelTree(AppDataPath, True, True, True);
        if DirExists(LegacyAppDataPath) then DelTree(LegacyAppDataPath, True, True, True);
      end;
    end;
  end;
end;
