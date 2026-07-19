# FH6 Aftermarket Watcher

FH6 애프터마켓에서 목표 차량을 화면 판독으로 찾고, 목표가 없을 때만
`한국어 ↔ English US` 언어 변경을 이용해 클라이언트를 재시작하는 로컬 도구입니다.

현재 단계는 사용자용 GUI, 화면 관찰 기능, **오픈월드 실전 시작 1단계**까지 구현돼 있습니다.
GUI는 사용자가 승인한 첫 지도 진입 Enter만 허용하며, 언어 변경과 반복 재시작은 계속 잠겨
있습니다.

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
- 시작·일시정지·중지, 판독 목록, 재시도/중복 수, 세션 로그를 제공하는 WinForms GUI
- 겹친 초록 차량 아이콘 군집과 지도 차량 카드의 해상도 정규화 판독

## GUI 실전 시작

사용자용 실행 파일은 다음 위치에 생성됩니다.

```text
src\Fh6Aftermarket.Gui\bin\Release\net10.0-windows\Fh6Aftermarket.Gui.exe
```

1. GUI를 실행합니다.
2. FH6를 영어 오픈월드 정차 상태로 두고 전경에서 F1을 누릅니다.
3. GUI가 `Esc → Enter`로 일시정지 메뉴를 거쳐 월드 맵을 엽니다.
4. 지도 화면을 확인할 때까지 후속 입력 없이 기다립니다.
5. 언제든 F2로 세션을 끝냅니다.

FH6가 전경이 아니면 캡처만 대기하며, OCR이 세 번 연속 불확실하면 `확인 필요` 상태가 됩니다.
커서를 조정한 뒤 `재개`를 누르면 됩니다. 세션 기록은 Git에서 제외된 `logs` 폴더에 저장됩니다.

실전 1회 빌드는 영어 오픈월드 정차 상태에서 시작합니다. FH6를 전경에 둔 채 F1을 누르면
정확한 창 제목과 16:9 화면을 확인하고 `Esc → Enter`로 월드 맵만 엽니다. 새 화면을 확인하기
전에는 후속 입력을 보내지 않습니다.

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

실제 차량 화면 OCR에는 Scoop의 Tesseract 본체와 언어 데이터가 필요합니다.

```powershell
scoop install tesseract
scoop install tesseract-languages
```

저장된 애프터마켓 화면에서 판매 배너를 세고 차량명을 판독할 수 있습니다. 앱은 Scoop
언어 데이터 경로를 명시적으로 전달하므로 전역 `TESSDATA_PREFIX` 설정은 필요하지 않습니다.

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

## 다음 단계

1. 오픈월드 F1에서 월드 맵이 실제로 열리는지 확인
2. 월드 맵 앵커가 확인된 뒤에만 Aftermarket 장소 필터 단계 연결
3. 빠른 이동과 차량 3대 OCR을 한 실전 사이클로 연결
4. 목표가 없을 때만 English US→KOR 재시작 단계 연결
5. 실제 환경 오류를 로그로 수집해 포커스·해상도 복구 동작 보강

자세한 수동 흐름은 [docs/manual-flow.md](docs/manual-flow.md), 화면 인식 계획은
[docs/vision-anchors.md](docs/vision-anchors.md), 사용자 검증 기준은
[docs/validation-plan.md](docs/validation-plan.md)를 참고합니다.
