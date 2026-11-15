@echo off
rem Uruchamia caly system OT-Lab na Windows: silnik, PLC i brame (kazdy w osobnym oknie),
rem a potem otwiera HMI w przegladarce. Zamkniecie: stop.cmd albo zamkniecie okien.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Nie znaleziono polecenia "dotnet". Zainstaluj .NET SDK 10: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Budowanie rozwiazania...
dotnet build OtLab.sln -c Release --nologo -v quiet
if errorlevel 1 (
    echo Budowanie nie powiodlo sie.
    pause
    exit /b 1
)

rem Kolejnosc jak w dokumencie: silnik, PLC, brama.
rem Brama musi wystartowac w ciagu 5 s po PLC, inaczej PLC zglosi "brak komunikacji z HMI".
start "OT-Lab Silnik" /D "%~dp0Motor" dotnet run --no-build -c Release
timeout /t 1 /nobreak >nul
start "OT-Lab PLC" /D "%~dp0Plc" dotnet run --no-build -c Release
timeout /t 1 /nobreak >nul
start "OT-Lab Brama" /D "%~dp0Gateway" dotnet run --no-build -c Release
timeout /t 3 /nobreak >nul

start "" http://localhost:8080
echo.
echo System uruchomiony. HMI: http://localhost:8080
echo W oknie "OT-Lab Silnik" klawisz P przelacza symulacje przegrzania.
