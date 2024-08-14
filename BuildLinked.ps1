# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/CustomTags_HeatSuit/*" -Force -Recurse
dotnet publish "./CustomTags_HeatSuit.csproj" -c Release -o "$env:RELOADEDIIMODS/CustomTags_HeatSuit" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location