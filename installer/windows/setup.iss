; GHS Markdown Editor — Inno Setup 6 installer script
; Compile with: iscc installer\windows\setup.iss

[Setup]
AppName=GHS Markdown Editor
AppVersion=1.0.1
AppPublisher=GHS
AppPublisherURL=https://github.com/michaeldhead/MarkDown-CrossPlatform
AppSupportURL=https://github.com/michaeldhead/MarkDown-CrossPlatform/issues
DefaultDirName={autopf}\GHS Markdown Editor
DefaultGroupName=GHS\GHS Markdown Editor
OutputDir=output
OutputBaseFilename=GHSMarkdownEditor-Setup-1.0.1
Compression=lzma2/ultra64
SolidCompression=yes
MinVersion=10.0
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\GhsMarkdown.Cross.exe
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "..\..\src\GhsMarkdown.Cross\bin\Release\net10.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GHS Markdown Editor"; Filename: "{app}\GhsMarkdown.Cross.exe"
Name: "{group}\Uninstall GHS Markdown Editor"; Filename: "{uninstallexe}"
Name: "{commondesktop}\GHS Markdown Editor"; Filename: "{app}\GhsMarkdown.Cross.exe"; Tasks: desktopicon

[Registry]
Root: HKCR; Subkey: ".md"; ValueType: string; ValueName: ""; ValueData: "GHSMarkdownFile"; Flags: uninsdeletevalue
Root: HKCR; Subkey: "GHSMarkdownFile"; ValueType: string; ValueName: ""; ValueData: "GHS Markdown Editor Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "GHSMarkdownFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GhsMarkdown.Cross.exe,0"
Root: HKCR; Subkey: "GHSMarkdownFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GhsMarkdown.Cross.exe"" ""%1"""

[Run]
Filename: "{app}\GhsMarkdown.Cross.exe"; Description: "Launch GHS Markdown Editor"; Flags: nowait postinstall skipifsilent
