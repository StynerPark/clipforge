# ClipForge

Made with OpenAI Codex.
OpenAI Codex를 이용해서 제작했습니다.

ClipForge is a portable Windows tool for turning video clips, extracted frames, crops, watermarks, and screen recordings into GIF/WebP animations.
ClipForge는 비디오 구간, 추출 프레임, 크롭, 워터마크, 화면 녹화를 GIF/WebP 움짤로 만드는 Windows 포터블 도구입니다.

## Features / 기능

Drag a video into the Original Video panel, scrub freely, set start/end points, and extract frames from the original frame stream.
Original Video 영역에 영상을 드래그하고 자유롭게 이동한 뒤 시작/끝점을 지정해 원본 프레임 기준으로 추출합니다.

Keep extracted frames in a persistent list, append frames from additional videos or recordings, change durations, and delete selected/all frames.
추출한 프레임 목록을 유지하고 다른 영상이나 녹화 프레임을 이어 붙이며, 지속시간 변경과 선택/전체 삭제를 할 수 있습니다.

Preview the result, crop with free or fixed-ratio selection, set output resolution, add image/text watermarks, and export Frame ZIP, GIF, or WebP.
결과를 미리보고 자유/고정 비율 크롭, 출력 해상도, 이미지/텍스트 워터마크를 적용해 Frame ZIP, GIF, WebP로 출력합니다.

Record the whole screen or a selected screen region and append the captured frames to the frame list.
전체 화면 또는 선택한 화면 영역을 녹화하고 캡처 프레임을 프레임 목록에 추가합니다.

## Button Guide / 버튼 기능

| Button | English | 한국어 |
|---|---|---|
| Clear Cache | Deletes ClipForge temporary files. | ClipForge 임시 파일을 삭제합니다. |
| -5s / -1s / +1s / +5s | Moves the video position by the selected time step. | 영상 위치를 선택한 시간 단위로 이동합니다. |
| Prev Frame / Next Frame | Steps through the source video frame by frame. | 원본 영상을 프레임 단위로 이동합니다. |
| Play/Pause | Plays or pauses the source video. | 원본 영상 재생/정지를 전환합니다. |
| Set Start / Set End | Sets the extraction segment start or end point. | 추출 구간의 시작점/끝점을 설정합니다. |
| Extract Frames / Stop | Starts or cancels frame extraction. | 프레임 추출을 시작하거나 중지합니다. |
| Select/Unselect All | Toggles selection for all extracted frames. | 추출 프레임 전체 선택/해제를 전환합니다. |
| Change Duration | Shows source FPS, original frame duration, presets, and changes duration for selected frames. | 원본 FPS와 원본 지속시간, 프리셋을 보고 선택 프레임 지속시간을 변경합니다. |
| Delete Selected / Delete All | Removes selected frames or clears all frames. | 선택 프레임 삭제 또는 전체 프레임 삭제를 수행합니다. |
| Preview Play/Pause | Plays or pauses the result preview. | 결과 미리보기 재생/정지를 전환합니다. |
| Crop / Reset | Opens crop selection or resets the crop. | 크롭 선택창을 열거나 크롭을 초기화합니다. |
| Insert / Reset | Opens image/text watermark editing or removes all watermarks. | 이미지/텍스트 워터마크 편집창을 열거나 워터마크를 전체 제거합니다. |
| Record Area / Full Record | Records a selected screen area or the full screen. | 선택 영역 또는 전체 화면을 녹화합니다. |
| Frame ZIP / GIF / WebP | Exports frames or the final animation. | 프레임 묶음 또는 최종 움짤을 출력합니다. |

## Recent Improvements / 최근 개선

Duration editing now shows the source FPS and original frame duration, with quick presets for Original, 2x, 0.5x, and 4x.
지속시간 변경창에서 원본 FPS와 원본 프레임 지속시간을 보여주고 Original, 2x, 0.5x, 4x 프리셋을 제공합니다.

Large frame lists are smoother because selection changes suppress expensive preview refreshes and the frame list uses virtualization.
대량 프레임 목록에서 선택 변경 중 무거운 미리보기 갱신을 막고 리스트 가상화를 적용해 동작이 부드러워졌습니다.

Extracted frame rows appear sooner because thumbnails load progressively after the list is created.
추출 프레임 행을 먼저 표시하고 썸네일은 이후 순차 로딩해 목록 표시 체감 속도를 개선했습니다.

The crop window now starts with no selection in Free mode, so dragging immediately creates a new crop area.
크롭 창은 Free 모드에서 기본 선택 영역 없이 열리며, 바로 드래그해서 새 크롭 영역을 만들 수 있습니다.

## Crop / 크롭

Free mode lets you drag any crop area. Fixed-ratio modes create a centered ratio box that can be moved or resized inside the image bounds.
Free 모드는 자유 영역 선택이며, 고정 비율 모드는 이미지 중앙에 비율 박스를 만들고 이미지 범위 안에서 이동/크기 조절합니다.

## Watermark / 워터마크

Drag image files into the editor, double-click the canvas to add text, resize boxes with the corner handle, and press Delete to remove selected items.
편집창에 이미지를 드래그하고 캔버스를 더블클릭해 텍스트를 추가하며, 모서리 핸들로 크기를 조절하고 Delete로 삭제합니다.

## Portable Build / 포터블 빌드

The release ZIP is self-contained for Windows x64 and includes FFmpeg and LibVLC runtime files.
릴리즈 ZIP은 Windows x64 포터블 패키지이며 FFmpeg와 LibVLC 런타임 파일을 포함합니다.

## Source / 소스

Built with .NET 8 WPF, LibVLCSharp, FFmpeg, and System.Drawing for screen capture.
.NET 8 WPF, LibVLCSharp, FFmpeg, System.Drawing 화면 캡처 기반으로 제작했습니다.
