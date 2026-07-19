# FH6 Aftermarket Watcher

FH6 애프터마켓에서 목표 차량을 화면 판독으로 찾고, 목표가 없을 때만
`한국어 ↔ English US` 언어 변경을 이용해 클라이언트를 재시작하는 로컬 도구입니다.

현재 단계는 사용자용 **검증 GUI**, 화면 관찰 기능, **잠긴 1회 입력기**까지 구현돼 있습니다.
기본 설정에서는 실제 키·마우스 입력을 거부하며, GUI도 화면 판독만 수행합니다. 반복 재시작은
검증 기준을 통과한 뒤에 연결합니다.

## 안전 원칙

- 지원 언어는 `KOR`와 `ENG`뿐입니다.
- 모든 화면 좌표는 1920×1080 게임 클라이언트 영역을 기준으로 정규화합니다.
- FHD, QHD, 4K의 16:9 화면만 허용합니다.
- 화면 상태나 OCR 결과가 불확실하면 제한 횟수만 재시도한 뒤 사용자 확인 상태로 전환합니다.
- 중복 차량명은 커서 문제일 수 있으므로 실패나 종료로 취급하지 않습니다.
- F2는 전역 등록과 50ms 키 상태 감시를 함께 사용하는 긴급 중단키입니다.
- 원본 캡처와 실행 로그는 개인정보 보호를 위해 Git에 포함하지 않습니다.

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

## 검증 GUI

사용자용 실행 파일은 다음 위치에 생성됩니다.

```text
src\Fh6Aftermarket.Gui\bin\Release\net10.0-windows\Fh6Aftermarket.Gui.exe
```

현재 GUI는 **검증 전용**입니다. 게임 화면을 캡처하고 OCR할 뿐 게임에 키나 마우스를 보내지
않습니다.

1. GUI를 실행하고 `시작 / 새 검증`을 누릅니다.
2. FH6를 전경에 두고 Aftermarket Cars 필터와 최대 확대 상태로 이동합니다.
3. 겹친 초록 차량 아이콘을 하나씩 수동 선택합니다.
4. GUI가 차량명 세 개를 기록하는지 확인합니다.
5. 같은 차량을 다시 선택해도 중복 수만 증가하고 검증은 계속됩니다.
6. `일시정지`로 잠시 멈추거나 `중지`/F2로 세션을 끝냅니다.

FH6가 전경이 아니면 캡처만 대기하며, OCR이 세 번 연속 불확실하면 `확인 필요` 상태가 됩니다.
커서를 조정한 뒤 `재개`를 누르면 됩니다. 세션 기록은 Git에서 제외된 `logs` 폴더에 저장됩니다.

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

## 잠긴 1회 입력기

KOR(시스템 언어)→English US 흐름은 QHD 실화면에서 키마다 확인했습니다. 다만
`config/safety.json`의 `automationEnabled` 기본값이 `false`이므로 아래 명령도 현재는 입력을
보내지 않고 거부됩니다. 라이브 검증을 다시 시작할 때만 설정을 명시적으로 바꾸고, 정확한
Forza 창이 전경에 있는 상태에서 사용합니다.

```powershell
C:\Users\user\scoop\apps\dotnet-sdk\current\dotnet.exe run --project .\src\Fh6Aftermarket\Fh6Aftermarket.csproj -- --run-one-shot --config .\config\workflow.json --flow kor-to-eng --safety .\config\safety.json --acknowledge-live-input FH6_ONE_SHOT
```

실행기는 `Forza Horizon 6` 정확한 제목, 16:9 클라이언트, F2 비활성 상태를 매 키 전에
재확인하고 키 사이에 750ms의 여유를 둡니다. 하나라도 달라지면 즉시 중단하고, 재시작
Enter 뒤에는 새 창에 입력하지 않습니다.

## 다음 단계

1. 물리 F2를 FH6 전경에서 눌러 GUI 중지 로그가 남는지 확인
2. FHD/QHD/4K와 Windows 배율이 다른 PC에서 검증 GUI 반복 시험
3. English US→KOR 1회 흐름을 실화면에서 검증
4. 재시작 후 지도 복귀 흐름에 화면 앵커 대기 추가
5. 검증 기준 통과 후 제한 횟수 반복 입력 연결

자세한 수동 흐름은 [docs/manual-flow.md](docs/manual-flow.md), 화면 인식 계획은
[docs/vision-anchors.md](docs/vision-anchors.md), 사용자 검증 기준은
[docs/validation-plan.md](docs/validation-plan.md)를 참고합니다.
