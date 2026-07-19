# FH6 Aftermarket Watcher

FH6 애프터마켓에서 목표 차량을 화면 판독으로 찾고, 목표가 없을 때만
`한국어 ↔ English US` 언어 변경을 이용해 클라이언트를 재시작하는 로컬 도구입니다.

비목표 차량 3대가 확인되면 `한국어 ↔ English US` 언어 변경으로 클라이언트를 재시작하고,
시작 화면을 지나 오픈월드까지 복귀한 뒤 다음 회차를 이어갑니다. 목표 차량을 발견하면
경고음을 내고 입력을 멈춥니다.

## 안전 원칙

- 지원 언어는 `KOR`와 `ENG`뿐입니다.
- 모든 화면 좌표는 1920×1080 게임 클라이언트 영역을 기준으로 정규화합니다.
- FHD, QHD, 4K의 16:9 화면만 허용합니다.
- 화면 상태나 OCR 결과가 불확실하면 제한 횟수만 재시도한 뒤 사용자 확인 상태로 전환합니다.
- 중복 차량명은 커서 문제일 수 있으므로 실패나 종료로 취급하지 않습니다.
- F2는 전역 등록과 50ms 키 상태 감시를 함께 사용하는 긴급 중단키입니다.
- 원본 캡처와 실행 로그는 개인정보 보호를 위해 Git에 포함하지 않습니다.
- `practicalStartEnabled`와 전체 `automationEnabled`를 분리해 현재 단계만 입력을 허용합니다.

## 현재 포함된 것

- 사용자가 검증한 KOR→ENG 및 ENG→KOR 재시작 키 순서
- 재시작 후 지도 필터 적용 키 순서
- 16:9 좌표 정규화 모델
- 설정 파일 구조 및 자체 검사 프로그램
- 화면 앵커와 다음 구현 단계 문서
- 여섯 한정 차량의 정식명·화면 축약명 사전과 OCR 오인식 허용 매처
- 녹색 판매 배너 검출, 차량명 영역 분리, Tesseract OCR
- `TargetFound` / `Clear` / `Uncertain` 안전 판정
- 실화면 검증을 통과한 KOR(시스템 언어)→English US 1회 재시작 흐름
- 매 키 전 정확한 창 제목·16:9·F2를 검사하는 1회 입력기
- 시작·일시정지·중지, 판독 목록, 재시도/중복 수, 세션 로그를 제공하는 콤팩트 WinForms GUI
- 항상 위 고정, 현재 단계, 단계별 남은 초, 최근 로그 표시
- 입력·화면 전환·빠른 이동·재시작·재시작 후 화면을 목적별로 묶은 GUI 타이밍 설정
- 겹친 초록 차량 아이콘 군집과 지도 차량 카드의 해상도 정규화 판독
- 세 차량 아이콘 자동 호버·OCR, 목표 발견 경고음, 비목표 시 ENG→KOR 1회 재시작
- v0.2.1: Tesseract의 특정 사용자 Scoop 경로 고정을 제거하고 표준 설치 위치 자동 탐색과
  한국어·영어 OCR 언어 데이터 동봉을 추가
- v0.2.0: 빠른 이동 뒤 차량 이동을 제거하고, 겹친 아이콘을 1시·좌하단·상단 순서로
  호버하는 고정 배치를 3840×2160 실게임에서 검증

## GUI 실전 시작

