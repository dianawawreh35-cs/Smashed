; The Agent App's installer (N-11, S-63). Built by publish.ps1, which passes
; the version and the published folder:
;
;   ISCC.exe /DAppVersion=0.4.1 /DSourceDir=<publish\agent-app-0.4.1> /DOutputDir=<publish> installer.iss
;
; Agents download the result from the web app (Agent App page) and double-click
; it. It installs into C:\SmashedAgentApp, where the laptops have run the app
; from the start, so a laptop set up from the old zip is upgraded in place and
; its shortcut keeps working.
;
; No administrator rights needed (PrivilegesRequired=lowest): the folder is the
; agent's own, the shortcuts go on the signed-in user's desktop, and Windows
; lists it under that user's installed apps.
;
; The app's own data (the offline call queue, the block list, the logs) is under
; %LOCALAPPDATA%\CallCenter and is never touched: not by an upgrade, not by
; uninstalling.

#ifndef AppVersion
  #error Pass the version: /DAppVersion=0.4.1
#endif
#ifndef SourceDir
  #error Pass the published folder: /DSourceDir=...
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
; lzma2/fast: 20 s and 68 MB from the 185 MB folder, where lzma2/max took six
; minutes for 57 MB (27 Sep). 11 MB more over the LAN is nothing.
#ifndef Compression
  #define Compression "lzma2/fast"
#endif

[Setup]
; Never change this id: it is how Windows knows a new version replaces the old.
AppId={{9E5444E9-1060-41BE-AF17-000942C40DE5}
AppName=Smashed Agent App
AppVersion={#AppVersion}
AppVerName=Smashed Agent App {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName=C:\SmashedAgentApp
; Always there, never asked: the folder is cleared on each upgrade (below), so
; it must never be one that holds anything else.
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=SmashedAgentApp-Setup-{#AppVersion}
SetupIconFile={#SourcePath}\..\..\src\CallCenter.AgentApp\Assets\app.ico
UninstallDisplayIcon={app}\CallCenter.AgentApp.exe
UninstallDisplayName=Smashed Agent App
WizardStyle=modern
Compression={#Compression}
SolidCompression=yes
; An app still open is closed first, after asking; otherwise its files cannot
; be replaced. It is not restarted: the finish page offers to start it.
CloseApplications=yes
RestartApplications=no
; Arabic when Windows is in Arabic, English otherwise.
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ar"; MessagesFile: "compiler:Languages\Arabic.isl"

[InstallDelete]
; The previous version's files, all of them. A self-contained .NET app is a
; folder of ~190 runtime files, and one left over from an older version can be
; loaded by mistake. The app writes nothing here (its data is in LOCALAPPDATA).
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Smashed Agent App"; Filename: "{app}\CallCenter.AgentApp.exe"
Name: "{autoprograms}\Smashed Agent App"; Filename: "{app}\CallCenter.AgentApp.exe"

[Run]
Filename: "{app}\CallCenter.AgentApp.exe"; Description: "{cm:LaunchProgram,Smashed Agent App}"; Flags: nowait postinstall skipifsilent
