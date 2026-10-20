; Dependencies page shared by the installers of Opsis News Crawler and Opsis - Desktop Customization (Inno Setup 6.1 or newer).
;
; Shows the components the program needs. What is already on the computer is shown ticked and cannot be changed; what is missing can
; be ticked and is then downloaded from Microsoft and installed silently:
;   * .NET 10 Desktop Runtime (x64)            - needed to run the program (framework-dependent build)
;   * Microsoft Edge WebView2 Runtime          - live video preview, weather map
; Every downloaded file is run only if it carries a valid Microsoft signature.
; Silent installs: /DEPS=all | dotnet | webview2 | none (default none: a silent install never downloads by itself).
;
; Use from the main script (include it before its own [Code] section, then call):
;   InitializeWizard:  DepsInit(<id of the page after which it appears>);
;   NextButtonClick:   if CurPageID = wpReady then Result := DepsDownloadSelected;
;   CurStepChanged:    if CurStep = ssInstall then DepsInstallSelected;
; The macro MyAppName must be defined before the include.

[CustomMessages]
english.DepsTitle=Required components
english.DepsSubtitle=Programs that {#MyAppName} needs
english.DepsIntro=Components that are already on this computer are ticked and cannot be changed. Tick the missing ones to download them from Microsoft and install them now.
english.DepDotNet=.NET 10 Desktop Runtime (needed to run the program)
english.DepWebView2=Microsoft Edge WebView2 Runtime (live video preview and weather map)
english.DepInstalled= - already installed
english.DepsDownloadTitle=Downloading components
english.DepsDownloadDesc=Downloading from Microsoft...
english.DepsInstalling=Installing components...
english.DepsFailed=%1 could not be installed (%2).%n%nYou can install it later from https://dotnet.microsoft.com/download or https://developer.microsoft.com/microsoft-edge/webview2/.
english.DepsNotSigned=the downloaded file is not signed by Microsoft
greek.DepsTitle=Απαιτούμενα στοιχεία
greek.DepsSubtitle=Προγράμματα που χρειάζεται το {#MyAppName}
greek.DepsIntro=Τα στοιχεία που υπάρχουν ήδη σε αυτόν τον υπολογιστή εμφανίζονται τσεκαρισμένα και δεν αλλάζουν. Τσεκάρετε όσα λείπουν για να τα κατεβάσετε από τη Microsoft και να εγκατασταθούν τώρα.
greek.DepDotNet=.NET 10 Desktop Runtime (απαραίτητο για να τρέξει το πρόγραμμα)
greek.DepWebView2=Microsoft Edge WebView2 Runtime (ζωντανή προεπισκόπηση βίντεο και χάρτης καιρού)
greek.DepInstalled= - ήδη εγκατεστημένο
greek.DepsDownloadTitle=Λήψη στοιχείων
greek.DepsDownloadDesc=Λήψη από τη Microsoft...
greek.DepsInstalling=Εγκατάσταση στοιχείων...
greek.DepsFailed=Δεν ήταν δυνατή η εγκατάσταση: %1 (%2).%n%nΜπορείτε να το εγκαταστήσετε αργότερα από https://dotnet.microsoft.com/download ή https://developer.microsoft.com/microsoft-edge/webview2/.
greek.DepsNotSigned=το αρχείο που κατέβηκε δεν είναι υπογεγραμμένο από τη Microsoft
german.DepsTitle=Erforderliche Komponenten
german.DepsSubtitle=Programme, die {#MyAppName} benötigt
german.DepsIntro=Komponenten, die bereits auf diesem Computer vorhanden sind, sind angehakt und nicht änderbar. Haken Sie fehlende an, um sie jetzt von Microsoft herunterzuladen und zu installieren.
german.DepDotNet=.NET 10 Desktop Runtime (zum Ausführen des Programms nötig)
german.DepWebView2=Microsoft Edge WebView2 Runtime (Live-Videovorschau und Wetterkarte)
german.DepInstalled= - bereits installiert
german.DepsDownloadTitle=Komponenten werden heruntergeladen
german.DepsDownloadDesc=Download von Microsoft...
german.DepsInstalling=Komponenten werden installiert...
german.DepsFailed=%1 konnte nicht installiert werden (%2).%n%nSie können es später von https://dotnet.microsoft.com/download oder https://developer.microsoft.com/microsoft-edge/webview2/ installieren.
german.DepsNotSigned=die heruntergeladene Datei ist nicht von Microsoft signiert
french.DepsTitle=Composants requis
french.DepsSubtitle=Programmes dont {#MyAppName} a besoin
french.DepsIntro=Les composants déjà présents sur cet ordinateur sont cochés et ne peuvent pas être modifiés. Cochez ceux qui manquent pour les télécharger depuis Microsoft et les installer maintenant.
french.DepDotNet=.NET 10 Desktop Runtime (nécessaire pour exécuter le programme)
french.DepWebView2=Microsoft Edge WebView2 Runtime (aperçu vidéo en direct et carte météo)
french.DepInstalled= - déjà installé
french.DepsDownloadTitle=Téléchargement des composants
french.DepsDownloadDesc=Téléchargement depuis Microsoft...
french.DepsInstalling=Installation des composants...
french.DepsFailed=%1 n'a pas pu être installé (%2).%n%nVous pourrez l'installer plus tard depuis https://dotnet.microsoft.com/download ou https://developer.microsoft.com/microsoft-edge/webview2/.
french.DepsNotSigned=le fichier téléchargé n'est pas signé par Microsoft
spanish.DepsTitle=Componentes necesarios
spanish.DepsSubtitle=Programas que necesita {#MyAppName}
spanish.DepsIntro=Los componentes que ya están en este equipo aparecen marcados y no se pueden cambiar. Marque los que faltan para descargarlos de Microsoft e instalarlos ahora.
spanish.DepDotNet=.NET 10 Desktop Runtime (necesario para ejecutar el programa)
spanish.DepWebView2=Microsoft Edge WebView2 Runtime (vista previa de vídeo en directo y mapa del tiempo)
spanish.DepInstalled= - ya instalado
spanish.DepsDownloadTitle=Descargando componentes
spanish.DepsDownloadDesc=Descargando de Microsoft...
spanish.DepsInstalling=Instalando componentes...
spanish.DepsFailed=No se pudo instalar %1 (%2).%n%nPuede instalarlo más tarde desde https://dotnet.microsoft.com/download o https://developer.microsoft.com/microsoft-edge/webview2/.
spanish.DepsNotSigned=el archivo descargado no está firmado por Microsoft
korean.DepsTitle=필수 구성 요소
korean.DepsSubtitle={#MyAppName}에 필요한 프로그램
korean.DepsIntro=이 컴퓨터에 이미 있는 구성 요소는 선택된 상태로 표시되며 변경할 수 없습니다. 없는 항목을 선택하면 Microsoft에서 내려받아 지금 설치합니다.
korean.DepDotNet=.NET 10 Desktop Runtime (프로그램 실행에 필요)
korean.DepWebView2=Microsoft Edge WebView2 Runtime (실시간 영상 미리 보기 및 날씨 지도)
korean.DepInstalled= - 이미 설치됨
korean.DepsDownloadTitle=구성 요소 다운로드
korean.DepsDownloadDesc=Microsoft에서 다운로드하는 중...
korean.DepsInstalling=구성 요소 설치 중...
korean.DepsFailed=%1을(를) 설치할 수 없습니다(%2).%n%n나중에 https://dotnet.microsoft.com/download 또는 https://developer.microsoft.com/microsoft-edge/webview2/ 에서 설치할 수 있습니다.
korean.DepsNotSigned=다운로드한 파일이 Microsoft 서명이 아닙니다
chinese.DepsTitle=所需组件
chinese.DepsSubtitle={#MyAppName} 需要的程序
chinese.DepsIntro=此计算机上已有的组件会显示为已勾选且无法更改。请勾选缺少的组件，以便从 Microsoft 下载并立即安装。
chinese.DepDotNet=.NET 10 Desktop Runtime（运行程序所必需）
chinese.DepWebView2=Microsoft Edge WebView2 Runtime（实时视频预览和天气地图）
chinese.DepInstalled= - 已安装
chinese.DepsDownloadTitle=正在下载组件
chinese.DepsDownloadDesc=正在从 Microsoft 下载...
chinese.DepsInstalling=正在安装组件...
chinese.DepsFailed=无法安装 %1（%2）。%n%n您可以稍后从 https://dotnet.microsoft.com/download 或 https://developer.microsoft.com/microsoft-edge/webview2/ 安装。
chinese.DepsNotSigned=下载的文件没有 Microsoft 签名
italian.DepsTitle=Componenti necessari
italian.DepsSubtitle=Programmi di cui {#MyAppName} ha bisogno
italian.DepsIntro=I componenti già presenti su questo computer sono spuntati e non modificabili. Spunta quelli mancanti per scaricarli da Microsoft e installarli ora.
italian.DepDotNet=.NET 10 Desktop Runtime (necessario per eseguire il programma)
italian.DepWebView2=Microsoft Edge WebView2 Runtime (anteprima video dal vivo e mappa meteo)
italian.DepInstalled= - già installato
italian.DepsDownloadTitle=Download dei componenti
italian.DepsDownloadDesc=Download da Microsoft...
italian.DepsInstalling=Installazione dei componenti...
italian.DepsFailed=Impossibile installare %1 (%2).%n%nPuoi installarlo più tardi da https://dotnet.microsoft.com/download o https://developer.microsoft.com/microsoft-edge/webview2/.
italian.DepsNotSigned=il file scaricato non è firmato da Microsoft
russian.DepsTitle=Необходимые компоненты
russian.DepsSubtitle=Программы, которые нужны {#MyAppName}
russian.DepsIntro=Компоненты, уже имеющиеся на этом компьютере, отмечены и недоступны для изменения. Отметьте недостающие, чтобы скачать их у Microsoft и установить сейчас.
russian.DepDotNet=.NET 10 Desktop Runtime (нужен для запуска программы)
russian.DepWebView2=Microsoft Edge WebView2 Runtime (предпросмотр видео и карта погоды)
russian.DepInstalled= - уже установлено
russian.DepsDownloadTitle=Загрузка компонентов
russian.DepsDownloadDesc=Загрузка с сайта Microsoft...
russian.DepsInstalling=Установка компонентов...
russian.DepsFailed=Не удалось установить %1 (%2).%n%nВы можете установить его позже с https://dotnet.microsoft.com/download или https://developer.microsoft.com/microsoft-edge/webview2/.
russian.DepsNotSigned=скачанный файл не подписан Microsoft
japanese.DepsTitle=必要なコンポーネント
japanese.DepsSubtitle={#MyAppName} に必要なプログラム
japanese.DepsIntro=このコンピューターにすでにあるコンポーネントはチェック済みで変更できません。足りないものにチェックを入れると、Microsoft からダウンロードして今すぐインストールします。
japanese.DepDotNet=.NET 10 Desktop Runtime (プログラムの実行に必要)
japanese.DepWebView2=Microsoft Edge WebView2 Runtime (ライブ動画プレビューと天気マップ)
japanese.DepInstalled= - インストール済み
japanese.DepsDownloadTitle=コンポーネントをダウンロードしています
japanese.DepsDownloadDesc=Microsoft からダウンロードしています...
japanese.DepsInstalling=コンポーネントをインストールしています...
japanese.DepsFailed=%1 をインストールできませんでした (%2)。%n%n後で https://dotnet.microsoft.com/download または https://developer.microsoft.com/microsoft-edge/webview2/ からインストールできます。
japanese.DepsNotSigned=ダウンロードしたファイルに Microsoft の署名がありません
portuguese.DepsTitle=Componentes necessários
portuguese.DepsSubtitle=Programas de que o {#MyAppName} precisa
portuguese.DepsIntro=Os componentes que já existem neste computador aparecem assinalados e não podem ser alterados. Assinale os que faltam para os transferir da Microsoft e instalar agora.
portuguese.DepDotNet=.NET 10 Desktop Runtime (necessário para executar o programa)
portuguese.DepWebView2=Microsoft Edge WebView2 Runtime (pré-visualização de vídeo em direto e mapa do tempo)
portuguese.DepInstalled= - já instalado
portuguese.DepsDownloadTitle=A transferir componentes
portuguese.DepsDownloadDesc=A transferir da Microsoft...
portuguese.DepsInstalling=A instalar componentes...
portuguese.DepsFailed=Não foi possível instalar %1 (%2).%n%nPode instalá-lo mais tarde a partir de https://dotnet.microsoft.com/download ou https://developer.microsoft.com/microsoft-edge/webview2/.
portuguese.DepsNotSigned=o ficheiro transferido não está assinado pela Microsoft
turkish.DepsTitle=Gerekli bileşenler
turkish.DepsSubtitle={#MyAppName} için gereken programlar
turkish.DepsIntro=Bu bilgisayarda zaten bulunan bileşenler işaretli gelir ve değiştirilemez. Eksik olanları işaretleyerek Microsoft'tan indirip şimdi kurabilirsiniz.
turkish.DepDotNet=.NET 10 Desktop Runtime (programı çalıştırmak için gerekli)
turkish.DepWebView2=Microsoft Edge WebView2 Runtime (canlı video önizlemesi ve hava durumu haritası)
turkish.DepInstalled= - zaten kurulu
turkish.DepsDownloadTitle=Bileşenler indiriliyor
turkish.DepsDownloadDesc=Microsoft'tan indiriliyor...
turkish.DepsInstalling=Bileşenler kuruluyor...
turkish.DepsFailed=%1 kurulamadı (%2).%n%nDaha sonra https://dotnet.microsoft.com/download veya https://developer.microsoft.com/microsoft-edge/webview2/ adresinden kurabilirsiniz.
turkish.DepsNotSigned=indirilen dosya Microsoft tarafından imzalanmamış
arabic.DepsTitle=المكونات المطلوبة
arabic.DepsSubtitle=البرامج التي يحتاجها {#MyAppName}
arabic.DepsIntro=المكونات الموجودة بالفعل على هذا الكمبيوتر تظهر محددة ولا يمكن تغييرها. حدّد المكونات الناقصة لتنزيلها من Microsoft وتثبيتها الآن.
arabic.DepDotNet=.NET 10 Desktop Runtime (مطلوب لتشغيل البرنامج)
arabic.DepWebView2=Microsoft Edge WebView2 Runtime (معاينة الفيديو المباشر وخريطة الطقس)
arabic.DepInstalled= - مثبّت بالفعل
arabic.DepsDownloadTitle=جارٍ تنزيل المكونات
arabic.DepsDownloadDesc=جارٍ التنزيل من Microsoft...
arabic.DepsInstalling=جارٍ تثبيت المكونات...
arabic.DepsFailed=تعذّر تثبيت %1 (%2).%n%nيمكنك تثبيته لاحقًا من https://dotnet.microsoft.com/download أو https://developer.microsoft.com/microsoft-edge/webview2/.
arabic.DepsNotSigned=الملف الذي تم تنزيله غير موقّع من Microsoft
hindi.DepsTitle=आवश्यक घटक
hindi.DepsSubtitle=वे प्रोग्राम जिनकी {#MyAppName} को ज़रूरत है
hindi.DepsIntro=इस कंप्यूटर पर पहले से मौजूद घटक चुने हुए दिखते हैं और बदले नहीं जा सकते। जो घटक नहीं हैं उन्हें चुनें ताकि उन्हें Microsoft से डाउनलोड करके अभी इंस्टॉल किया जा सके।
hindi.DepDotNet=.NET 10 Desktop Runtime (प्रोग्राम चलाने के लिए ज़रूरी)
hindi.DepWebView2=Microsoft Edge WebView2 Runtime (लाइव वीडियो पूर्वावलोकन और मौसम मानचित्र)
hindi.DepInstalled= - पहले से इंस्टॉल है
hindi.DepsDownloadTitle=घटक डाउनलोड हो रहे हैं
hindi.DepsDownloadDesc=Microsoft से डाउनलोड हो रहा है...
hindi.DepsInstalling=घटक इंस्टॉल हो रहे हैं...
hindi.DepsFailed=%1 इंस्टॉल नहीं हो सका (%2)।%n%nआप इसे बाद में https://dotnet.microsoft.com/download या https://developer.microsoft.com/microsoft-edge/webview2/ से इंस्टॉल कर सकते हैं।
hindi.DepsNotSigned=डाउनलोड की गई फ़ाइल Microsoft द्वारा हस्ताक्षरित नहीं है

[Code]
const
  DotNetUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  WebView2Url = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

var
  DepsPage: TInputOptionWizardPage;
  DepsDownload: TDownloadWizardPage;
  DepDotNetPresent: Boolean;
  DepWebView2Present: Boolean;
  DepDotNetFile: String;
  DepWebView2File: String;

function DotNetDesktop10Present(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function WebView2Present(): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) or
     RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) or
     RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) then
    Result := (Version <> '') and (Version <> '0.0.0.0');
end;

procedure DepsInit(AfterPageID: Integer);
begin
  DepDotNetPresent := DotNetDesktop10Present();
  DepWebView2Present := WebView2Present();
  DepsPage := CreateInputOptionPage(AfterPageID, CustomMessage('DepsTitle'), CustomMessage('DepsSubtitle'), CustomMessage('DepsIntro'), False, False);
  if DepDotNetPresent then
    DepsPage.Add(CustomMessage('DepDotNet') + CustomMessage('DepInstalled'))
  else
    DepsPage.Add(CustomMessage('DepDotNet'));
  if DepWebView2Present then
    DepsPage.Add(CustomMessage('DepWebView2') + CustomMessage('DepInstalled'))
  else
    DepsPage.Add(CustomMessage('DepWebView2'));
  // Present: ticked and locked. Missing: ticked, the user may untick.
  DepsPage.Values[0] := True;
  DepsPage.Values[1] := True;
  DepsPage.CheckListBox.ItemEnabled[0] := not DepDotNetPresent;
  DepsPage.CheckListBox.ItemEnabled[1] := not DepWebView2Present;
  DepsDownload := CreateDownloadPage(CustomMessage('DepsDownloadTitle'), CustomMessage('DepsDownloadDesc'), nil);
end;

// What is going to be installed: silent installs follow /DEPS, wizard installs follow the page.
function WantDotNet(): Boolean;
var
  P: String;
begin
  if DepDotNetPresent then begin Result := False; Exit; end;
  if WizardSilent or (DepsPage = nil) then
  begin
    P := Lowercase(ExpandConstant('{param:DEPS|none}'));
    Result := (P = 'all') or (P = 'dotnet');
  end
  else
    Result := DepsPage.Values[0];
end;

function WantWebView2(): Boolean;
var
  P: String;
begin
  if DepWebView2Present then begin Result := False; Exit; end;
  if WizardSilent or (DepsPage = nil) then
  begin
    P := Lowercase(ExpandConstant('{param:DEPS|none}'));
    Result := (P = 'all') or (P = 'webview2');
  end
  else
    Result := DepsPage.Values[1];
end;

// Runs before the files are copied: downloads what was chosen. A failed download is reported and skipped - the program installs anyway.
function DepsDownloadSelected(): Boolean;
begin
  Result := True;
  DepDotNetFile := '';
  DepWebView2File := '';
  if not (WantDotNet() or WantWebView2()) then Exit;
  if WizardSilent then
  begin
    // no wizard page in a silent install: plain downloads
    try
      if WantDotNet() then
      begin
        DownloadTemporaryFile(DotNetUrl, 'windowsdesktop-runtime-win-x64.exe', '', nil);
        DepDotNetFile := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
      end;
      if WantWebView2() then
      begin
        DownloadTemporaryFile(WebView2Url, 'MicrosoftEdgeWebview2Setup.exe', '', nil);
        DepWebView2File := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
      end;
    except
      Log('Dependency download failed: ' + GetExceptionMessage);
    end;
    Exit;
  end;
  DepsDownload.Clear;
  if WantDotNet() then DepsDownload.Add(DotNetUrl, 'windowsdesktop-runtime-win-x64.exe', '');
  if WantWebView2() then DepsDownload.Add(WebView2Url, 'MicrosoftEdgeWebview2Setup.exe', '');
  DepsDownload.Show;
  try
    try
      DepsDownload.Download;
      if WantDotNet() then DepDotNetFile := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
      if WantWebView2() then DepWebView2File := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
    except
      if not DepsDownload.AbortedByUser then
        SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), ['.NET / WebView2', GetExceptionMessage]), mbError, MB_OK, IDOK);
    end;
  finally
    DepsDownload.Hide;
  end;
end;

// True when the file carries a valid Authenticode signature of Microsoft Corporation.
function SignedByMicrosoft(const FileName: String): Boolean;
var
  ResultCode: Integer;
  Command: String;
begin
  Command := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$s = Get-AuthenticodeSignature -LiteralPath ''' + FileName +
    '''; if ($s.Status -eq ''Valid'' -and $s.SignerCertificate.Subject -like ''*O=Microsoft Corporation*'') { exit 0 } else { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Command, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Installs what was downloaded (called at the start of the installation step).
procedure DepsInstallSelected();
var
  ResultCode: Integer;
begin
  if (DepDotNetFile = '') and (DepWebView2File = '') then Exit;
  WizardForm.StatusLabel.Caption := CustomMessage('DepsInstalling');
  if DepDotNetFile <> '' then
  begin
    if not SignedByMicrosoft(DepDotNetFile) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), ['.NET 10 Desktop Runtime', CustomMessage('DepsNotSigned')]), mbError, MB_OK, IDOK)
    else
    begin
      // needs administrator rights: Windows asks for them (UAC)
      if not ShellExec('runas', DepDotNetFile, '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) or
         not ((ResultCode = 0) or (ResultCode = 3010) or (ResultCode = 1638)) then
        SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), ['.NET 10 Desktop Runtime', IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
    end;
  end;
  if DepWebView2File <> '' then
  begin
    if not SignedByMicrosoft(DepWebView2File) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), ['WebView2 Runtime', CustomMessage('DepsNotSigned')]), mbError, MB_OK, IDOK)
    else if not Exec(DepWebView2File, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      SuppressibleMsgBox(FmtMessage(CustomMessage('DepsFailed'), ['WebView2 Runtime', IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
  end;
end;
