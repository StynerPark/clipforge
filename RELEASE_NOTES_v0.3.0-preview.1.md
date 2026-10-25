Made with OpenAI Codex.  
OpenAI Codex를 이용해서 제작했습니다.

# ClipForge v0.3.0-preview.1 — Image Patch Preview

![ClipForge v0.3.0-preview.1 interface](https://raw.githubusercontent.com/StynerPark/ClipForge/master/Assets/clipforge-v0.3.0-preview.1.png)

## English

### What’s new

- Added **Image Patch > Area**, a freehand editor for replacing a selected region with pixels from the same coordinates in an edited full-frame image.
- Added drag-and-drop and file selection for loading the edited image.
- Added same-resolution validation to prevent misaligned patch images.
- Added a composited preview, **Show original** comparison, redraw, reset, and `0–12 px` inside-edge feathering.
- Added **Apply to all frames** so one fixed patch can be used across the extracted frame list.
- Image Patch is reflected in Result Preview and GIF/WebP export.
- Original temporary frames and center-panel thumbnails remain unchanged.

### Recommended workflow

1. Extract the desired video segment.
2. Copy one PNG frame from the current `tmp` session folder.
3. Remove the logo or fixed element in an external editor and save the entire image at the original pixel size.
4. Open **Image Patch > Area**, draw the replacement boundary, and load the edited image.
5. Verify the composite, apply it to all frames, and export GIF or WebP.

### Preview limitations

- The patch is fixed and does not track motion or perform AI restoration.
- Moving people, hair, or objects passing through the selected area can be covered.
- Only one patch area is active at a time.
- Patch settings are session-only and must be set again after the app or working state is cleared.
- **Frame ZIP exports original extracted frames and does not include Image Patch.**
- GIF encode/decode and pixel-composite tests passed. WebP, cancel/reset/re-entry, crop/watermark combinations, and portability on another Windows x64 PC still require broader regression testing.

## 한국어

### 새로운 기능

- 수정한 전체 프레임 이미지에서 동일한 좌표의 픽셀을 가져와 자유 선택 영역만 교체하는 **Image Patch > Area** 기능을 추가했습니다.
- 수정 이미지 드래그 앤 드롭과 파일 선택 불러오기를 지원합니다.
- 해상도가 다른 이미지의 잘못된 적용을 막는 원본 크기 검증을 추가했습니다.
- 합성 미리보기, **원본 보기** 비교, 영역 다시 그리기, Reset, `0–12 px` 안쪽 경계 혼합을 추가했습니다.
- 하나의 고정 패치를 전체 추출 프레임에 사용하는 **모든 프레임에 적용** 기능을 추가했습니다.
- Image Patch 결과는 Result Preview와 GIF/WebP 출력에 반영됩니다.
- 임시 원본 프레임과 가운데 썸네일은 변경하지 않습니다.

### 권장 작업 순서

1. 원하는 영상 구간의 프레임을 추출합니다.
2. 현재 `tmp` 세션 폴더에서 PNG 한 장을 복사합니다.
3. 외부 편집기에서 로고나 고정 요소를 지우고 원본 픽셀 크기의 전체 이미지로 저장합니다.
4. **Image Patch > Area**를 열어 교체 영역을 그리고 수정 이미지를 불러옵니다.
5. 합성 결과를 확인하고 모든 프레임에 적용한 뒤 GIF 또는 WebP로 저장합니다.

### 프리뷰 제한사항

- 고정 이미지 패치이며 움직임 추적이나 AI 복원 기능은 아닙니다.
- 선택 영역을 지나가는 사람, 머리카락, 물체도 패치로 가려질 수 있습니다.
- 한 번에 하나의 패치 영역만 사용할 수 있습니다.
- 패치 설정은 현재 세션에서만 유지되며 앱 또는 작업 상태를 초기화하면 다시 지정해야 합니다.
- **Frame ZIP은 원본 추출 프레임을 저장하며 Image Patch를 포함하지 않습니다.**
- GIF 인코딩/디코딩과 픽셀 합성 시험은 통과했습니다. WebP, 취소/Reset/재진입, 크롭·워터마크 조합, 다른 Windows x64 PC 포터블 실행은 추가 회귀 검증이 필요합니다.

## Download / 다운로드

Download `ClipForge_Portable_v0.3.0-preview.1_image_patch_win-x64.zip`, extract it, and run `ClipForgeWpf.exe`.  
`ClipForge_Portable_v0.3.0-preview.1_image_patch_win-x64.zip`을 다운로드하고 압축을 푼 뒤 `ClipForgeWpf.exe`를 실행하세요.

**SHA-256**  
`36984ee318801c8b6a0184ad1fe6fbdb2dab1a3f1d2c8e61876778db09bb54cf`
