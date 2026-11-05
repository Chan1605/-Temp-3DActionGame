@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem ThirdParty 에셋을 .meta까지 그대로 private 레포에 올리고, 메인 레포에는 서브모듈로 연결
set "ASSET_REPO=https://github.com/Chan1605/3DActionGame-ThirdParty.git"

where git >nul 2>nul
if errorlevel 1 (
  echo [오류] git 명령이 없습니다. https://git-scm.com 에서 Git for Windows를 설치한 뒤 다시 실행하세요.
  pause
  exit /b 1
)
git lfs version >nul 2>nul
if errorlevel 1 (
  echo [오류] Git LFS가 없습니다. Git for Windows를 기본 옵션으로 다시 설치하면 함께 설치됩니다.
  pause
  exit /b 1
)
if exist ".gitmodules" (
  findstr /c:"Assets/ThirdParty" ".gitmodules" >nul && (
    echo 이미 서브모듈로 연결돼 있습니다. 할 일이 없습니다.
    pause
    exit /b 0
  )
)
if not exist "Assets\ThirdParty" (
  echo [오류] Assets\ThirdParty 폴더가 없습니다.
  pause
  exit /b 1
)

echo.
echo ThirdParty 폴더 크기:
powershell -NoProfile -Command "'{0:N2} GB' -f ((Get-ChildItem 'Assets/ThirdParty' -Recurse -File | Measure-Object Length -Sum).Sum/1GB)"
echo GitHub 무료 LFS 한도는 저장 10GB, 월 대역폭 10GB 입니다.
echo.
echo 먼저 GitHub에서 Private 레포 "3DActionGame-ThirdParty" 를 README 없이 빈 레포로 만들어 두세요.
echo 준비됐으면 아무 키나 누르세요. 중단하려면 창을 닫으세요.
pause >nul

rem ===== 1. 에셋 레포 만들고 푸시 =====
pushd "Assets\ThirdParty"
if not exist ".git" (
  git init -b main
  copy /y "..\..\.gitattributes" ".gitattributes" >nul
  git lfs install
  echo 파일을 커밋하는 중... 몇 분 걸릴 수 있습니다.
  git add .
  git commit -q -m "ThirdParty assets"
  if errorlevel 1 (
    echo [오류] 커밋 실패. git config --global user.name / user.email 설정을 확인하세요.
    popd
    pause
    exit /b 1
  )
)
git remote get-url origin >nul 2>nul
if errorlevel 1 git remote add origin %ASSET_REPO%

rem [변경] 일반 git 업로드가 1GB를 넘어 GitHub에서 408 시간 초과 → 1MB 넘는 파일을 전부 LFS로 옮겨 일반 업로드를 작게 만듦 (아직 푸시 전이라 기록 재작성 안전)
if not exist ".git\lfs-migrated" (
  echo 1MB 넘는 파일을 LFS로 옮기는 중... 몇 분 걸릴 수 있습니다.
  git lfs migrate import --above=1MB --everything --yes
  if errorlevel 1 (
    echo [오류] LFS 변환 실패. 화면을 캡처해서 보내주세요.
    popd
    pause
    exit /b 1
  )
  type nul > ".git\lfs-migrated"
)
git config http.postBuffer 524288000
echo 업로드 중... 로그인 창이 뜨면 GitHub 계정으로 로그인하세요.
git push -u origin main
if errorlevel 1 (
  echo [오류] 푸시 실패. 레포 주소와 이름, 100MB 넘는 비 LFS 파일이 있는지 확인하세요. 다시 실행하면 이어서 진행합니다.
  popd
  pause
  exit /b 1
)
popd

rem ===== 2. 메인 레포: ignore 해제 후 서브모듈 등록 =====
powershell -NoProfile -Command "$p='.gitignore'; $l=[IO.File]::ReadAllLines($p) | Where-Object { $_ -notmatch '^/\[Aa\]ssets/ThirdParty' }; [IO.File]::WriteAllLines((Resolve-Path $p),$l,(New-Object Text.UTF8Encoding $false))"
git submodule add %ASSET_REPO% Assets/ThirdParty
if errorlevel 1 (
  echo [오류] 서브모듈 등록 실패.
  pause
  exit /b 1
)
git add .gitignore .gitmodules Assets/ThirdParty Assets/ThirdParty.meta
git commit -m "ThirdParty를 private 서브모듈로 연결" -- .gitignore .gitmodules Assets/ThirdParty Assets/ThirdParty.meta
git push

echo.
echo 완료! 다른 PC에서는 아래처럼 클론하면 에셋까지 받아집니다.
echo   git clone --recursive https://github.com/Chan1605/-Temp-3DActionGame.git
pause
