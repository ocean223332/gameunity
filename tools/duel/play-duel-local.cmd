@echo off
rem Double-click: two game windows side by side for a duel against yourself. Add -Build to rebuild first.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play-duel-local.ps1" %*
if errorlevel 1 pause
