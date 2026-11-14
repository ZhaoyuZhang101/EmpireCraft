# Publish the standalone server.
#   .\publish.ps1                      -> .server\publish\linux-x64   (copy this folder to the Linux box)
#   .\publish.ps1 -Runtime win-x64     -> .server\publish\win-x64
# Do NOT publish as a single file: the mod's Harmony patches need libclrjit next to the program,
# and none of them attach when it is packed into one file.
param([string]$Runtime = "linux-x64")
$ErrorActionPreference = "Stop"
$out = Join-Path $PSScriptRoot ("publish\" + $Runtime)
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $PSScriptRoot "EmpireCraft.Server.csproj") -c Release -r $Runtime --self-contained true -p:DebugType=none -o $out -v q -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ("published to " + $out)
Write-Host "run on the server:  ./EmpireCraftServer --data <mod folder> --state <state folder>"
