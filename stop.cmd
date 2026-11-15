@echo off
rem Zamyka okna uruchomione przez start.cmd (razem z procesami w srodku).
taskkill /FI "WINDOWTITLE eq OT-Lab*" /T /F >nul 2>nul
echo Zatrzymano procesy OT-Lab.
