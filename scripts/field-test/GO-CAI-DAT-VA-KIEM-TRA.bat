@echo off
rem Chuot phai file nay -> Run as administrator (chay SAU KHI da kiem thu thu cong xong)
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0kiem-thu-may-that.ps1" -GoCaiDat
