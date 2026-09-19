@echo off
rem Dvojklikem otevře databázi v DB Browser for SQLite.
rem Když nainstalovaný není, vypíše, co v databázi je, a jak ho doinstalovat.

set DB=H:\Můj disk\bakalarka\Data z brýlí\bakalarka.db

if not exist "%DB%" (
  echo Databaze jeste neexistuje. Spust nejdriv "Vytvorit databazi.bat".
  pause
  exit /b 1
)

set PROHLIZEC=%ProgramFiles%\DB Browser for SQLite\DB Browser for SQLite.exe
if exist "%PROHLIZEC%" (
  start "" "%PROHLIZEC%" "%DB%"
  exit /b 0
)

echo DB Browser for SQLite neni nainstalovany.
echo Nainstalujes ho prikazem:
echo.
echo     winget install DBBrowserForSQLite.DBBrowserForSQLite
echo.
echo Zatim vypis toho, co v databazi je:
echo.
python "%~dp0dotaz.py"
pause
