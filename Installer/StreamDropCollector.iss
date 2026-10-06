#define MyAppName "Stream Drop Collector"
#define MyAppExeName "Stream Drop Collector.exe"
#define MyAppPublisher "tsgsOFFICIAL"

#ifndef MyAppVersion
#define MyAppVersion GetStringFileInfo("..\publish\self-contained\" + MyAppExeName, "ProductVersion")
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={userappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=yes
OutputBaseFilename=StreamDropCollector-{#MyAppVersion}-Setup
OutputDir=..\publish\installer
SetupIconFile=..\UI\Assets\logo.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\self-contained\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; A running app keeps its files and browser profile locked, which would stop them being removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ""{#MyAppExeName}"""; Flags: runhidden; RunOnceId: "StopApp"

[Code]
const
  StartupRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupRunValue = 'StreamDropCollector';

var
  RemoveUserData: Boolean;

// The app writes its settings, logins and caches into the install folder (and %APPDATA%\Stream Drop Collector), and
// older updates copied extra files in there. Inno Setup only removes files it installed itself, so the rest is cleaned here.
function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveUserData := False;

  // Silent uninstalls keep user data unless they pass /REMOVEUSERDATA=1
  if ExpandConstant('{param:REMOVEUSERDATA|0}') = '1' then
    RemoveUserData := True
  else if not UninstallSilent then
    RemoveUserData := SuppressibleMsgBox(
      'Do you also want to delete your settings, accounts and saved logins?' + #13#10#13#10 +
      'Choose No to keep them, for example if you plan to reinstall later.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

function IsUserData(const Name: String): Boolean;
begin
  Result :=
    (CompareText(Name, 'Accounts') = 0) or
    (CompareText(Name, 'Accounts.json') = 0) or
    (CompareText(Name, 'Settings.json') = 0) or
    (CompareText(Name, 'helix-auth.dat') = 0) or
    (CompareText(Name, 'LastMinedStreamers.json') = 0) or
    (CompareText(Name, 'PinnedCampaignCache.json') = 0) or
    (CompareText(Name, 'KnownGames.json') = 0) or
    (CompareText(Name, '{#MyAppExeName}.WebView2') = 0);
end;

procedure CleanDirectory(const Dir: String; const KeepUserData: Boolean);
var
  FindRec: TFindRec;
  Path: String;
begin
  if not DirExists(Dir) then
    Exit;

  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') and
           (Pos('unins', LowerCase(FindRec.Name)) <> 1) and
           not (KeepUserData and IsUserData(FindRec.Name)) then
        begin
          Path := Dir + '\' + FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
            DelTree(Path, True, True, True)
          else
            DeleteFile(Path);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  RemoveDir(Dir); // only succeeds when empty, so kept data is never touched
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir, DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  RegDeleteValue(HKEY_CURRENT_USER, StartupRunKey, StartupRunValue);

  AppDir := ExpandConstant('{app}');
  DataDir := ExpandConstant('{userappdata}\{#MyAppName}');

  CleanDirectory(AppDir, not RemoveUserData);

  // The app always stores its data under %APPDATA%, even when it was installed somewhere else
  if CompareText(AppDir, DataDir) <> 0 then
    CleanDirectory(DataDir, not RemoveUserData);
end;
