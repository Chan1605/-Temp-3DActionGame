@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0Assets\ThirdParty"

rem LFS 변환 뒤 작업 폴더에 남은 포인터 파일(132바이트)을 실제 파일로 되돌림
echo Unity가 꺼져 있는지 확인하세요. 준비됐으면 아무 키나 누르세요.
pause >nul

git lfs install --local
echo 실제 파일로 복원하는 중... 몇 분 걸릴 수 있습니다.
git lfs pull
if errorlevel 1 (
  echo [오류] 복원 실패. 화면을 캡처해서 보내주세요.
  pause
  exit /b 1
)

echo.
echo 확인: 아래 크기가 132 바이트가 아니라 수십~수백 KB면 정상입니다.
powershell -NoProfile -Command "(Get-Item 'SwordAnimationPack/Animation/Humanoid/01_Idle/Idle.anim').Length"
echo 완료! 이 파일은 지워도 됩니다.
pause
