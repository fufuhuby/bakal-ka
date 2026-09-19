@echo off
rem Dvojklikem postaví databázi z CSV, která leží na Disku.
rem Parametr --mysql navíc vysype SQL dump pro MAMP / phpMyAdmin.
python "%~dp0databaze.py" %*
pause