사용자용 패키지는 [GitHub Releases](https://github.com/unmatched785/fh6-aftermarket/releases)에서
받을 수 있습니다. ZIP을 원하는 폴더에 풀고 다음 파일을 실행합니다.

```text
Fh6Aftermarket.Gui.exe
```

Windows 10/11 x64와 FH6의 16:9 FHD/QHD/4K 화면을 지원합니다. 차량명 OCR을 위해
Tesseract 실행 파일이 필요하며, v0.2.1 ZIP에는 `eng+kor` 언어 데이터가 포함됩니다.
앱은 동봉 경로, 환경 변수, 실제 Scoop 루트, Program Files, PATH 순서로 설치 위치를 찾습니다.

1. GUI를 실행합니다.
2. GUI에서 타이밍 값을 PC 환경에 맞게 조정합니다. 재시작 후 첫 Enter 대기는
   기본 15초, 두 번째 Enter 뒤 오픈월드 대기는 기본 30초입니다. 클라이언트 재시작은
   기본 60초이며, 오픈월드 진입 후 `M`까지의 별도 대기는 기본 10초입니다.
   나머지 기본값은 입력 850ms, 화면 전환 2.5초, 빠른 이동 15초입니다.
3. FH6를 KOR 또는 ENG 오픈월드 정차 상태로 두고 전경에서 F1을 누릅니다.
4. 화면 전환 설정에서 파생된 1~2초 준비 대기 후 GUI가 `M`으로 월드 맵에 들어갑니다.
   시작할 때는 언어를 판별하지 않습니다.
5. Evolving World 필터에서 지구본을 찾아 빠른 이동한 뒤 이동하지 않고 그 자리에서
   다시 지도를 엽니다. 빠른 이동 지점과 차량 아이콘 군집이 겹치는 고정 배치를 사용합니다.
6. `M`으로 지도를 열고 맨 위 `All` 기준으로
   `PageDown → Enter ×2 → Down ×26 → Enter`를 실행합니다.
7. 지도를 다시 열어 최대 확대하고 초록 Aftermarket 차량 아이콘 군집을 확인한 뒤,
   플레이어 화살표를 피해 1시 방향부터 마우스 호버로 차량 카드를 표시합니다.
8. 목표가 없으면 언어를 바꿔 재시작한 뒤 `Enter → 대기 → Enter → 대기 → Esc → 짧은 대기 → M`으로
   오픈월드에 복귀하고 다음 회차를 시작합니다. 별도의 FH6 포커스 대기 단계는 없습니다.
9. 언제든 F2로 세션을 끝냅니다.

FH6가 전경이 아니면 캡처만 대기하며, OCR이 세 번 연속 불확실하면 `확인 필요` 상태가 됩니다.
커서를 조정한 뒤 `재개`를 누르면 됩니다. 세션 기록은 Git에서 제외된 `logs` 폴더에 저장됩니다.

실전 1회 빌드는 KOR/ENG 오픈월드 정차 상태에서 시작합니다. FH6를 전경에 둔 채 F1을 누르면
정확한 창 제목과 16:9 화면을 확인하고, 지구본 빠른 이동 지점에서 움직이지 않은 채
Aftermarket Cars만 활성화한 지도를 최대 확대합니다.
목표 차량이 없을 때만 마지막에 메뉴를 열어 ENG/KOR를 판별하고 반대 언어로 변경합니다.

이 PC의 라이브 시험에서는 게임 표시 화면과 별개로 Windows 캡처 표면이 3840×2160으로
제공됐습니다. GUI는 게임 설정의 표기 해상도가 아니라 실제 캡처 크기를 기준으로 좌표를
정규화합니다.

## 로컬 검사

이 PC에서는 Scoop SDK를 사용합니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe build .\Fh6Aftermarket.slnx
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\tests\Fh6Aftermarket.SelfTest\Fh6Aftermarket.SelfTest.csproj
```

수동 흐름을 사람이 읽을 수 있는 형태로 확인할 수도 있습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --config .\config\workflow.json --print-flow kor-to-eng
```

저장된 스크린샷을 입력 없이 판독할 수 있습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --inspect-image .\captures\sample.png
```

현재 전경 창을 PNG로 저장하고 같은 판독을 수행할 수도 있습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --capture-foreground .\captures\foreground.png
```

OCR에서 얻었다고 가정한 텍스트를 목표 목록과 대조할 수 있습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --targets .\config\targets.json --match-text "Lambo Sesto"
```

실제 차량 화면 OCR에는 Tesseract 실행 파일이 필요합니다. 릴리스 ZIP에는 영어와 한국어
데이터만 이미 포함되어 있으므로 별도 언어팩을 설치할 필요가 없습니다.

```powershell
scoop install tesseract
```

Scoop이 전체 `tesseract-languages` 패키지를 제안해도 설치하지 마세요. 여기에는 스페인어를
비롯한 불필요한 언어가 모두 들어 있습니다. 소스 체크아웃에서 직접 실행할 때는 릴리스 ZIP의
`tessdata` 폴더를 사용하거나, 영어 `eng.traineddata`와 한국어 `kor.traineddata` 두 파일만
있는 폴더를 `FH6_TESSDATA_DIR`로 지정합니다.

표준 Program Files 설치와 사용자 지정 `SCOOP` 루트도 자동으로 찾습니다. 그래도 찾지 못하는
특수 설치는 `FH6_TESSERACT_EXE`에 `tesseract.exe` 전체 경로를, `FH6_TESSDATA_DIR`에
`eng.traineddata`와 `kor.traineddata`가 들어 있는 폴더를 지정할 수 있습니다. 시작 실패 창에는
앱이 확인한 경로와 이 두 환경 변수 이름이 함께 표시됩니다.

저장된 애프터마켓 화면에서 판매 배너를 세고 차량명을 판독할 수 있습니다. 앱은 선택한
영어·한국어 데이터 경로를 명시적으로 전달하므로 전역 `TESSDATA_PREFIX` 설정은 필요하지 않습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --analyze-aftermarket-image .\captures\aftermarket.png --targets .\config\targets.json
```

전경 게임 창을 입력 없이 감시할 수도 있습니다. 창 제목에 `Forza`가 포함되고 클라이언트가
16:9일 때만 OCR을 실행합니다. 목표를 찾거나, 판매 배너가 보이는데 OCR이 불완전하거나,
동일한 `Clear` 판정이 2회 연속 나오면 종료합니다. `Ctrl+C`로 언제든 끝낼 수 있습니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --watch-foreground --targets .\config\targets.json --title-contains Forza --max-samples 120
```

## 잠긴 언어 변경 입력기

KOR(시스템 언어)→English US 흐름은 QHD 실화면에서 키마다 확인했습니다. 다만
`config/safety.json`의 `automationEnabled` 값이 `false`이므로 아래 명령은 현재 입력을
보내지 않고 거부됩니다. 라이브 검증을 다시 시작할 때만 설정을 명시적으로 바꾸고, 정확한
Forza 창이 전경에 있는 상태에서 사용합니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --run-one-shot --config .\config\workflow.json --flow kor-to-eng --safety .\config\safety.json --acknowledge-live-input FH6_ONE_SHOT
```

실행기는 `Forza Horizon 6` 정확한 제목, 16:9 클라이언트, F2 비활성 상태를 매 키 전에
재확인하고 키 사이에 750ms의 여유를 둡니다. 하나라도 달라지면 즉시 중단하고, 재시작
Enter 뒤에는 새 창에 입력하지 않습니다.

## 검증 환경

- FHD 60Hz, 120Hz, 144Hz
- SSD 환경
- HDD 로딩 시간은 보장하지 않으며 GUI의 재시작·로딩 값을 늘려 조정할 수 있습니다.

자세한 수동 흐름은 [docs/manual-flow.md](docs/manual-flow.md), 화면 인식 계획은
[docs/vision-anchors.md](docs/vision-anchors.md), 사용자 검증 기준은
[docs/validation-plan.md](docs/validation-plan.md)를 참고합니다.
