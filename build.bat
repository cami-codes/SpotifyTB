@echo off
rem Gera dist\SpotifyTB.exe (arquivo único, pequeno).
rem Precisa do .NET 8 Desktop Runtime para rodar.
setlocal
cd /d "%~dp0"

echo Compilando SpotifyTB...
dotnet publish SpotifyTB.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 (
  echo.
  echo Falha na compilacao. Veja as mensagens acima.
  pause
  exit /b 1
)

echo.
echo Pronto: %cd%\dist\SpotifyTB.exe
pause
