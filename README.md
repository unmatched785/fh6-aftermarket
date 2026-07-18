# FH6 Aftermarket Watcher

FH6 애프터마켓에서 목표 차량을 화면 판독으로 찾고, 목표가 없을 때만
`한국어 ↔ English US` 언어 변경을 이용해 클라이언트를 재시작하는 로컬 도구입니다.

현재 단계는 화면 관찰 기능과 **잠긴 1회 입력기**까지 구현돼 있습니다. 기본 설정에서는
실제 키 입력을 거부하며, 반복 실행과 마우스 입력은 아직 구현하지 않았습니다.

## 안전 원칙

- 지원 언어는 `KOR`와 `ENG`뿐입니다.
- 모든 화면 좌표는 1920×1080 게임 클라이언트 영역을 기준으로 정규화합니다.
- FHD, QHD, 4K의 16:9 화면만 허용합니다.
- 화면 상태나 OCR 결과가 불확실하면 재시작하지 않고 정지합니다.
- F2는 향후 모든 상태에서 작동하는 긴급 중단키로 사용합니다.
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

1. English US→KOR 1회 흐름을 실화면에서 검증
2. 재시작 후 지도 복귀 흐름에 화면 앵커 대기 추가
3. 1회 실행기를 실제 화면에서 재검증
4. 제한 횟수 반복과 긴급 중단

자세한 수동 흐름은 [docs/manual-flow.md](docs/manual-flow.md), 화면 인식 계획은
[docs/vision-anchors.md](docs/vision-anchors.md)를 참고합니다.
