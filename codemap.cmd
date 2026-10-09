@echo off
rem CodeMap - live code map of the project. Double-click to open it in the browser.
cd /d "%~dp0"
where node >nul 2>nul || (echo Node.js 18+ is required: https://nodejs.org & pause & exit /b 1)
node tools\codemap\server.js --open
