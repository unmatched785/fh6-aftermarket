# KOR→ENG 1회 재시작 검증

- 검증일: 2026-07-19
- 게임 창: Forza Horizon 6
- 클라이언트 영역: 2560×1440 (16:9)
- 시작 언어 상태: 시스템 언어(한국어 UI)
- 목표 언어: English US

## 결과

각 키 입력 뒤 화면을 다시 캡처해 선택 위치를 확인했습니다. 언어 목록의 실제 시작점은
`시스템 언어`였고 `English US`까지는 아래 방향키 5회였습니다. `English US`에서 Enter를
눌러야 재시작 확인창이 열리며, 기본 선택 `아니요`에서 아래 방향키 1회로 `예`에 도달합니다.

마지막 Enter 뒤 기존 창 ID `329118`이 사라졌고 새 창 ID `395004`가 생성됐습니다. 새 창은
스플래시, Turn 10 Studios 로딩을 거쳐 `Enter Start Game`과
`Accessibility/Settings`가 표시된 영어 시작 화면에 도달했습니다.

## 확정된 키 순서

```text
Escape
Down
Right
Enter
Up ×2
Enter
Down ×5
Enter
Down
Enter
```

이 검증은 KOR(시스템 언어)→English US 한 방향에만 적용됩니다. ENG→KOR, 재시작 뒤 지도
진입, 반복 루프는 별도 실화면 검증 전까지 실행 준비 상태로 취급하지 않습니다.
