param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versione non valida' }
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet publish AppAvvio.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o artifacts/publish
if ($LASTEXITCODE -ne 0) { throw 'Publish fallita' }
if (-not (Test-Path artifacts/tools/vpk.exe)) {
    dotnet tool install vpk --tool-path artifacts/tools --version 1.2.161
    if ($LASTEXITCODE -ne 0) { throw 'Installazione vpk fallita' }
}
& ./artifacts/tools/vpk.exe pack --packId AppAvvio --packVersion $Version --packDir artifacts/publish --mainExe AppAvvio.exe --packTitle AppAvvio --packAuthors LoLler111 --releaseNotes release-notes.md --outputDir artifacts/releases --shortcuts StartMenuRoot --delta None --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw 'Creazione pacchetto fallita' }
