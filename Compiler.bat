@echo off
REM Compile GestionGym en Release et copie le resultat dans le dossier "publication".
REM Prerequis : Visual Studio 2019/2022 (charge de travail "Developpement .NET Desktop") ou le SDK .NET (dotnet).
cd /d "%~dp0"
dotnet publish GestionGym.csproj -c Release -o publication
if errorlevel 1 (
  echo.
  echo *** ERREUR DE COMPILATION : copie le message ci-dessus et envoie-le moi. ***
  pause
  exit /b 1
)
echo.
echo Termine. L'application est dans le dossier "publication" : GestionGym.exe
pause
