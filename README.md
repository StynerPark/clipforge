# ClipForge

Made with OpenAI Codex.
OpenAI Codex를 이용해서 제작했습니다.

Portable Windows tool for turning video clips, extracted frames, crops, watermarks, and screen recordings into GIF/WebP animations.
비디오 구간, 추출 프레임, 크롭, 워터마크, 화면 녹화를 GIF/WebP 움짤로 만드는 포터블 Windows 도구입니다.

## Features / 기능

Drag a video into the Original Video panel, scrub freely, set start/end points, and extract frames from the original frame stream.
Original Video 영역에 영상을 드래그하고 자유롭게 이동한 뒤 시작/끝점을 지정해 원본 프레임 기준으로 추출합니다.

Keep extracted frames in a persistent list, append frames from additional videos or recordings, change durations, and delete selected/all frames.
추출된 프레임은 유지되며 다른 영상이나 녹화 프레임을 이어 붙이고, 지속시간 변경 및 선택/전체 삭제가 가능합니다.

Preview the result, crop with fixed ratios, set output resolution, add image/text watermarks, and export Frame ZIP, GIF, or WebP.
결과를 미리보고 고정 비율 크롭, 출력 해상도, 이미지/텍스트 워터마크를 적용해 Frame ZIP, GIF, WebP로 출력합니다.

Record the whole screen or a selected screen region and append the captured frames to the frame list.
전체 화면 또는 선택한 화면 영역을 녹화해 캡처 프레임을 프레임 목록에 추가합니다.

## Button Guide / 버튼 기능

| Button | English | 한국어 |
|---|---|---|
| Clear Cache | Deletes ClipForge temporary files. | ClipForge 임시 파일을 삭제합니다. |
| -5s / -1s / +1s / +5s | Moves the video position by the selected time step. | 영상 위치를 선택한 시간 단위로 이동합니다. |
| Prev Frame / Next Frame | Steps through the source video frame by frame. | 원본 영상을 프레임 단위로 이동합니다. |
| Play/Pause | Plays or pauses the source video. | 원본 영상 재생/정지를 전환합니다. |
| Set Start / Set End | Sets the extraction segment start or end point. | 추출할 구간의 시작점/끝점을 설정합니다. |
| Extract Frames / Stop | Starts or cancels frame extraction. | 프레임 추출을 시작하거나 중지합니다. |
| Select/Unselect All | Toggles selection for all extracted frames. | 추출 프레임 전체 선택/해제를 전환합니다. |
| Change Duration | Changes duration for selected frames. | 선택된 프레임의 지속시간을 변경합니다. |
| Delete Selected / Delete All | Removes selected frames or clears all frames. | 선택 프레임 삭제 또는 전체 프레임 삭제를 수행합니다. |
| Preview Play/Pause | Plays or pauses the result preview. | 결과 미리보기 재생/정지를 전환합니다. |
| Crop / Reset | Opens crop selection or resets the crop. | 크롭 선택창을 열거나 크롭을 초기화합니다. |
| Insert / Reset | Opens image/text watermark editing or removes all watermarks. | 이미지/텍스트 워터마크 편집창을 열거나 워터마크를 전체 제거합니다. |
| Record Area / Full Record | Records a selected screen area or the full screen. | 선택 영역 또는 전체 화면을 녹화합니다. |
| Frame ZIP / GIF / WebP | Exports frames or the final animation. | 프레임 묶음 또는 최종 움짤을 출력합니다. |

## Crop / 크롭

Free mode lets you drag any crop area; ratio modes create a centered fixed-ratio box that can be moved or resized inside the image bounds.
Free는 자유 영역 선택이며, 비율 모드는 이미지 중앙에 고정 비율 박스를 만들고 이미지 범위 안에서 이동/크기 조절합니다.

## Watermark / 워터마크

Drag image files into the editor, double-click the canvas to add text, resize boxes with the corner handle, and press Delete to remove selected items.
편집창에 이미지를 드래그하고, 캔버스를 더블클릭해 텍스트를 추가하며, 모서리 핸들로 크기 조절하고 Delete로 삭제합니다.

## Portable Build / 포터블 빌드

The release ZIP is self-contained for Windows x64 and includes FFmpeg and LibVLC runtime files.
릴리즈 ZIP은 Windows x64 포터블 패키지이며 FFmpeg와 LibVLC 런타임 파일을 포함합니다.

## Source / 소스

Built with .NET 8 WPF, LibVLCSharp, FFmpeg, and System.Drawing for screen capture.
.NET 8 WPF, LibVLCSharp, FFmpeg, System.Drawing 화면 캡처 기반으로 제작되었습니다.
