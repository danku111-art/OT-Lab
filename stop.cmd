@echo off
rem Zatrzymuje trzy programy OT-Lab uruchomione przez start.cmd.
rem
rem Procesy wybieramy po nazwie pliku exe, nigdy po tytule okna. Filtr
rem "WINDOWTITLE eq OT-Lab*" pasuje tez do okna Eksploratora z folderem OT-Lab
rem (proces explorer.exe, czyli pulpit i pasek zadan) oraz do przegladarki
rem z otwartym HMI - taskkill /F zamknalby je razem z naszymi programami.
for %%P in (OtLab.Gateway.exe OtLab.Plc.exe OtLab.Motor.exe) do (
    taskkill /IM %%P /F >nul 2>nul && echo Zatrzymano %%P
)
echo Gotowe.
