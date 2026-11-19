@echo off
rem Gera dist-standalone\SpotifyTB.exe (arquivo único, autônomo).
rem O arquivo fica maior porque inclui o runtime do .NET.
setlocal
cd /d "%~dp0"

echo Compilando SpotifyTB (autonomo)...
dotnet publish SpotifyTB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist-standalone
if errorlevel 1 (
  echo.
  echo Falha na compilacao. Veja as mensagens acima.
  pause
  exit /b 1
)

echo.
echo Pronto: %cd%\dist-standalone\SpotifyTB.exe
pause
