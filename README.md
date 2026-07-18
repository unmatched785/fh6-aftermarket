# FH6 Aftermarket Watcher

FH6 애프터마켓에서 목표 차량을 화면 판독으로 찾고, 목표가 없을 때만
`한국어 ↔ English US` 언어 변경을 이용해 클라이언트를 재시작하는 로컬 도구입니다.

현재 단계는 **화면 관찰·검증 전용**입니다. 실제 키나 마우스 입력은 보내지 않습니다.

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

## 다음 단계

1. 전경 게임 창을 주기적으로 읽는 입력 없는 감시 모드
2. 목표 없는 실제 화면을 추가 확보해 `Clear` 완전성 판정 검증
3. 단 한 번만 실행되는 재시작 입력
4. 제한 횟수 반복과 긴급 중단

자세한 수동 흐름은 [docs/manual-flow.md](docs/manual-flow.md), 화면 인식 계획은
[docs/vision-anchors.md](docs/vision-anchors.md)를 참고합니다.
